package com.barkfluff.client.bft

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import barkfluff.beacon.BeaconApiGrpcKt
import barkfluff.beacon.BeaconApiOuterClass
import com.barkfluff.client.data.GlobalParam
import io.grpc.okhttp.OkHttpChannelBuilder
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import java.io.Closeable
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.util.Base64
import java.util.concurrent.TimeUnit
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import javax.net.SocketFactory

enum class BftProxyStatus { DISABLED, CONNECTING, CONNECTED, NO_CONNECTION, KEY_REJECTED, SERVER_MISMATCH }

internal fun monotonicTimeMillis(nowNanos: () -> Long = System::nanoTime): Long =
    TimeUnit.NANOSECONDS.toMillis(nowNanos())

data class BftProxyViewState(
    val configured: Boolean,
    val enabled: Boolean,
    val name: String?,
    val host: String?,
    val port: Int?,
    val status: BftProxyStatus,
    val generation: Long
)

/** Process-wide owner for the one BFT tunnel and its transport configuration generation. */
class BftProxyManager internal constructor(
    private val store: BftProxySettingsStore,
    private val tunnelConnector: (String) -> BftTunnel,
    private val nowMillis: () -> Long = { monotonicTimeMillis() },
    private val retryPolicy: BftReconnectPolicy = BftReconnectPolicy(),
    private val connectionValidator: ((BftTunnel) -> Unit)? = null
) {
    private data class StoredConfiguration(
        val connectionString: String,
        val name: String?,
        val host: String,
        val port: Int,
        val enabled: Boolean
    )

    private val stateLock = Any()
    private val connectionLock = Any()
    private val listeners = CopyOnWriteArrayList<(Long) -> Unit>()
    private val scheduler = Executors.newSingleThreadScheduledExecutor { runnable ->
        Thread(runnable, "bft-proxy-reconnect").apply { isDaemon = true }
    }
    private var retryTask: ScheduledFuture<*>? = null
    @Volatile private var tunnel: BftTunnel? = null
    @Volatile private var tunnelGeneration = -1L
    @Volatile private var persistentNetworkDemand = false
    @Volatile private var retryAtMillis = 0L
    @Volatile private var activationPending = false
    @Volatile private var generation = 0L
    private var stored = try {
        readStoredConfiguration()
    } catch (_: Exception) {
        runCatching { Log.e(TAG, "Encrypted BFT proxy settings could not be read; proxy disabled") }
        null
    }
    private val mutableState = MutableStateFlow(viewState(stored, initialStatus(stored)))
    val state: StateFlow<BftProxyViewState> = mutableState

    val isEnabled: Boolean
        get() = mutableState.value.enabled || activationPending

    val transportGeneration: Long
        get() = generation

    val settings: BftProxyViewState
        get() = mutableState.value

    fun addTransportGenerationListener(listener: (Long) -> Unit): Closeable {
        listeners += listener
        return Closeable { listeners -= listener }
    }

    fun saveConnectionString(connectionString: String, enable: Boolean = true) {
        val parsed = BftConnectionString.parse(connectionString)
        val canonical = try {
            parsed.format()
        } finally {
            parsed.staticPublicKey.fill(0)
            parsed.obfsKey.fill(0)
        }
        val settings = BftConnectionString.parse(canonical)
        val next = StoredConfiguration(canonical, settings.name, settings.host, settings.port, enable)
        settings.staticPublicKey.fill(0)
        settings.obfsKey.fill(0)
        replaceStoredConfiguration(next)
    }

    /** Saves and validates a newly imported link without allowing traffic onto a direct route mid-check. */
    fun saveConnectionStringAndEnable(connectionString: String) {
        val parsed = BftConnectionString.parse(connectionString)
        val canonical = try {
            parsed.format()
        } finally {
            parsed.staticPublicKey.fill(0)
            parsed.obfsKey.fill(0)
        }
        val settings = BftConnectionString.parse(canonical)
        val next = StoredConfiguration(canonical, settings.name, settings.host, settings.port, enabled = false)
        settings.staticPublicKey.fill(0)
        settings.obfsKey.fill(0)
        activateConfiguration(next)
    }

    fun setEnabled(enabled: Boolean) {
        val current = stored ?: throw IllegalStateException("No BFT proxy is configured")
        if (enabled) {
            BftConnectionString.parse(current.connectionString).also {
                it.staticPublicKey.fill(0)
                it.obfsKey.fill(0)
            }
        }
        if (current.enabled == enabled) return
        if (enabled && connectionValidator != null) {
            activateConfiguration(current)
            return
        }
        replaceStoredConfiguration(current.copy(enabled = enabled))
    }

    fun retryConnection() {
        if (!isEnabled) return
        synchronized(stateLock) {
            retryAtMillis = 0L
            retryPolicy.networkChanged()
            mutableState.value = viewState(stored, BftProxyStatus.NO_CONNECTION)
        }
        scheduler.execute { runCatching { ensureConnectedTunnel() } }
    }

    fun clear() {
        if (stored == null) return
        store.clear()
        stored = null
        invalidateTransport(BftProxyStatus.DISABLED)
    }

    /** Called with the same foreground/realtime demand boundary used by RealtimeService. */
    fun setPersistentNetworkDemand(needed: Boolean) {
        persistentNetworkDemand = needed
        if (!needed) {
            cancelRetry()
            closeCurrentTunnel()
            updateStatus(if (isEnabled) BftProxyStatus.NO_CONNECTION else BftProxyStatus.DISABLED)
        } else if (isEnabled && tunnel?.state != BftTunnel.State.READY) {
            scheduleReconnect(0)
        }
    }

    /** Closes stale sockets immediately and advances the configuration seen by every factory. */
    fun onNetworkChanged() {
        synchronized(stateLock) {
            generation++
            retryAtMillis = 0L
            retryPolicy.networkChanged()
            mutableState.value = viewState(stored, if (stored?.enabled == true) BftProxyStatus.NO_CONNECTION else BftProxyStatus.DISABLED)
        }
        closeCurrentTunnel()
        notifyTransportGeneration()
        if (persistentNetworkDemand && isEnabled) scheduleReconnect(0)
    }

    fun socketFactory(serviceId: Int): SocketFactory = BftSocketFactory {
        openSocketStreams(serviceId)
    }

    fun openSocketStreams(serviceId: Int): BftSocketStreams {
        val activeTunnel = ensureConnectedTunnel()
        if (serviceId !in activeTunnel.hello.serviceIds) {
            throw BftProxyException(BftProxyErrorCode.SERVICE_UNAVAILABLE)
        }
        val openedGeneration = tunnelGeneration
        val pin = "sha256/${Base64.getEncoder().encodeToString(activeTunnel.hello.tlsSpkiSha256)}"
        val stream = activeTunnel.openStream(serviceId)
        activeStreams.incrementAndGet()
        val released = java.util.concurrent.atomic.AtomicBoolean()
        return stream.asSocketStreams(pin) {
            if (released.compareAndSet(false, true) && tunnelGeneration == openedGeneration) {
                val remaining = activeStreams.decrementAndGet()
                if (!persistentNetworkDemand && remaining <= 0) {
                    activeStreams.set(0)
                    closeIfIdle()
                }
            }
        }
    }

    private val activeStreams = java.util.concurrent.atomic.AtomicInteger()

    private fun ensureConnectedTunnel(): BftTunnel {
        synchronized(connectionLock) {
            val config = synchronized(stateLock) {
                val current = stored
                if (current?.enabled != true) throw IOException("BFT proxy is disabled")
                if (current.connectionString.isBlank()) throw IOException("BFT proxy configuration is invalid")
                if (retryAtMillis > nowMillis()) {
                    if (settings.status == BftProxyStatus.SERVER_MISMATCH) {
                        throw BftProxyException(BftProxyErrorCode.SERVER_MISMATCH)
                    }
                    throw unavailableError()
                }
                current to generation
            }
            tunnel?.takeIf { it.state == BftTunnel.State.READY && tunnelGeneration == config.second }?.let { return it }

            updateStatus(BftProxyStatus.CONNECTING)
            try {
                val connected = tunnelConnector(config.first.connectionString)
                try {
                    connectionValidator?.invoke(connected)
                } catch (error: Throwable) {
                    connected.close()
                    throw error
                }
                if (generation != config.second || !isEnabled) {
                    connected.close()
                    throw IOException("BFT proxy configuration changed")
                }
                tunnel = connected
                tunnelGeneration = config.second
                retryAtMillis = 0L
                retryPolicy.connected()
                updateStatus(BftProxyStatus.CONNECTED)
                connected.completion.thenAccept { reason -> onTunnelClosed(connected, config.second, reason) }
                return connected
            } catch (error: Throwable) {
                if (error is IOException && error.message == "BFT proxy configuration changed") throw error
                if (error is BftProxyException && error.code == BftProxyErrorCode.SERVER_MISMATCH) {
                    retryAtMillis = Long.MAX_VALUE
                    updateStatus(BftProxyStatus.SERVER_MISMATCH)
                    throw error
                }
                val authenticationFailed = (error as? BftTunnelConnectException)?.closeReason?.kind == BftTunnelCloseReason.Kind.AUTH_FAILED
                val invalidConfiguration = error is IllegalArgumentException
                val silentClose = error is BftTunnelConnectException && !authenticationFailed
                val decision = retryPolicy.recordFailure(authenticationFailed || invalidConfiguration, silentClose)
                retryAtMillis = nowMillis() + decision.delayMillis
                updateStatus(if (decision.keyRejected) BftProxyStatus.KEY_REJECTED else BftProxyStatus.NO_CONNECTION)
                if (persistentNetworkDemand) scheduleReconnect(decision.delayMillis)
                if (error is BftProxyException && error.code == BftProxyErrorCode.SERVER_IDENTITY_UNAVAILABLE) throw error
                throw unavailableError(error)
            }
        }
    }

    private fun onTunnelClosed(closedTunnel: BftTunnel, closedGeneration: Long, reason: BftTunnelCloseReason) {
        if (tunnel !== closedTunnel || generation != closedGeneration) return
        tunnel = null
        val authenticationFailed = reason.kind == BftTunnelCloseReason.Kind.AUTH_FAILED
        val orderlyShutdown = reason.goAway?.code == BftGoAwayCode.SHUTDOWN
        val decision = if (orderlyShutdown) {
            retryPolicy.connected()
            BftReconnectPolicy.FailureDecision(0L, keyRejected = false)
        } else {
            retryPolicy.recordFailure(
                authenticationFailed,
                closedWithoutResponse = reason.kind == BftTunnelCloseReason.Kind.NETWORK_ERROR
            )
        }
        retryAtMillis = nowMillis() + decision.delayMillis
        synchronized(stateLock) {
            generation++
            mutableState.value = viewState(
                stored,
                when {
                    decision.keyRejected -> BftProxyStatus.KEY_REJECTED
                    isEnabled -> BftProxyStatus.NO_CONNECTION
                    else -> BftProxyStatus.DISABLED
                }
            )
        }
        notifyTransportGeneration()
        if (persistentNetworkDemand && isEnabled) scheduleReconnect(decision.delayMillis)
    }

    private fun scheduleReconnect(delayMillis: Long) {
        if (!persistentNetworkDemand || !isEnabled) return
        synchronized(stateLock) {
            retryTask?.cancel(false)
            retryTask = scheduler.schedule({
                if (!persistentNetworkDemand || !isEnabled) return@schedule
                runCatching { ensureConnectedTunnel() }
            }, delayMillis.coerceAtLeast(0), TimeUnit.MILLISECONDS)
        }
    }

    private fun cancelRetry() = synchronized(stateLock) {
        retryTask?.cancel(false)
        retryTask = null
    }

    private fun closeIfIdle() {
        if (persistentNetworkDemand) return
        val old = tunnel ?: return
        tunnel = null
        old.close()
        updateStatus(if (isEnabled) BftProxyStatus.NO_CONNECTION else BftProxyStatus.DISABLED)
    }

    private fun replaceStoredConfiguration(next: StoredConfiguration) {
        store.write(next.connectionString, next.name.orEmpty(), next.enabled)
        stored = next
        invalidateTransport(if (next.enabled) BftProxyStatus.NO_CONNECTION else BftProxyStatus.DISABLED)
    }

    private fun activateConfiguration(configuration: StoredConfiguration) {
        synchronized(connectionLock) {
            activationPending = true
            synchronized(stateLock) {
                generation++
                retryAtMillis = 0L
                mutableState.value = viewState(configuration, BftProxyStatus.CONNECTING)
            }
            cancelRetry()
            closeCurrentTunnel()
            notifyTransportGeneration()

            var candidate: BftTunnel? = null
            try {
                store.write(configuration.connectionString, configuration.name.orEmpty(), enabled = false)
                stored = configuration.copy(enabled = false)
                candidate = tunnelConnector(configuration.connectionString)
                connectionValidator?.invoke(candidate)
                store.write(configuration.connectionString, configuration.name.orEmpty(), enabled = true)
                stored = configuration.copy(enabled = true)
                tunnel = candidate
                tunnelGeneration = generation
                retryAtMillis = 0L
                retryPolicy.connected()
                activationPending = false
                updateStatus(BftProxyStatus.CONNECTED)
                val connected = requireNotNull(candidate)
                val connectedGeneration = generation
                connected.completion.thenAccept { reason -> onTunnelClosed(connected, connectedGeneration, reason) }
            } catch (error: Throwable) {
                candidate?.close()
                activationPending = false
                stored = configuration.copy(enabled = false)
                runCatching { store.write(configuration.connectionString, configuration.name.orEmpty(), enabled = false) }
                synchronized(stateLock) {
                    generation++
                    retryAtMillis = if (error is BftProxyException && error.code == BftProxyErrorCode.SERVER_MISMATCH) {
                        Long.MAX_VALUE
                    } else {
                        0L
                    }
                    mutableState.value = viewState(
                        stored,
                        if (error is BftProxyException && error.code == BftProxyErrorCode.SERVER_MISMATCH) {
                            BftProxyStatus.SERVER_MISMATCH
                        } else {
                            BftProxyStatus.NO_CONNECTION
                        }
                    )
                }
                notifyTransportGeneration()
                throw when (error) {
                    is BftProxyException -> error
                    else -> unavailableError(error)
                }
            }
        }
    }

    private fun invalidateTransport(status: BftProxyStatus) {
        synchronized(stateLock) {
            generation++
            retryAtMillis = 0L
            retryPolicy.networkChanged()
            mutableState.value = viewState(stored, status)
        }
        cancelRetry()
        closeCurrentTunnel()
        notifyTransportGeneration()
        if (persistentNetworkDemand && isEnabled) scheduleReconnect(0)
    }

    private fun closeCurrentTunnel() {
        val old = tunnel
        tunnel = null
        tunnelGeneration = -1L
        old?.close()
        activeStreams.set(0)
    }

    private fun notifyTransportGeneration() {
        val current = generation
        listeners.forEach { listener -> runCatching { listener(current) } }
    }

    private fun updateStatus(status: BftProxyStatus) = synchronized(stateLock) {
        mutableState.value = viewState(stored, status)
    }

    private fun readStoredConfiguration(): StoredConfiguration? {
        val value = store.read() ?: return null
        return try {
            val parsed = BftConnectionString.parse(value.connectionString)
            try {
                StoredConfiguration(
                    parsed.format(),
                    value.name.ifBlank { parsed.name.orEmpty() }.ifBlank { null },
                    parsed.host,
                    parsed.port,
                    value.enabled
                )
            } finally {
                parsed.staticPublicKey.fill(0)
                parsed.obfsKey.fill(0)
            }
        } catch (_: IllegalArgumentException) {
            StoredConfiguration(value.connectionString, value.name.ifBlank { null }, "", 0, value.enabled)
        }
    }

    private fun initialStatus(configuration: StoredConfiguration?): BftProxyStatus = when {
        configuration?.enabled != true -> BftProxyStatus.DISABLED
        configuration.host.isBlank() -> BftProxyStatus.KEY_REJECTED
        else -> BftProxyStatus.NO_CONNECTION
    }

    private fun viewState(configuration: StoredConfiguration?, status: BftProxyStatus) = BftProxyViewState(
        configured = configuration != null,
        enabled = configuration?.enabled == true,
        name = configuration?.name,
        host = configuration?.host?.takeIf(String::isNotBlank),
        port = configuration?.port?.takeIf { it > 0 },
        status = if (configuration?.enabled == true || status == BftProxyStatus.SERVER_MISMATCH) status else BftProxyStatus.DISABLED,
        generation = generation
    )

    private fun unavailableError(cause: Throwable? = null): BftProxyException =
        BftProxyException(
            when (settings.status) {
                BftProxyStatus.KEY_REJECTED -> BftProxyErrorCode.KEY_REJECTED
                BftProxyStatus.SERVER_MISMATCH -> BftProxyErrorCode.SERVER_MISMATCH
                else -> BftProxyErrorCode.NO_CONNECTION
            },
            cause
        )

    internal interface BftProxySettingsStore {
        fun read(): BftProxyStoredSettings?
        fun write(connectionString: String, name: String, enabled: Boolean)
        fun clear()
    }

    internal data class BftProxyStoredSettings(val connectionString: String, val name: String, val enabled: Boolean)

    internal class EncryptedSettingsStore(context: Context) : BftProxySettingsStore {
        private val preferences: SharedPreferences = EncryptedSharedPreferences.create(
            PREFERENCES_FILE,
            MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC),
            context.applicationContext,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )

        override fun read(): BftProxyStoredSettings? {
            val connectionString = preferences.getString(KEY_CONNECTION_STRING, null) ?: return null
            return BftProxyStoredSettings(
                connectionString,
                preferences.getString(KEY_NAME, "").orEmpty(),
                preferences.getBoolean(KEY_ENABLED, false)
            )
        }

        override fun write(connectionString: String, name: String, enabled: Boolean) {
            preferences.edit()
                .putString(KEY_CONNECTION_STRING, connectionString)
                .putString(KEY_NAME, name)
                .putBoolean(KEY_ENABLED, enabled)
                .apply()
        }

        override fun clear() {
            preferences.edit().clear().apply()
        }

        private companion object {
            const val PREFERENCES_FILE = "bft_proxy_secure_prefs"
            const val KEY_CONNECTION_STRING = "connection_string"
            const val KEY_NAME = "name"
            const val KEY_ENABLED = "enabled"
        }
    }

    private class UnavailableSettingsStore : BftProxySettingsStore {
        override fun read(): BftProxyStoredSettings? = null
        override fun write(connectionString: String, name: String, enabled: Boolean) {
            throw IllegalStateException("Encrypted proxy settings are unavailable")
        }
        override fun clear() = Unit
    }

    companion object {
        @Volatile private var processInstance: BftProxyManager? = null

        fun get(context: Context): BftProxyManager = processInstance ?: synchronized(this) {
            processInstance ?: BftProxyManager(
                runCatching { EncryptedSettingsStore(context) }.getOrElse {
                    runCatching { Log.e(TAG, "Encrypted BFT proxy settings could not be opened; proxy disabled") }
                    UnavailableSettingsStore()
                },
                ::connectTunnel,
                connectionValidator = { tunnel -> validateTunnelForActiveSession(context.applicationContext, tunnel) }
            ).also {
                processInstance = it
            }
        }

        private const val TAG = "BftProxyManager"
    }

}

