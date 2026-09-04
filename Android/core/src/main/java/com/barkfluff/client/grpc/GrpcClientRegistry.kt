package com.barkfluff.client.grpc

import android.content.Context
import android.util.Log
import barkfluff.beacon.BeaconApiGrpcKt
import barkfluff.calls.CallsApiGrpcKt
import barkfluff.fast.auth.FastAuthApiGrpcKt
import barkfluff.files.FilesApiGrpcKt
import barkfluff.identity.IdentityApiGrpcKt
import barkfluff.messages.MessagesApiGrpcKt
import barkfluff.navigator.NavigatorApiGrpcKt
import barkfluff.onliner.OnlinerApiGrpcKt
import barkfluff.updates.UpdatesApiGrpcKt
import barkfluff.users.UsersApiGrpcKt
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.security.TlsTransportFactory
import io.grpc.Channel
import io.grpc.ClientInterceptor
import io.grpc.ClientInterceptors
import io.grpc.ManagedChannel
import java.util.EnumMap

/**
 * Small platform-free lifecycle primitive used to pin registry semantics in unit tests.
 * [GrpcClientRegistry] uses the same swap/terminal-shutdown rules for its typed entries.
 */
internal class ClientSlotRegistry<K, C>(
    private val endpointFor: (K) -> String,
    private val normalize: (String) -> String,
    private val clientFactory: (K, String) -> C,
    private val closeAction: (C) -> Unit = {},
) {
    private val lock = Any()
    private val slots = mutableMapOf<K, Pair<String, C>>()

    @Volatile
    private var shutdownRequested = false

    fun get(key: K): C? = synchronized(lock) {
        if (shutdownRequested) return null
        slots[key]?.second ?: run {
            val address = endpointFor(key)
            if (address.isBlank()) return null
            putLocked(key, address, force = false)
        }
    }

    fun create(key: K, address: String, force: Boolean = false): Result<Unit> = synchronized(lock) {
        if (shutdownRequested) return Result.failure(IllegalStateException("registry is shut down"))
        if (address.isBlank()) return Result.failure(IllegalArgumentException("endpoint is blank"))
        putLocked(key, address, force)
            ?.let { Result.success(Unit) }
            ?: Result.failure(IllegalStateException("client creation failed"))
    }

    fun warmUp(keys: Iterable<K>) {
        keys.forEach { get(it) }
    }

    fun recreate(keys: Iterable<K> = synchronized(lock) { slots.keys.toList() }) {
        keys.forEach { key ->
            val address = endpointFor(key)
            if (address.isBlank()) {
                synchronized(lock) { slots.remove(key)?.second }?.let(closeAction)
            } else {
                create(key, address, force = true)
            }
        }
    }

    fun shutdown() {
        val old = synchronized(lock) {
            shutdownRequested = true
            slots.values.map { it.second }.also { slots.clear() }
        }
        old.forEach(closeAction)
    }

    val isShutdown: Boolean
        get() = shutdownRequested

    private fun putLocked(key: K, address: String, force: Boolean): C? {
        val normalized = runCatching { normalize(address) }.getOrNull() ?: return null
        val current = slots[key]
        if (!force && current?.first == normalized) return current.second
        return runCatching { clientFactory(key, normalized) }.getOrNull()?.also { created ->
            slots[key] = normalized to created
            current?.second?.let(closeAction)
        }
    }
}

/**
 * Owns the lifecycle of all typed gRPC stubs.
 *
 * Navigator and Beacon are explicit because onboarding selects their endpoint. The remaining
 * services resolve their endpoint from [GlobalParam] only when first read, which also makes
 * workers and FCM callbacks safe after a killed process. This class deliberately has no RPC
 * methods: domain gateways are the only layer allowed to call a stub.
 */
