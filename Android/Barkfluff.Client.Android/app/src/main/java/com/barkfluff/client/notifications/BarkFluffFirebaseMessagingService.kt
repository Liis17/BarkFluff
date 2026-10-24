package com.barkfluff.client.notifications

import android.graphics.Bitmap
import android.util.Log
import coil.ImageLoader
import coil.request.ImageRequest
import com.barkfluff.client.R
import com.barkfluff.client.calls.CallTelecomManager
import com.barkfluff.client.calls.IncomingCallPrefetch
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.grpc.RealtimeSideEffects
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.utils.AvatarLoader
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.util.UUID

internal suspend fun dispatchEncryptedMarker(
    data: Map<String, String>, effects: RealtimeSideEffects, expectedScopeId: String? = null
): Boolean {
    val senderUserId = data["sender_user_id"]?.toLongOrNull()?.takeIf { it > 0 } ?: return false
    return when (data["type"]) {
        "new_private_message" -> {
            val chatId = data["private_chat_id"]?.takeIf(String::isNotBlank) ?: return false
            val eventId = data["event_id"]?.toLongOrNull()?.takeIf { it > 0 } ?: return false
            effects.showPrivateMessageNotification(chatId, eventId, senderUserId, expectedScopeId)
            true
        }
        "new_secret_message" -> {
            val eventId = runCatching { UUID.fromString(data["event_id"]).toString() }.getOrNull() ?: return false
            val senderDeviceId = runCatching { UUID.fromString(data["sender_device_id"]).toString() }.getOrNull() ?: return false
            effects.showSecretMessageNotification(eventId, senderUserId, senderDeviceId, expectedScopeId)
            true
        }
        else -> false
    }
}

@AndroidEntryPoint
class BarkFluffFirebaseMessagingService : FirebaseMessagingService() {

    @javax.inject.Inject lateinit var audioPlayback: com.barkfluff.client.audio.AudioPlayback
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var realtimeSideEffects: RealtimeSideEffects

    companion object {
        private const val TAG = "BarkFluffFCM"

        // Сколько ждём аватар звонящего перед показом звонка
        private const val AVATAR_PREFETCH_TIMEOUT_MS = 2500L

        // MessageAttachmentType enum values from proto
        private const val ATTACHMENT_TYPE_IMAGE = 1
        private const val ATTACHMENT_TYPE_VIDEO = 2
        private const val ATTACHMENT_TYPE_GIF = 3
        private const val ATTACHMENT_TYPE_DOCUMENT = 4
        private const val ATTACHMENT_TYPE_AUDIO = 5
        private const val ATTACHMENT_TYPE_VOICE = 6
        private const val ATTACHMENT_TYPE_STICKER = 7
    }

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onNewToken(token: String) {
        Log.d(TAG, "onNewToken: получен новый FCM токен")

        // Сохраняем токен локально
        val globalParam = GlobalParam(applicationContext)
        globalParam.firebaseToken = token

        // Пытаемся отправить на сервер если клиент уже инициализирован
        sendTokenToServer(applicationContext, token)
    }