private fun validateTunnelForActiveSession(context: Context, tunnel: BftTunnel) {
    val globalParam = GlobalParam(context)
    if (!BftProxySessionPolicy.hasSession(globalParam.accessToken, globalParam.refreshToken, globalParam.userId)) return
    val endpoints = runCatching { BftBeaconTunnelProbe.getEndpoints(tunnel) }
        .getOrElse { throw BftProxyException(BftProxyErrorCode.SERVER_IDENTITY_UNAVAILABLE, it) }
    if (!BftNodeIdentity.matches(globalParam.bftServiceEndpoints, endpoints)) {
        throw BftProxyException(BftProxyErrorCode.SERVER_MISMATCH)
    }
}

internal object BftBeaconTunnelProbe {
    fun getEndpoints(
        tunnel: BftTunnel,
        tlsHostOverride: String = BEACON_AUTHORITY,
        tlsSpkiPinOverride: String? = null
    ): Map<Int, String> = runBlocking(Dispatchers.IO) {
        val tlsFactory = BftTlsSocketFactory(
            tlsSpkiPinOverride ?: "sha256/${Base64.getEncoder().encodeToString(tunnel.hello.tlsSpkiSha256)}",
            tlsHostOverride = tlsHostOverride
        )
        val channel = OkHttpChannelBuilder.forAddress("127.0.0.1", BEACON_PORT)
            .overrideAuthority(tlsHostOverride)
            .socketFactory(BftSocketFactory(tunnel, BEACON_SERVICE_ID))
            .sslSocketFactory(tlsFactory)
            .disableRetry()
            .build()
        try {
            val response = withTimeout(PROBE_TIMEOUT_MILLIS) {
                BeaconApiGrpcKt.BeaconApiCoroutineStub(channel)
                    .withDeadlineAfter(PROBE_TIMEOUT_MILLIS, TimeUnit.MILLISECONDS)
                    .getServerInfo(BeaconApiOuterClass.GetServerInfoRequest.newBuilder().build())
            }
            buildMap {
                put(2, endpoint(response.identity.endpoint.host, response.identity.endpoint.port))
                put(3, endpoint(response.users.endpoint.host, response.users.endpoint.port))
                put(4, endpoint(response.messages.endpoint.host, response.messages.endpoint.port))
                put(5, endpoint(response.files.endpoint.host, response.files.endpoint.port))
                put(7, endpoint(response.updates.endpoint.host, response.updates.endpoint.port))
                put(8, endpoint(response.onliner.endpoint.host, response.onliner.endpoint.port))
                put(9, endpoint(response.fastAuth.endpoint.host, response.fastAuth.endpoint.port))
                if (response.hasCalls()) put(10, endpoint(response.calls.endpoint.host, response.calls.endpoint.port))
                if (response.filesMediaEndpoint.isNotBlank()) put(6, response.filesMediaEndpoint)
            }
        } finally {
            channel.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
        }
    }

    private fun endpoint(host: String, port: Int): String = "$host:$port"

    private const val BEACON_AUTHORITY = "bft.internal"
    private const val BEACON_SERVICE_ID = 1
    private const val BEACON_PORT = 443
    private const val PROBE_TIMEOUT_MILLIS = 5_000L
}

private fun connectTunnel(connectionString: String): BftTunnel {
    val settings = BftConnectionString.parse(connectionString)
    val socket = Socket()
    return try {
        socket.connect(InetSocketAddress(settings.host, settings.port), 10_000)
        BftTunnel.connect(socket, connectionString)
    } catch (error: Throwable) {
        runCatching { socket.close() }
        throw error
    } finally {
        settings.staticPublicKey.fill(0)
        settings.obfsKey.fill(0)
    }
}
