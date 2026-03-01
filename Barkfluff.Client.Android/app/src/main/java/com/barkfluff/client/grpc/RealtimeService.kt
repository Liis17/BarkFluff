package com.barkfluff.client.grpc

import android.content.Context
import android.graphics.Bitmap
import android.graphics.drawable.BitmapDrawable
import android.util.Log
import com.barkfluff.client.data.OpenChatManager
import barkfluff.onliner.OnlinerApiOuterClass
import barkfluff.updates.UpdatesApiOuterClass
import coil.request.ImageRequest
import coil.request.SuccessResult
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlin.coroutines.coroutineContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.min
import kotlin.math.pow

/**
 * Сервис реального времени — подписывается на обновления сообщений, прочтений и онлайн-статусов.
 * Аналог RealtimeUpdateService + OnlineStatusService из WPF клиента.
 *
 * Использует общий GrpcManager из Application для всех gRPC вызовов.
 * Поддерживает resume/pause для корректной работы при сворачивании/разворачивании.
 */
class RealtimeService(private val context: Context, private val grpcManager: GrpcManager) {

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

    private data class CachedUserInfo(
        val displayName: String,
        val avatarFileId: String,
        val avatarFullFileId: String,
        val avatarPreviewUrl: String,
        val avatarFullUrl: String
    )

    // Internal state
    private val globalParam = GlobalParam(context)
    private var serviceScope: CoroutineScope? = null
    private val tokenRefreshMutex = Mutex()
    private val seenMessageIds = LinkedHashSet<Long>()
    private val userInfoCache = ConcurrentHashMap<Long, CachedUserInfo>()

    // Online subscription state
    @Volatile
    private var subscribedUserIds: List<Long> = emptyList()

    /**
     * Возобновляет стримы реального времени.
     * Безопасно вызывать повторно — пересоздаёт scope если предыдущий был отменён.
     */
    fun resume() {
        // Если scope ещё активен, ничего не делаем
        val currentScope = serviceScope
        if (currentScope != null && currentScope.isActive) {
            Log.v(TAG, "resume: scope already active, skipping")
            return
        }

        Log.i(TAG, "Resuming realtime streams")

        // Пересоздаём каналы принудительно — старые могли сломаться (DNS failure после фона)
        grpcManager.recreateAllClients(context, globalParam)

        val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        serviceScope = scope

        scope.launch { streamWithReconnect("NewMessages") { collectNewMessages() } }
        scope.launch { streamWithReconnect("MessagesRead") { collectMessagesRead() } }
        scope.launch { streamWithReconnect("OnlineStatus") { collectOnlineStatus() } }
        scope.launch { onlinePingLoop() }
        scope.launch { notificationLoop() }
    }

    /**
     * Приостанавливает все стримы (при сворачивании приложения).
     * Каналы НЕ закрываются — они управляются GrpcManager.
     */
    fun pause() {
        Log.i(TAG, "Pausing realtime streams")
        _connectionState.value = ConnectionState.DISCONNECTED
        serviceScope?.cancel()
        serviceScope = null
    }

    /**
     * Полностью останавливает сервис (при завершении приложения).
     */
    fun shutdown() {
        Log.i(TAG, "Shutting down realtime streams")
        _connectionState.value = ConnectionState.DISCONNECTED
        serviceScope?.cancel()
        serviceScope = null
    }

    /**
     * Обновляет список пользователей для отслеживания онлайн-статуса.
     */
    fun changeOnlineSubscription(userIds: List<Long>) {
        subscribedUserIds = userIds
        val scope = serviceScope ?: return
        scope.launch {
            try {
                val client = grpcManager.onlinerClient ?: return@launch
                val request = OnlinerApiOuterClass.ChangeUsersInSubscriptionRequest.newBuilder()
                    .addAllUserIds(userIds)
                    .build()
                client.changeUsersInSubscription(request)
                Log.v(TAG, "Online subscription updated: ${userIds.size} users")
            } catch (e: Exception) {
                Log.w(TAG, "Failed to change online subscription", e)
            }
        }
    }

    /**
     * Отмечает сообщение как прочитанное (вызывается из BroadcastReceiver).
     */
    fun markAsRead(messageId: Long) {
        val scope = serviceScope ?: return
        scope.launch {
            try {
                grpcManager.markAsRead(listOf(messageId))
                Log.v(TAG, "Marked message $messageId as read")
            } catch (e: Exception) {
                Log.w(TAG, "Failed to mark message as read: ${e.message}")
            }
        }
    }

    // --- Stream collectors ---