    override fun onMessageReceived(remoteMessage: RemoteMessage) {
        Log.d(TAG, "onMessageReceived: от ${remoteMessage.from}")

        val data = remoteMessage.data
        when (data["type"]) {
            "incoming_call" -> {
                handleIncomingCall(data)
                return
            }
            "dismiss_call" -> {
                val callId = data["call_id"]
                if (!callId.isNullOrBlank()) {
                    NotificationHelper.dismissCall(applicationContext, callId)
                }
                return
            }
            "private_chat_invite" -> {
                handlePrivateChatInvite(data)
                return
            }
            "new_private_message" -> {
                val scopeId = CacheScope.from(GlobalParam(applicationContext))?.id ?: return
                serviceScope.launch { dispatchEncryptedMarker(data, realtimeSideEffects, scopeId) }
                return
            }
            "new_secret_message" -> {
                val scopeId = CacheScope.from(GlobalParam(applicationContext))?.id ?: return
                serviceScope.launch { dispatchEncryptedMarker(data, realtimeSideEffects, scopeId) }
                return
            }
            null, "new_message", "dismiss_chat_notifications" -> Unit
            else -> return
        }

        // Команда dismiss: убираем нотификацию чата (после прочтения на другом устройстве)
        if (data["type"] == "dismiss_chat_notifications") {
            val dismissChatId = data["chat_id"]
            if (dismissChatId.isNullOrBlank()) {
                Log.w(TAG, "dismiss_chat_notifications без chat_id, пропускаем")
                return
            }
            Log.d(TAG, "dismiss_chat_notifications: chatId=$dismissChatId")
            val messageId = if ("message_id" in data) {
                data["message_id"]?.toLongOrNull()?.takeIf { it >= 0 } ?: return
            } else 0L
            NotificationHelper.dismissForChat(applicationContext, dismissChatId, messageId)
            return
        }

        // Извлекаем данные
        val chatId = data["chat_id"] ?: return
        val globalParam = GlobalParam(applicationContext)
        val scopeId = CacheScope.from(globalParam)?.id ?: return
        if (!globalParam.notificationsEnabled || OpenChatManager.isOpen(chatId)) return

        // Guard: чат замьючен пользователем — не показываем локальное уведомление.
        // Сервер уже подавляет push для замьюченных чатов; это защита от гонок кэша токенов.
        if (chatId in GlobalParam(applicationContext).mutedChatIds) {
            Log.d(TAG, "onMessageReceived: чат $chatId замьючен, уведомление пропущено")
            return
        }

        val senderId = data["sender_id"]?.toLongOrNull() ?: return
        if (senderId == globalParam.userId) return
        val senderName = data["sender_name"]?.takeIf { it.isNotBlank() }
            ?: getString(R.string.notification_unknown_sender)
        val avatarUrl = data["avatar_url"]?.takeIf { it.isNotBlank() }
        val chatTitle = data["chat_title"]?.takeIf { it.isNotBlank() }
        val chatAvatarUrl = data["chat_avatar_url"]?.takeIf { it.isNotBlank() }
        val isGroupChat = data["is_group_chat"]?.toBoolean() ?: false
        val contentType = data["content_type"]?.toIntOrNull() ?: 0
        val messageId = data["message_id"]?.toLongOrNull() ?: 0
        val attachmentCount = data["attachment_count"]?.toIntOrNull() ?: 0
        val messageText = data["message_text"] ?: ""
        val timestamp = data["sent_at"]?.toLongOrNull()?.let { if (it < 1_000_000_000_000L) it * 1_000L else it }
            ?: remoteMessage.sentTime
        val input = RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = messageId,
            senderId = senderId,
            senderName = senderName,
            chatTitle = if (isGroupChat) chatTitle.orEmpty() else senderName,
            isGroupChat = isGroupChat,
            timestampMillis = timestamp,
            text = messageText,
            attachmentKind = when (contentType) {
                ATTACHMENT_TYPE_IMAGE -> "image"
                ATTACHMENT_TYPE_VIDEO -> "video"
                ATTACHMENT_TYPE_GIF -> "gif"
                ATTACHMENT_TYPE_DOCUMENT -> "document"
                ATTACHMENT_TYPE_AUDIO -> "audio"
                ATTACHMENT_TYPE_VOICE -> "voice"
                ATTACHMENT_TYPE_STICKER -> "sticker"
                else -> ""
            },
            attachmentCount = attachmentCount.coerceAtLeast(1),
            senderNameIsFallback = data["sender_name"].isNullOrBlank(),
            chatTitleIsFallback = if (isGroupChat) data["chat_title"].isNullOrBlank()
                else data["sender_name"].isNullOrBlank(),
            // FCM carries the existing short preview. A later realtime event can contain full text.
            contentIsComplete = false,
        )

        Log.d(TAG, "onMessageReceived: chatId=$chatId, sender=$senderName, isGroup=$isGroupChat, messageId=$messageId")
        NotificationHelper.showRegularMessageNotification(applicationContext, input)