class GrpcClientRegistry(
    context: Context,
    private val tlsTransport: TlsTransportFactory = TlsTransportFactory(context.applicationContext),
) {

    enum class ClientId {
        NAVIGATOR,
        BEACON,
        IDENTITY,
        USERS,
        FILES,
        MESSAGES,
        UPDATES,
        ONLINER,
        FAST_AUTH,
        CALLS,
    }

    private data class Entry(
        val address: String,
        val managedChannel: ManagedChannel,
        val channel: Channel,
        val stub: Any,
    )

    private val appContext = context.applicationContext
    private val lock = Any()
    private val entries = EnumMap<ClientId, Entry>(ClientId::class.java)

    @Volatile
    private var shutdownRequested = false

    val navigatorChannel: Channel?
        get() = channel(ClientId.NAVIGATOR)
    val beaconChannel: Channel?
        get() = channel(ClientId.BEACON)
    val identityChannel: Channel?
        get() = channel(ClientId.IDENTITY)
    val usersChannel: Channel?
        get() = channel(ClientId.USERS)
    val filesChannel: Channel?
        get() = channel(ClientId.FILES)
    val messagesChannel: Channel?
        get() = channel(ClientId.MESSAGES)
    val updatesChannel: Channel?
        get() = channel(ClientId.UPDATES)
    val onlinerChannel: Channel?
        get() = channel(ClientId.ONLINER)
    val fastAuthChannel: Channel?
        get() = channel(ClientId.FAST_AUTH)
    val callsChannel: Channel?
        get() = channel(ClientId.CALLS)

    val navigatorClient: NavigatorApiGrpcKt.NavigatorApiCoroutineStub?
        get() = lazyClient(ClientId.NAVIGATOR, ::buildNavigator)
    val beaconClient: BeaconApiGrpcKt.BeaconApiCoroutineStub?
        get() = lazyClient(ClientId.BEACON, ::buildBeacon)
    val identityClient: IdentityApiGrpcKt.IdentityApiCoroutineStub?
        get() = lazyClient(ClientId.IDENTITY, ::buildIdentity)
    val usersClient: UsersApiGrpcKt.UsersApiCoroutineStub?
        get() = lazyClient(ClientId.USERS, ::buildUsers)
    val filesClient: FilesApiGrpcKt.FilesApiCoroutineStub?
        get() = lazyClient(ClientId.FILES, ::buildFiles)
    val messagesClient: MessagesApiGrpcKt.MessagesApiCoroutineStub?
        get() = lazyClient(ClientId.MESSAGES, ::buildMessages)
    val updatesClient: UpdatesApiGrpcKt.UpdatesApiCoroutineStub?
        get() = lazyClient(ClientId.UPDATES, ::buildUpdates)
    val onlinerClient: OnlinerApiGrpcKt.OnlinerApiCoroutineStub?
        get() = lazyClient(ClientId.ONLINER, ::buildOnliner)
    val fastAuthClient: FastAuthApiGrpcKt.FastAuthApiCoroutineStub?
        get() = lazyClient(ClientId.FAST_AUTH, ::buildFastAuth)
    val callsClient: CallsApiGrpcKt.CallsApiCoroutineStub?
        get() = lazyClient(ClientId.CALLS, ::buildCalls)

    /** Normalizes an endpoint using the same TLS/cleartext policy as every channel. */
    fun normalizeEndpointAddress(address: String): String = tlsTransport.normalizeGrpcAddress(address)

    /** Creates an explicit Navigator client without reading a stale endpoint from storage. */
    fun createNavigatorClient(address: String): Result<Unit> =
        create(ClientId.NAVIGATOR, address, includeAuth = false, includeDeviceInfo = false)

    /** Creates an explicit Beacon client without reading a stale endpoint from storage. */
    fun createBeaconClient(address: String): Result<Unit> =
        create(ClientId.BEACON, address, includeAuth = false, includeDeviceInfo = false)

    fun createIdentityClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.IDENTITY,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createUsersClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.USERS,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createFilesClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.FILES,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createMessagesClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.MESSAGES,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createUpdatesClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.UPDATES,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createOnlinerClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.ONLINER,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createFastAuthClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.FAST_AUTH,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    fun createCallsClient(
        address: String,
        context: Context? = null,
        includeDeviceInfo: Boolean = false,
    ): Result<Unit> = create(
        ClientId.CALLS,
        address,
        context ?: appContext,
        includeAuth = context != null,
        includeDeviceInfo = includeDeviceInfo && context != null,
    )

    /** Warm up the authenticated clients; Navigator/Beacon remain explicit by design. */
    fun warmUp(globalParam: GlobalParam = GlobalParam(appContext), context: Context = appContext) {
        if (shutdownRequested) return
        configuredClientIds().forEach { id ->
            val address = endpointFor(id, globalParam)
            if (address.isNotBlank()) {
                create(id, address, context, includeAuth = true, includeDeviceInfo = true)
            }
        }
    }

    /** Idempotently creates all configured clients, preserving explicit Navigator/Beacon. */
    fun initAllClients(globalParam: GlobalParam = GlobalParam(appContext), context: Context = appContext) {
        warmUp(globalParam, context)
    }

    /**
     * Replaces all currently-created clients with fresh channels. New channels are installed
     * before old ones are closed, so a concurrent request never observes a null stub.
     */
    fun recreateAllClients(globalParam: GlobalParam = GlobalParam(appContext), context: Context = appContext) {
        val existing = synchronized(lock) { entries.keys.toList() }
        val clientsToRecreate = (existing + configuredClientIds()).distinct()
        clientsToRecreate.forEach { id ->
            val address = if (id == ClientId.NAVIGATOR || id == ClientId.BEACON) {
                synchronized(lock) { entries[id]?.address.orEmpty() }
            } else {
                endpointFor(id, globalParam)
            }
            if (address.isBlank()) {
                remove(id)
            } else {
                create(
                    id = id,
                    address = address,
                    context = context,
                    includeAuth = id != ClientId.NAVIGATOR && id != ClientId.BEACON,
                    includeDeviceInfo = id != ClientId.NAVIGATOR && id != ClientId.BEACON,
                    force = true,
                )
            }
        }
    }

    /** Terminal shutdown. Once called, lazy getters never recreate a channel. */
    fun shutdown() {
        val oldEntries = synchronized(lock) {
            shutdownRequested = true
            entries.values.toList().also { entries.clear() }
        }
        oldEntries.forEach { close(it.managedChannel) }
    }

    val isShutdown: Boolean
        get() = shutdownRequested

    private fun configuredClientIds(): List<ClientId> = listOf(
        ClientId.IDENTITY,
        ClientId.USERS,
        ClientId.FILES,
        ClientId.MESSAGES,
        ClientId.UPDATES,
        ClientId.ONLINER,
        ClientId.FAST_AUTH,
        ClientId.CALLS,
    )

    private fun endpointFor(id: ClientId, globalParam: GlobalParam): String = when (id) {
        ClientId.IDENTITY -> globalParam.socketIdentity
        ClientId.USERS -> globalParam.socketUsers
        ClientId.FILES -> globalParam.socketFiles
        ClientId.MESSAGES -> globalParam.socketMessages
        ClientId.UPDATES -> globalParam.socketUpdates
        ClientId.ONLINER -> globalParam.socketOnliner
        ClientId.FAST_AUTH -> globalParam.socketFastAuth
        ClientId.CALLS -> globalParam.socketCalls
        ClientId.NAVIGATOR, ClientId.BEACON -> ""
    }

    private fun channel(id: ClientId): Channel? = synchronized(lock) { entries[id]?.channel }

    private inline fun <reified T : Any> lazyClient(
        id: ClientId,
        noinline builder: (Channel) -> T,
    ): T? {
        synchronized(lock) {
            if (shutdownRequested) return null
            entries[id]?.let { return it.stub as? T }
            val address = endpointFor(id, GlobalParam(appContext))
            if (address.isBlank()) return null
            return createLocked(id, address, appContext, includeAuth = id != ClientId.NAVIGATOR && id != ClientId.BEACON, includeDeviceInfo = true, force = false, builder)
                ?.stub as? T
        }
    }

    private fun create(
        id: ClientId,
        address: String,
        context: Context = appContext,
        includeAuth: Boolean,
        includeDeviceInfo: Boolean,
        force: Boolean = false,
    ): Result<Unit> {
        if (address.isBlank()) return Result.failure(IllegalArgumentException("${id.name} endpoint is blank"))
        synchronized(lock) {
            if (shutdownRequested) return Result.failure(IllegalStateException("gRPC registry is shut down"))
            val builder: (Channel) -> Any = builderFor(id)
            val created = createLocked(id, address, context, includeAuth, includeDeviceInfo, force, builder)
                ?: return Result.failure(IllegalStateException("Unable to create ${id.name} client"))
            return Result.success(Unit)
        }
    }

    private fun createLocked(
        id: ClientId,
        address: String,
        context: Context,
        includeAuth: Boolean,
        includeDeviceInfo: Boolean,
        force: Boolean,
        builder: (Channel) -> Any,
    ): Entry? {
        val normalized = runCatching { tlsTransport.normalizeGrpcAddress(address) }
            .getOrElse { return null }
        val current = entries[id]
        if (!force && current?.address == normalized) return current

        return try {
            val managed = tlsTransport.createGrpcChannel(normalized)
            val interceptors = buildList {
                if (includeAuth) add(AuthInterceptor(context))
                if (includeDeviceInfo) add(DeviceInfoInterceptor(context))
            }
            val intercepted = if (interceptors.isEmpty()) managed else {
                ClientInterceptors.intercept(managed, *interceptors.toTypedArray())
            }
            val created = Entry(normalized, managed, intercepted, builder(intercepted))
            entries[id] = created
            current?.let { close(it.managedChannel) }
            created
        } catch (error: Exception) {
            Log.e("GrpcClientRegistry", "Unable to create $id client", error)
            null
        }
    }

    private fun remove(id: ClientId) {
        val old = synchronized(lock) { entries.remove(id) }
        old?.let { close(it.managedChannel) }
    }

    private fun builderFor(id: ClientId): (Channel) -> Any = when (id) {
        ClientId.NAVIGATOR -> ::buildNavigator
        ClientId.BEACON -> ::buildBeacon
        ClientId.IDENTITY -> ::buildIdentity
        ClientId.USERS -> ::buildUsers
        ClientId.FILES -> ::buildFiles
        ClientId.MESSAGES -> ::buildMessages
        ClientId.UPDATES -> ::buildUpdates
        ClientId.ONLINER -> ::buildOnliner
        ClientId.FAST_AUTH -> ::buildFastAuth
        ClientId.CALLS -> ::buildCalls
    }

    private fun buildNavigator(channel: Channel) = NavigatorApiGrpcKt.NavigatorApiCoroutineStub(channel)
    private fun buildBeacon(channel: Channel) = BeaconApiGrpcKt.BeaconApiCoroutineStub(channel)
    private fun buildIdentity(channel: Channel) = IdentityApiGrpcKt.IdentityApiCoroutineStub(channel)
    private fun buildUsers(channel: Channel) = UsersApiGrpcKt.UsersApiCoroutineStub(channel)
    private fun buildFiles(channel: Channel) = FilesApiGrpcKt.FilesApiCoroutineStub(channel)
    private fun buildMessages(channel: Channel) = MessagesApiGrpcKt.MessagesApiCoroutineStub(channel)
    private fun buildUpdates(channel: Channel) = UpdatesApiGrpcKt.UpdatesApiCoroutineStub(channel)
    private fun buildOnliner(channel: Channel) = OnlinerApiGrpcKt.OnlinerApiCoroutineStub(channel)
    private fun buildFastAuth(channel: Channel) = FastAuthApiGrpcKt.FastAuthApiCoroutineStub(channel)
    private fun buildCalls(channel: Channel) = CallsApiGrpcKt.CallsApiCoroutineStub(channel)

    private fun close(channel: ManagedChannel) {
        channel.shutdown()
        if (!channel.isTerminated) channel.shutdownNow()
    }
}