    private suspend fun collectNewMessages() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeNewMessagesRequest.getDefaultInstance()
        _connectionState.value = ConnectionState.CONNECTED
        client.subscribeNewMessages(request).collect { event ->
            val msgId = event.message.id
            // Dedup
            synchronized(seenMessageIds) {
                if (!seenMessageIds.add(msgId)) {
                    return@collect
                }
                if (seenMessageIds.size > DEDUP_MAX_SIZE) {
                    val iter = seenMessageIds.iterator()
                    iter.next()
                    iter.remove()
                }
            }
            Log.v(TAG, "New message: id=$msgId, chatId=${event.chatId}")
            _newMessages.emit(event)
        }
    }

    private suspend fun collectMessagesRead() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesReadRequest.getDefaultInstance()
        client.subscribeMessagesRead(request).collect { event ->
            Log.v(TAG, "Message read: chatId=${event.chatId}, msgId=${event.messageId}")
            _messagesRead.emit(event)
        }
    }

    private suspend fun collectOnlineStatus() {
        val client = grpcManager.onlinerClient ?: throw IllegalStateException("Onliner client not created")
        val request = OnlinerApiOuterClass.SubscribeToOnlineStatusRequest.newBuilder()
            .addAllUserIds(subscribedUserIds)
            .build()
        client.subscribeToOnlineStatus(request).collect { status ->
            Log.v(TAG, "Online status: userId=${status.userId}, status=${status.status}")
            _onlineStatuses.emit(status)
        }
    }

    private suspend fun onlinePingLoop() {
        while (coroutineContext.isActive) {
            try {
                val client = grpcManager.onlinerClient
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

                    // Проверяем, открыт ли этот чат сейчас — если да, не показываем уведомление
                    if (OpenChatManager.isOpen(chatId)) {
                        return@collect
                    }

                    val messageId = msg.id
                    val messageText = msg.content?.text ?: ""

                    // Resolve sender info (cache first)
                    val userInfo = userInfoCache[senderId] ?: run {
                        val result = grpcManager.getUserData(senderId)
                        if (result.isFailure) {
                            Log.w(TAG, "Failed to get user data for notification: senderId=$senderId")
                            return@collect
                        }
                        val user = result.getOrNull()!!
                        val displayName = "${user.firstName} ${user.lastName}".trim().ifEmpty { user.username }
                        val previewId = user.profilePicturePreviewFileId
                        val fullId = user.profilePictureFileId
                        Log.d(TAG, "User $senderId avatar: previewFileId='$previewId', fullFileId='$fullId', previewUrl='${user.profilePicturePreviewUrl}', fullUrl='${user.profilePictureUrl}'")
                        val info = CachedUserInfo(displayName, previewId, fullId, user.profilePicturePreviewUrl, user.profilePictureUrl)
                        userInfoCache[senderId] = info
                        info
                    }

                    // Load avatar bitmap (headless, no View needed)
                    var avatarBitmap: Bitmap? = null
                    val avatarFileId = userInfo.avatarFileId.ifBlank { userInfo.avatarFullFileId }
                    if (avatarFileId.isNotBlank()) {
                        avatarBitmap = loadBitmapByFileId(avatarFileId)
                    }
                    // Fallback: попробовать загрузить напрямую из URL (если fileId не сработал)
                    if (avatarBitmap == null) {
                        val directUrl = userInfo.avatarPreviewUrl.ifBlank { userInfo.avatarFullUrl }
                        if (directUrl.isNotBlank()) {
                            avatarBitmap = loadBitmapByUrl(directUrl)
                        }
                    }

                    // Load image attachment if present
                    var imageBitmap: Bitmap? = null
                    try {
                        val attachments = msg.content?.attachmentsList
                        val imageAttachment = attachments?.firstOrNull {
                            it.type == barkfluff.shared.Shared.MessageAttachmentType.IMAGE
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
            // Сначала проверяем URL кэш (заполняется AvatarLoader при загрузке списка чатов)
            var url = com.barkfluff.client.utils.AvatarLoader.urlCache[fileId]

            if (url == null) {
                val urlResult = grpcManager.getFileDownloadUrl(fileId)
                if (urlResult.isFailure) {
                    Log.w(TAG, "Failed to get download URL for fileId=$fileId: ${urlResult.exceptionOrNull()?.message}")
                    return null
                }
                url = urlResult.getOrNull()
                if (url.isNullOrBlank()) {
                    Log.w(TAG, "Empty download URL for fileId=$fileId")
                    return null
                }
                com.barkfluff.client.utils.AvatarLoader.urlCache[fileId] = url
            }

            // Используем общий ImageLoader с disk/memory cache
            val imageLoader = com.barkfluff.client.utils.AvatarLoader.getImageLoader(context)
            val request = ImageRequest.Builder(context)
                .data(url)
                .memoryCacheKey(fileId)
                .diskCacheKey(fileId)
                .allowHardware(false)
                .build()
            val imageResult = imageLoader.execute(request)
            if (imageResult is SuccessResult) {
                (imageResult.drawable as? BitmapDrawable)?.bitmap
            } else {
                Log.w(TAG, "Coil failed to load bitmap for fileId=$fileId, result=${imageResult.javaClass.simpleName}")
                null
            }
        } catch (e: Exception) {
            Log.w(TAG, "Failed to load bitmap for fileId=$fileId", e)
            null
        }
    }

    private suspend fun loadBitmapByUrl(url: String): Bitmap? {
        return try {
            val imageLoader = com.barkfluff.client.utils.AvatarLoader.getImageLoader(context)
            val request = ImageRequest.Builder(context)
                .data(url)
                .allowHardware(false)
                .build()
            val imageResult = imageLoader.execute(request)
            if (imageResult is SuccessResult) {
                (imageResult.drawable as? BitmapDrawable)?.bitmap
            } else {
                Log.w(TAG, "Coil failed to load bitmap from URL directly")
                null
            }
        } catch (e: Exception) {
            Log.w(TAG, "Failed to load bitmap from URL directly", e)
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
                Log.v(TAG, "[$name] Connecting...")
                block()
                // Stream ended normally (server closed) — reset and reconnect
                attempts = 0
                Log.v(TAG, "[$name] Stream ended normally, reconnecting")
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
                Log.v(TAG, "[$name] Waiting ${backoff}ms before reconnect")
                delay(backoff)

                // Переинициализируем клиенты (каналы могли сломаться)
                grpcManager.recreateAllClients(context, globalParam)
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
                // Убеждаемся что identity клиент инициализирован в общем GrpcManager
                grpcManager.createIdentityClient(identityAddress, context, includeDeviceInfo = true)
                val result = grpcManager.refreshAccessToken(refreshToken, globalParam.refreshTokenExpiration)

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
}
