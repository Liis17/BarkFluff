package com.barkfluff.client.grpc

import android.content.Context
import android.graphics.Bitmap
import android.graphics.drawable.BitmapDrawable
import android.util.Log
import barkfluff.identity.IdentityApiGrpcKt
import barkfluff.identity.IdentityApiOuterClass
import barkfluff.onliner.OnlinerApiGrpcKt
import barkfluff.onliner.OnlinerApiOuterClass
import barkfluff.shared.Shared
import barkfluff.updates.UpdatesApiGrpcKt
import barkfluff.updates.UpdatesApiOuterClass
import coil.ImageLoader
import coil.request.ImageRequest
import coil.request.SuccessResult
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import io.grpc.ClientInterceptors
import io.grpc.ManagedChannel
import io.grpc.okhttp.OkHttpChannelBuilder
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlin.coroutines.coroutineContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import okhttp3.OkHttpClient
import java.security.cert.X509Certificate
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager
import kotlin.math.min
import kotlin.math.pow

/**
 * Сервис реального времени — подписывается на обновления сообщений, прочтений и онлайн-статусов.
 * Аналог RealtimeUpdateService + OnlineStatusService из WPF клиента.
 */
class RealtimeService(private val context: Context) {

    companion object {
        private const val TAG = "RealtimeService"
        private const val TOKEN_BUFFER_MINUTES = 5
        private const val ONLINE_PING_INTERVAL_MS = 3000L
        private const val MAX_BACKOFF_MS = 30_000L
        private const val BASE_BACKOFF_MS = 2000L
        private const val TOKEN_REFRESH_AFTER_ATTEMPTS = 3
        private const val DEDUP_MAX_SIZE = 1000
    }

    enum class ConnectionState { DISCONNECTED, CONNECTING, CONNECTED }

    // Public event flows
    private val _newMessages = MutableSharedFlow<UpdatesApiOuterClass.NewMessageEvent>(extraBufferCapacity = 64)
    val newMessages: SharedFlow<UpdatesApiOuterClass.NewMessageEvent> = _newMessages

    private val _messagesRead = MutableSharedFlow<UpdatesApiOuterClass.MessageReadEvent>(extraBufferCapacity = 64)
    val messagesRead: SharedFlow<UpdatesApiOuterClass.MessageReadEvent> = _messagesRead

    private val _onlineStatuses = MutableSharedFlow<OnlinerApiOuterClass.UserOnlineStatus>(extraBufferCapacity = 64)
    val onlineStatuses: SharedFlow<OnlinerApiOuterClass.UserOnlineStatus> = _onlineStatuses

    private val _connectionState = MutableStateFlow(ConnectionState.DISCONNECTED)
    val connectionState: StateFlow<ConnectionState> = _connectionState

    private data class CachedUserInfo(val displayName: String, val avatarFileId: String)

    // Internal state
    private val globalParam = GlobalParam(context)
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val tokenRefreshMutex = Mutex()
    private val seenMessageIds = LinkedHashSet<Long>()
    private val userInfoCache = ConcurrentHashMap<Long, CachedUserInfo>()

    // gRPC manager for notification lookups (users + files)
    private var notificationGrpcManager: GrpcManager? = null

