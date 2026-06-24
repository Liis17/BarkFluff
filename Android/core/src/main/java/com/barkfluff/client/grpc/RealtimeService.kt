package com.barkfluff.client.grpc

import android.content.Context
import android.util.Log
import com.barkfluff.client.data.OpenChatManager
import barkfluff.onliner.OnlinerApiOuterClass
import barkfluff.updates.UpdatesApiOuterClass
import com.barkfluff.client.data.GlobalParam
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlin.coroutines.coroutineContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlin.math.min
import kotlin.math.pow

/**
 * Сервис реального времени — подписывается на обновления сообщений, прочтений и онлайн-статусов.
 * Аналог RealtimeUpdateService + OnlineStatusService из WPF клиента.
 *
 * Использует общий GrpcManager из Application для всех gRPC вызовов.
 * Поддерживает resume/pause для корректной работы при сворачивании/разворачивании.
 */
class RealtimeService(
    private val context: Context,
    private val grpcManager: GrpcManager,
    private val sideEffects: RealtimeSideEffects? = null
) {

    companion object {
        private const val TAG = "RealtimeService"
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

    private val _messageEdited = MutableSharedFlow<UpdatesApiOuterClass.MessageEditedEvent>(extraBufferCapacity = 64)
    val messageEdited: SharedFlow<UpdatesApiOuterClass.MessageEditedEvent> = _messageEdited

    private val _messageDeleted = MutableSharedFlow<UpdatesApiOuterClass.MessageDeletedEvent>(extraBufferCapacity = 64)
    val messageDeleted: SharedFlow<UpdatesApiOuterClass.MessageDeletedEvent> = _messageDeleted

    private val _messagePinned = MutableSharedFlow<UpdatesApiOuterClass.MessagePinnedEvent>(extraBufferCapacity = 64)
    val messagePinned: SharedFlow<UpdatesApiOuterClass.MessagePinnedEvent> = _messagePinned

    private val _messageUnpinned = MutableSharedFlow<UpdatesApiOuterClass.MessageUnpinnedEvent>(extraBufferCapacity = 64)
    val messageUnpinned: SharedFlow<UpdatesApiOuterClass.MessageUnpinnedEvent> = _messageUnpinned

    private val _allMessagesUnpinned = MutableSharedFlow<UpdatesApiOuterClass.AllMessagesUnpinnedEvent>(extraBufferCapacity = 16)
    val allMessagesUnpinned: SharedFlow<UpdatesApiOuterClass.AllMessagesUnpinnedEvent> = _allMessagesUnpinned

    // --- Приватные чаты (user-scope) ---
    private val _privateMessages = MutableSharedFlow<UpdatesApiOuterClass.NewEncryptedMessageEvent>(extraBufferCapacity = 64)
    val privateMessages: SharedFlow<UpdatesApiOuterClass.NewEncryptedMessageEvent> = _privateMessages

    private val _privateMessageEdits = MutableSharedFlow<UpdatesApiOuterClass.EncryptedMessageEditedEvent>(extraBufferCapacity = 64)
    val privateMessageEdits: SharedFlow<UpdatesApiOuterClass.EncryptedMessageEditedEvent> = _privateMessageEdits

    private val _privateMessageDeletes = MutableSharedFlow<UpdatesApiOuterClass.EncryptedMessageDeletedEvent>(extraBufferCapacity = 64)
    val privateMessageDeletes: SharedFlow<UpdatesApiOuterClass.EncryptedMessageDeletedEvent> = _privateMessageDeletes

    private val _privateChatInvites = MutableSharedFlow<UpdatesApiOuterClass.PrivateChatInviteEvent>(extraBufferCapacity = 32)
    val privateChatInvites: SharedFlow<UpdatesApiOuterClass.PrivateChatInviteEvent> = _privateChatInvites

    private val _privateChatInviteResolutions = MutableSharedFlow<UpdatesApiOuterClass.PrivateChatInviteResolutionEvent>(extraBufferCapacity = 32)
    val privateChatInviteResolutions: SharedFlow<UpdatesApiOuterClass.PrivateChatInviteResolutionEvent> = _privateChatInviteResolutions

    // --- Секретные чаты (device-scope) ---
    private val _secretChatInvites = MutableSharedFlow<UpdatesApiOuterClass.SecretChatInviteEvent>(extraBufferCapacity = 16)
    val secretChatInvites: SharedFlow<UpdatesApiOuterClass.SecretChatInviteEvent> = _secretChatInvites

    private val _secretChatResolutions = MutableSharedFlow<UpdatesApiOuterClass.SecretChatInviteResolutionEvent>(extraBufferCapacity = 16)
    val secretChatResolutions: SharedFlow<UpdatesApiOuterClass.SecretChatInviteResolutionEvent> = _secretChatResolutions

    private val _secretMessages = MutableSharedFlow<UpdatesApiOuterClass.NewSecretMessageEvent>(extraBufferCapacity = 64)
    val secretMessages: SharedFlow<UpdatesApiOuterClass.NewSecretMessageEvent> = _secretMessages

    private val _connectionState = MutableStateFlow(ConnectionState.DISCONNECTED)
    val connectionState: StateFlow<ConnectionState> = _connectionState

    // Internal state
    private val globalParam = GlobalParam(context)
    private var serviceScope: CoroutineScope? = null
    private val seenMessageIds = LinkedHashSet<Long>()

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
        scope.launch { streamWithReconnect("MessagesEdited") { collectMessagesEdited() } }
        scope.launch { streamWithReconnect("MessagesDeleted") { collectMessagesDeleted() } }
        scope.launch { streamWithReconnect("MessagesPinned") { collectMessagesPinned() } }
        scope.launch { streamWithReconnect("MessagesUnpinned") { collectMessagesUnpinned() } }
        scope.launch { streamWithReconnect("AllMessagesUnpinned") { collectAllMessagesUnpinned() } }
        scope.launch { streamWithReconnect("OnlineStatus") { collectOnlineStatus() } }
        // E2E приватные чаты (user-scope)
        scope.launch { streamWithReconnect("PrivateMessages") { collectPrivateMessages() } }
        scope.launch { streamWithReconnect("PrivateMessageEdits") { collectPrivateMessageEdits() } }
        scope.launch { streamWithReconnect("PrivateMessageDeletes") { collectPrivateMessageDeletes() } }
        scope.launch { streamWithReconnect("PrivateChatInvites") { collectPrivateChatInvites() } }
        scope.launch { streamWithReconnect("PrivateChatInviteResolutions") { collectPrivateChatInviteResolutions() } }
        // E2E секретные чаты (device-scope)
        scope.launch { streamWithReconnect("SecretChatInvites") { collectSecretChatInvites() } }
        scope.launch { streamWithReconnect("SecretChatResolutions") { collectSecretChatResolutions() } }
        scope.launch { streamWithReconnect("SecretMessages") { collectSecretMessages() } }
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
            sideEffects?.onChatChanged(event.chatId)
        }
    }

    private suspend fun collectMessagesRead() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesReadRequest.getDefaultInstance()
        client.subscribeMessagesRead(request).collect { event ->
            Log.v(TAG, "Message read: chatId=${event.chatId}, msgId=${event.messageId}")
            _messagesRead.emit(event)
            // Если текущий пользователь в списке прочитавших — убираем уведомление из шторки
            val uid = globalParam.userId
            if (uid > 0 && event.newReadByList.contains(uid)) {
                sideEffects?.dismissChatNotifications(event.chatId)
                sideEffects?.onChatChanged(event.chatId)
            }
        }
    }

    private suspend fun collectMessagesEdited() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesEditedRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to MessagesEdited stream")
        client.subscribeMessagesEdited(request).collect { event ->
            Log.d(TAG, "Message edited received: chatId=${event.chatId}, msgId=${event.message.id}")
            _messageEdited.emit(event)
            sideEffects?.onChatChanged(event.chatId)
        }
    }

    private suspend fun collectMessagesDeleted() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesDeletedRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to MessagesDeleted stream")
        client.subscribeMessagesDeleted(request).collect { event ->
            Log.d(TAG, "Message deleted received: chatId=${event.chatId}, msgId=${event.messageId}")
            _messageDeleted.emit(event)
            sideEffects?.onChatChanged(event.chatId)
        }
    }

    private suspend fun collectMessagesPinned() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesPinnedRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to MessagesPinned stream")
        client.subscribeMessagesPinned(request).collect { event ->
            Log.d(TAG, "Message pinned received: chatId=${event.chatId}, msgId=${event.messageId}")
            _messagePinned.emit(event)
        }
    }

    private suspend fun collectMessagesUnpinned() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeMessagesUnpinnedRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to MessagesUnpinned stream")
        client.subscribeMessagesUnpinned(request).collect { event ->
            Log.d(TAG, "Message unpinned received: chatId=${event.chatId}, msgId=${event.messageId}")
            _messageUnpinned.emit(event)
        }
    }

    private suspend fun collectAllMessagesUnpinned() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeAllMessagesUnpinnedRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to AllMessagesUnpinned stream")
        client.subscribeAllMessagesUnpinned(request).collect { event ->
            Log.d(TAG, "All messages unpinned: chatId=${event.chatId}")
            _allMessagesUnpinned.emit(event)
        }
    }

    private suspend fun collectPrivateMessages() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribePrivateMessagesRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to PrivateMessages stream")
        client.subscribePrivateMessages(request).collect { event ->
            Log.v(TAG, "Private encrypted msg received: chatId=${event.chatId}, msgId=${event.message.id}")
            _privateMessages.emit(event)
        }
    }

    private suspend fun collectPrivateMessageEdits() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribePrivateMessageEditsRequest.getDefaultInstance()
        client.subscribePrivateMessageEdits(request).collect { event ->
            Log.v(TAG, "Private msg edited: chatId=${event.chatId}, msgId=${event.message.id}")
            _privateMessageEdits.emit(event)
        }
    }

    private suspend fun collectPrivateMessageDeletes() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribePrivateMessageDeletesRequest.getDefaultInstance()
        client.subscribePrivateMessageDeletes(request).collect { event ->
            Log.v(TAG, "Private msg deleted: chatId=${event.chatId}, msgId=${event.messageId}")
            _privateMessageDeletes.emit(event)
        }
    }

    private suspend fun collectPrivateChatInvites() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribePrivateChatInvitesRequest.getDefaultInstance()
        client.subscribePrivateChatInvites(request).collect { event ->
            Log.d(TAG, "Private chat invite received: chatId=${event.chatId}, inviter=${event.inviterUserId}")
            _privateChatInvites.emit(event)
        }
    }

    private suspend fun collectPrivateChatInviteResolutions() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribePrivateChatInviteResolutionsRequest.getDefaultInstance()
        client.subscribePrivateChatInviteResolutions(request).collect { event ->
            Log.d(TAG, "Private chat invite resolution: chatId=${event.chatId}, accepted=${event.accepted}")
            _privateChatInviteResolutions.emit(event)
        }
    }

    private suspend fun collectSecretChatInvites() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeSecretChatInvitesRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to SecretChatInvites stream (device-scope)")
        client.subscribeSecretChatInvites(request).collect { event ->
            Log.d(TAG, "Secret chat invite received: inviteId=${event.inviteId}, sender=${event.senderUserId}/${event.senderDeviceId}")
            _secretChatInvites.emit(event)
        }
    }

    private suspend fun collectSecretChatResolutions() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeSecretChatResolutionsRequest.getDefaultInstance()
        client.subscribeSecretChatResolutions(request).collect { event ->
            Log.d(TAG, "Secret chat resolution: inviteId=${event.inviteId}, accepted=${event.accepted}")
            _secretChatResolutions.emit(event)
        }
    }

    private suspend fun collectSecretMessages() {
        val client = grpcManager.updatesClient ?: throw IllegalStateException("Updates client not created")
        val request = UpdatesApiOuterClass.SubscribeSecretMessagesRequest.getDefaultInstance()
        Log.d(TAG, "Subscribing to SecretMessages stream (device-scope)")
        client.subscribeSecretMessages(request).collect { event ->
            Log.v(TAG, "Secret envelope received: msgId=${event.envelope.messageId}")
            _secretMessages.emit(event)
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
        val se = sideEffects ?: return
        try {
            _newMessages.collect { event ->
                try {
                    se.showMessageNotification(event)
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

    // Обновление токена делегируется в GrpcManager — единый мьютекс на все стримы и операции,
    // чтобы параллельные рефреши не аннулировали refresh-токен друг друга.
    private suspend fun ensureTokenValid() {
        grpcManager.ensureTokenValid(context)
    }

    private suspend fun forceRefreshToken() {
        grpcManager.forceRefreshToken(context)
    }
}