        // The notification is already visible with initials. Avatar work can enrich it only while
        // the same account and chat are still eligible; attachment media is summarized as text.
        serviceScope.launch {
            try {
                val senderBitmap = loadBitmapFromUrl(avatarUrl)
                val chatBitmap = if (isGroupChat) loadBitmapFromUrl(chatAvatarUrl) else senderBitmap
                val current = GlobalParam(applicationContext)
                if (CacheScope.from(current)?.id == scopeId && current.notificationsEnabled &&
                    chatId !in current.mutedChatIds && !OpenChatManager.isOpen(chatId)) {
                    NotificationHelper.showRegularMessageNotification(
                        applicationContext, input, senderBitmap, chatBitmap,
                    )
                }
            } catch (e: Exception) {
                Log.w(TAG, "Unable to enrich message notification avatar", e)
            }
        }
    }


    private fun handlePrivateChatInvite(data: Map<String, String>) {
        val chatId = data["chat_id"]
        if (chatId.isNullOrBlank()) {
            Log.w(TAG, "private_chat_invite без chat_id, пропускаем")
            return
        }
        val inviterName = data["inviter_name"]?.takeIf { it.isNotBlank() } ?: "BarkFluff"
        val avatarUrl = data["avatar_url"]?.takeIf { it.isNotBlank() }

        serviceScope.launch {
            val avatarBitmap = loadBitmapFromUrl(avatarUrl)
            withContext(Dispatchers.Main) {
                NotificationHelper.showPrivateInviteNotification(
                    context = applicationContext,
                    inviterName = inviterName,
                    chatId = chatId,
                    avatarBitmap = avatarBitmap
                )
            }
        }
        Log.d(TAG, "private_chat_invite: chatId=$chatId, inviter=$inviterName")
    }

    private fun handleIncomingCall(data: Map<String, String>) {
        val callId = data["call_id"]
        if (callId.isNullOrBlank()) {
            Log.w(TAG, "incoming_call без call_id, пропускаем")
            return
        }

        val rawMediaType = data["media_type"].orEmpty()
        val mediaType = if (rawMediaType.equals("2") || rawMediaType.contains("VIDEO", ignoreCase = true)) {
            "video"
        } else {
            "audio"
        }
        val callerName = data["caller_name"]?.takeIf { it.isNotBlank() }
            ?: data["chat_title"]?.takeIf { it.isNotBlank() }
            ?: applicationContext.getString(R.string.app_name)

        val callerUserId = data["caller_user_id"]?.toLongOrNull() ?: 0L
        val chatId = data["chat_id"].orEmpty()
        val chatTitle = data["chat_title"].orEmpty()
        val avatarUrl = data["avatar_url"]?.takeIf { it.isNotBlank() }

        serviceScope.launch {
            withContext(Dispatchers.Main.immediate) { audioPlayback.pause() }
            // Аватар готовим до показа звонка: при убитом приложении процесс поднимает FCM,
            // gRPC-клиентов ещё нет, и звонок успевал показаться только с инициалами.
            val prefetched = withTimeoutOrNull(AVATAR_PREFETCH_TIMEOUT_MS) {
                IncomingCallPrefetch.prepareAvatar(applicationContext, callId, callerUserId, avatarUrl)
            }
            if (prefetched == null) {
                Log.w(TAG, "incoming_call: аватар не готов за ${AVATAR_PREFETCH_TIMEOUT_MS}мс, показываем с инициалами")
            }

            withContext(Dispatchers.Main) {
                CallTelecomManager.reportIncomingCall(
                    context = applicationContext,
                    callId = callId,
                    callerName = callerName,
                    mediaType = mediaType,
                    callerUserId = callerUserId,
                    chatId = chatId,
                    chatTitle = chatTitle
                )

                NotificationHelper.showIncomingCallNotification(
                    context = applicationContext,
                    callId = callId,
                    callerName = callerName,
                    mediaType = mediaType,
                    callerUserId = callerUserId,
                    chatId = chatId,
                    chatTitle = chatTitle
                )
            }
            Log.d(TAG, "incoming_call notification shown: callId=$callId, mediaType=$mediaType")
        }
    }

    /**
     * Загружает Bitmap из URL с использованием Coil.
     */
    private suspend fun loadBitmapFromUrl(url: String?): Bitmap? {
        if (url.isNullOrBlank()) return null

        return try {
            val imageLoader = AvatarLoader.getImageLoader(applicationContext)
            val request = ImageRequest.Builder(applicationContext)
                .data(url)
                .memoryCacheKey(url)
                .diskCacheKey(url)
                .size(512) // Ограничиваем размер для уведомлений
                .build()

            val result = imageLoader.execute(request)
            (result.drawable as? android.graphics.drawable.BitmapDrawable)?.bitmap
        } catch (e: Exception) {
            Log.w(TAG, "loadBitmapFromUrl: failed to load", e)
            null
        }
    }

    /**
     * Отправляет Firebase токен на сервер.
     * Вызывается при получении нового токена и при запуске приложения.
     */
    fun sendTokenToServer(context: android.content.Context, token: String) {
        serviceScope.launch {
            try {
                val result = userProfileGateway.setFirebaseToken(token)
                if (result.isSuccess) {
                    Log.i(TAG, "sendTokenToServer: токен успешно отправлен на сервер")
                } else {
                    Log.e(TAG, "sendTokenToServer: ошибка отправки токена: ${result.exceptionOrNull()?.message}")
                }
            } catch (e: Exception) {
                Log.e(TAG, "sendTokenToServer: исключение", e)
            }
        }
    }
}