    // Coil ImageLoader for headless avatar loading
    private val notificationImageLoader by lazy {
        val trustManager = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
            override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
        }
        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf<TrustManager>(trustManager), null)
        }
        val okHttp = OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier { _, _ -> true }
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(15, TimeUnit.SECONDS)
            .build()
        ImageLoader.Builder(context)
            .okHttpClient(okHttp)
            .build()
    }

    // gRPC channels and stubs (managed independently)
    private var updatesChannel: ManagedChannel? = null
    private var updatesClient: UpdatesApiGrpcKt.UpdatesApiCoroutineStub? = null
    private var onlinerChannel: ManagedChannel? = null
    private var onlinerClient: OnlinerApiGrpcKt.OnlinerApiCoroutineStub? = null

    // Online subscription state
    @Volatile
    private var subscribedUserIds: List<Long> = emptyList()

    @Volatile
    private var started = false

    /**
     * Запускает все стримы реального времени.
     * Безопасно вызывать повторно — при повторном вызове ничего не происходит.
     */
    fun start() {
        if (started) return
        started = true
        Log.i(TAG, "Starting realtime streams")

        createChannels()

        serviceScope.launch { streamWithReconnect("NewMessages") { collectNewMessages() } }
        serviceScope.launch { streamWithReconnect("MessagesRead") { collectMessagesRead() } }
        serviceScope.launch { streamWithReconnect("OnlineStatus") { collectOnlineStatus() } }
        serviceScope.launch { onlinePingLoop() }
        serviceScope.launch { notificationLoop() }
    }

    /**
     * Останавливает все стримы и закрывает каналы.
     */
    fun shutdown() {
        Log.i(TAG, "Shutting down realtime streams")
        started = false
        serviceScope.cancel()
        shutdownChannels()
        notificationGrpcManager?.shutdown()
        notificationGrpcManager = null
    }

    /**
     * Обновляет список пользователей для отслеживания онлайн-статуса.
     */
    fun changeOnlineSubscription(userIds: List<Long>) {
        subscribedUserIds = userIds
        serviceScope.launch {
            try {
                val client = onlinerClient ?: return@launch
                val request = OnlinerApiOuterClass.ChangeUsersInSubscriptionRequest.newBuilder()
                    .addAllUserIds(userIds)
                    .build()
                client.changeUsersInSubscription(request)
                Log.d(TAG, "Online subscription updated: ${userIds.size} users")
            } catch (e: Exception) {
                Log.w(TAG, "Failed to change online subscription", e)
            }
        }
    }

    /**
     * Отмечает сообщение как прочитанное (вызывается из BroadcastReceiver).
     */
    fun markAsRead(messageId: Long) {
        serviceScope.launch {
            try {
                val mgr = notificationGrpcManager ?: return@launch
                mgr.markAsRead(listOf(messageId))
                Log.d(TAG, "Marked message $messageId as read")
            } catch (e: Exception) {
                Log.w(TAG, "Failed to mark message as read: ${e.message}")
            }
        }
    }

    // --- Stream collectors ---

    private suspend fun collectNewMessages() {
        val client = updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeNewMessagesRequest.getDefaultInstance()
        _connectionState.value = ConnectionState.CONNECTED
        client.subscribeNewMessages(request).collect { event ->
            val msgId = event.message.id
            // Dedup
            synchronized(seenMessageIds) {
                if (!seenMessageIds.add(msgId)) {
                    Log.d(TAG, "Duplicate message $msgId, skipping")
                    return@collect
                }
                if (seenMessageIds.size > DEDUP_MAX_SIZE) {
                    val iter = seenMessageIds.iterator()
                    iter.next()
                    iter.remove()
                }
            }
            Log.d(TAG, "New message: id=$msgId, chatId=${event.chatId}")
            _newMessages.emit(event)
        }
    }

    private suspend fun collectMessagesRead() {
        val client = updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesReadRequest.getDefaultInstance()
        client.subscribeMessagesRead(request).collect { event ->
            Log.d(TAG, "Message read: chatId=${event.chatId}, msgId=${event.messageId}")
            _messagesRead.emit(event)
        }
    }

    private suspend fun collectOnlineStatus() {
        val client = onlinerClient ?: throw IllegalStateException("Onliner client not created")
        val request = OnlinerApiOuterClass.SubscribeToOnlineStatusRequest.newBuilder()
            .addAllUserIds(subscribedUserIds)
            .build()
        client.subscribeToOnlineStatus(request).collect { status ->
            Log.d(TAG, "Online status: userId=${status.userId}, status=${status.status}")
            _onlineStatuses.emit(status)
        }
    }

    private suspend fun onlinePingLoop() {
        while (coroutineContext.isActive) {
            try {
                val client = onlinerClient
                if (client != null) {
                    val request = OnlinerApiOuterClass.SetOnlineStatusRequest.getDefaultInstance()
                    client.setOnlineStatus(request)
                }
            } catch (e: Exception) {
                Log.w(TAG, "Online ping failed: ${e.message}")
            }
            delay(ONLINE_PING_INTERVAL_MS)
        }
    }

    private suspend fun notificationLoop() {
        try {
            _newMessages.collect { event ->
                try {
                    val msg = event.message ?: return@collect
                    val senderId = msg.senderId
                    if (senderId == globalParam.userId) return@collect

                    val chatId = event.chatId
                    val messageId = msg.id
                    val messageText = msg.content?.text ?: ""

                    // Resolve sender info (cache first)
                    val userInfo = userInfoCache[senderId] ?: run {
                        val mgr = notificationGrpcManager ?: return@collect
                        val result = mgr.getUserData(senderId)
                        if (result.isFailure) {
                            Log.w(TAG, "Failed to get user data for notification: senderId=$senderId")
                            return@collect
                        }
                        val user = result.getOrNull()!!
                        val displayName = "${user.firstName} ${user.lastName}".trim().ifEmpty { user.username }
                        val info = CachedUserInfo(displayName, user.profilePicturePreviewFileId)
                        userInfoCache[senderId] = info
                        info
                    }

                    // Load avatar bitmap (headless, no View needed)
                    var avatarBitmap: Bitmap? = null
                    if (userInfo.avatarFileId.isNotBlank()) {
                        avatarBitmap = loadBitmapByFileId(userInfo.avatarFileId)
                        Log.d(TAG, "Avatar loaded for senderId=$senderId: bitmap=${avatarBitmap != null}, " +
                                "size=${avatarBitmap?.let { "${it.width}x${it.height}" } ?: "null"}, " +
                                "config=${avatarBitmap?.config}")
                    } else {
                        Log.d(TAG, "No avatar fileId for senderId=$senderId")
                    }

                    // Load image attachment if present
                    var imageBitmap: Bitmap? = null
                    try {
                        val attachments = msg.content?.attachmentsList
                        val imageAttachment = attachments?.firstOrNull {
                            it.type == Shared.MessageAttachmentType.IMAGE
                        }
                        if (imageAttachment != null) {
                            val previewFileId = imageAttachment.previewFileId.ifBlank { imageAttachment.fileId }
                            if (previewFileId.isNotBlank()) {
                                imageBitmap = loadBitmapByFileId(previewFileId)
                            }
                        }
                    } catch (e: Exception) {
                        Log.w(TAG, "Failed to load image attachment for notification", e)
                    }

                    NotificationHelper.showMessageNotification(
                        context,
                        userInfo.displayName,
                        messageText,
                        avatarBitmap,
                        chatId,
                        messageId,
                        imageBitmap
                    )
                    Log.d(TAG, "Notification dispatched: chatId=${event.chatId}, msgId=$messageId")
                } catch (e: Exception) {
                    Log.e(TAG, "Notification dispatch error", e)
                }
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.e(TAG, "Notification loop failed", e)
        }
    }

    private suspend fun loadBitmapByFileId(fileId: String): Bitmap? {
        return try {
            val mgr = notificationGrpcManager ?: return null
            val urlResult = mgr.getFileDownloadUrl(fileId)
            val url = urlResult.getOrNull()
            if (url.isNullOrBlank()) return null

            val request = ImageRequest.Builder(context)
                .data(url)
                .allowHardware(false)
                .build()
            val imageResult = notificationImageLoader.execute(request)
            if (imageResult is SuccessResult) {
                (imageResult.drawable as? BitmapDrawable)?.bitmap
            } else null
        } catch (e: Exception) {
            Log.w(TAG, "Failed to load bitmap for fileId=$fileId", e)
            null
        }
    }

    // --- Reconnection ---

    private suspend fun streamWithReconnect(name: String, block: suspend () -> Unit) {
        var attempts = 0
        while (coroutineContext.isActive) {
            try {
                _connectionState.value = ConnectionState.CONNECTING
                ensureTokenValid()
                Log.d(TAG, "[$name] Connecting...")
                block()
                // Stream ended normally (server closed) — reset and reconnect
                attempts = 0
                Log.d(TAG, "[$name] Stream ended normally, reconnecting")
            } catch (e: CancellationException) {
                throw e // shutdown requested
            } catch (e: Exception) {
                attempts++
                Log.w(TAG, "[$name] Stream error (attempt $attempts): ${e.message}")

                if (attempts >= TOKEN_REFRESH_AFTER_ATTEMPTS) {
                    Log.d(TAG, "[$name] Force-refreshing token after $attempts attempts")
                    forceRefreshToken()
                }

                val backoff = min(
                    BASE_BACKOFF_MS * 2.0.pow((attempts - 1).coerceAtMost(10).toDouble()),
                    MAX_BACKOFF_MS.toDouble()
                ).toLong()

                _connectionState.value = ConnectionState.DISCONNECTED
                Log.d(TAG, "[$name] Waiting ${backoff}ms before reconnect")
                delay(backoff)

                recreateChannels()
            }
        }
    }

    // --- Token management ---

    private suspend fun ensureTokenValid() {
        val expiration = globalParam.accessTokenExpiration
        val now = System.currentTimeMillis()
        val bufferMs = TOKEN_BUFFER_MINUTES * 60 * 1000L

        if (expiration > 0 && now + bufferMs < expiration) {
            return // Token still valid
        }

        Log.d(TAG, "Token expiring soon, refreshing")
        forceRefreshToken()
    }

    private suspend fun forceRefreshToken() {
        tokenRefreshMutex.withLock {
            // Double-check after acquiring lock — another stream may have already refreshed
            val expiration = globalParam.accessTokenExpiration
            val now = System.currentTimeMillis()
            val bufferMs = TOKEN_BUFFER_MINUTES * 60 * 1000L
            if (expiration > 0 && now + bufferMs < expiration) {
                Log.d(TAG, "Token already refreshed by another stream")
                return
            }

            val refreshToken = globalParam.refreshToken
            if (refreshToken.isNullOrBlank()) {
                Log.e(TAG, "No refresh token available")
                return
            }

            val identityAddress = globalParam.socketIdentity
            if (identityAddress.isBlank()) {
                Log.e(TAG, "No identity address available")
                return
            }

            try {
                // Create temporary GrpcManager for identity call
                val tempManager = GrpcManager()
                tempManager.createIdentityClient(identityAddress, context, includeDeviceInfo = true)
                val result = tempManager.refreshAccessToken(refreshToken, globalParam.refreshTokenExpiration)
                tempManager.shutdown()

                if (result.isSuccess) {
                    val tokenResult = result.getOrNull()!!
                    globalParam.accessToken = tokenResult.accessToken
                    globalParam.accessTokenExpiration = tokenResult.accessTokenExpiration
                    globalParam.refreshToken = tokenResult.refreshToken
                    globalParam.refreshTokenExpiration = tokenResult.refreshTokenExpiration
                    Log.i(TAG, "Token refreshed successfully")
                } else {
                    Log.e(TAG, "Token refresh failed: ${result.exceptionOrNull()?.message}")
                }
            } catch (e: Exception) {
                Log.e(TAG, "Token refresh error", e)
            }
        }
    }

    // --- Channel management ---

    private fun createChannels() {
        val updatesAddress = globalParam.socketUpdates
        val onlinerAddress = globalParam.socketOnliner
        val usersAddress = globalParam.socketUsers
        val filesAddress = globalParam.socketFiles

        // Create notification gRPC manager for user info / avatar lookups
        notificationGrpcManager?.shutdown()
        val mgr = GrpcManager()
        val identityAddress = globalParam.socketIdentity
        if (identityAddress.isNotBlank()) {
            mgr.createIdentityClient(identityAddress, context, includeDeviceInfo = true)
        }
        if (usersAddress.isNotBlank()) {
            mgr.createUsersClient(usersAddress, context, includeDeviceInfo = true)
        }
        if (filesAddress.isNotBlank()) {
            mgr.createFilesClient(filesAddress, context, includeDeviceInfo = true)
        }
        val messagesAddress = globalParam.socketMessages
        if (messagesAddress.isNotBlank()) {
            mgr.createMessagesClient(messagesAddress, context, includeDeviceInfo = true)
        }
        notificationGrpcManager = mgr

        if (updatesAddress.isNotBlank()) {
            updatesChannel = createManagedChannel(updatesAddress)
            val intercepted = ClientInterceptors.intercept(
                updatesChannel!!,
                AuthInterceptor(context, GrpcManager()),
                DeviceInfoInterceptor(context)
            )
            updatesClient = UpdatesApiGrpcKt.UpdatesApiCoroutineStub(intercepted)
            Log.d(TAG, "Updates channel created: $updatesAddress")
        }

        if (onlinerAddress.isNotBlank()) {
            onlinerChannel = createManagedChannel(onlinerAddress)
            val intercepted = ClientInterceptors.intercept(
                onlinerChannel!!,
                AuthInterceptor(context, GrpcManager()),
                DeviceInfoInterceptor(context)
            )
            onlinerClient = OnlinerApiGrpcKt.OnlinerApiCoroutineStub(intercepted)
            Log.d(TAG, "Onliner channel created: $onlinerAddress")
        }
    }

    private fun recreateChannels() {
        Log.d(TAG, "Recreating gRPC channels")
        shutdownChannels()
        createChannels()
    }

    private fun shutdownChannels() {
        shutdownChannel(updatesChannel)
        shutdownChannel(onlinerChannel)
        updatesChannel = null
        onlinerChannel = null
        updatesClient = null
        onlinerClient = null
    }

    private fun shutdownChannel(channel: ManagedChannel?) {
        channel ?: return
        try {
            channel.shutdown()
            if (!channel.awaitTermination(3, TimeUnit.SECONDS)) {
                channel.shutdownNow()
            }
        } catch (e: InterruptedException) {
            channel.shutdownNow()
            Thread.currentThread().interrupt()
        }
    }

    private fun createManagedChannel(address: String): ManagedChannel {
        val url = if (!address.startsWith("http://") && !address.startsWith("https://")) {
            "http://$address"
        } else {
            address
        }
        val useTls = url.startsWith("https://")
        val hostPort = url.removePrefix("http://").removePrefix("https://")
        val parts = hostPort.split(":")
        val host = parts[0]
        val port = parts[1].toInt()

        val builder = OkHttpChannelBuilder.forAddress(host, port)
        if (useTls) {
            val trustManager = object : X509TrustManager {
                override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
                override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
                override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
            }
            val sslContext = SSLContext.getInstance("TLS")
            sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
            builder.sslSocketFactory(sslContext.socketFactory)
        } else {
            builder.usePlaintext()
        }
        return builder.build()
    }
}
