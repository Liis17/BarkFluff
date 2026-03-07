package com.barkfluff.client.notifications

import android.graphics.Bitmap
import android.util.Log
import coil.ImageLoader
import coil.request.ImageRequest
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class BarkFluffFirebaseMessagingService : FirebaseMessagingService() {

    companion object {
        private const val TAG = "BarkFluffFCM"

        // MessageAttachmentType enum values from proto
        private const val ATTACHMENT_TYPE_IMAGE = 1
        private const val ATTACHMENT_TYPE_VIDEO = 2
        private const val ATTACHMENT_TYPE_GIF = 3
        private const val ATTACHMENT_TYPE_DOCUMENT = 4
        private const val ATTACHMENT_TYPE_AUDIO = 5
        private const val ATTACHMENT_TYPE_VOICE = 6
    }

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onNewToken(token: String) {
        Log.d(TAG, "onNewToken: получен новый FCM токен: ${token.take(20)}...")

        // Сохраняем токен локально
        val globalParam = GlobalParam(applicationContext)
        globalParam.firebaseToken = token

        // Пытаемся отправить на сервер если клиент уже инициализирован
        sendTokenToServer(applicationContext, token)
    }

    override fun onMessageReceived(remoteMessage: RemoteMessage) {
        Log.d(TAG, "onMessageReceived: от ${remoteMessage.from}")

        val data = remoteMessage.data

        // Извлекаем данные
        val chatId = data["chat_id"] ?: return
        val senderId = data["sender_id"]?.toLongOrNull() ?: return
        val senderName = data["sender_name"]?.takeIf { it.isNotBlank() } ?: "Unknown"
        val avatarUrl = data["avatar_url"]?.takeIf { it.isNotBlank() }
        val chatTitle = data["chat_title"]?.takeIf { it.isNotBlank() }
        val chatAvatarUrl = data["chat_avatar_url"]?.takeIf { it.isNotBlank() }
        val isGroupChat = data["is_group_chat"]?.toBoolean() ?: false
        val contentType = data["content_type"]?.toIntOrNull() ?: 0
        val imageUrl = data["image_url"]?.takeIf { it.isNotBlank() }
        val messageId = data["message_id"]?.toLongOrNull() ?: 0
        val messageText = remoteMessage.notification?.body ?: data["message_text"] ?: ""

        // Проверяем дубликаты (если gRPC уже показал уведомление)
        synchronized(NotificationHelper.recentlyShownMessages) {
            if (NotificationHelper.recentlyShownMessages.contains(messageId)) {
                Log.d(TAG, "Skipping duplicate notification for messageId=$messageId")
                return
            }
            NotificationHelper.recentlyShownMessages.add(messageId)
            // Очищаем старые записи для предотвращения утечки памяти
            if (NotificationHelper.recentlyShownMessages.size > 100) {
                NotificationHelper.recentlyShownMessages.clear()
            }
        }

        Log.d(TAG, "onMessageReceived: chatId=$chatId, sender=$senderName, isGroup=$isGroupChat, contentType=$contentType")

        // Определяем отображаемое имя и аватар
        val displayName = if (isGroupChat && !chatTitle.isNullOrBlank()) {
            "$senderName в $chatTitle"
        } else {
            senderName
        }

        val displayAvatarUrl = if (isGroupChat && !chatAvatarUrl.isNullOrBlank()) {
            chatAvatarUrl
        } else {
            avatarUrl
        }

        // Формируем текст в зависимости от типа контента
        val displayText = when (contentType) {
            ATTACHMENT_TYPE_IMAGE -> if (messageText.isBlank()) "\uD83D\uDCF7 Фото" else "\uD83D\uDCF7 $messageText"
            ATTACHMENT_TYPE_VIDEO -> "\uD83C\uDFAC Видео"
            ATTACHMENT_TYPE_GIF -> "\uD83C\uDF7F GIF"
            ATTACHMENT_TYPE_DOCUMENT -> "\uD83D\uDCC4 Документ"
            ATTACHMENT_TYPE_AUDIO -> "\uD83C\uDFB5 Аудио"
            ATTACHMENT_TYPE_VOICE -> "\uD83C\uDFA4 Голосовое сообщение"
            else -> messageText
        }

        // Загружаем изображения в фоне и показываем уведомление
        serviceScope.launch {
            try {
                // Загружаем аватар
                val avatarBitmap = loadBitmapFromUrl(displayAvatarUrl)

                // Загружаем превью картинки если это изображение
                val imageBitmap = if (contentType == ATTACHMENT_TYPE_IMAGE && !imageUrl.isNullOrBlank()) {
                    loadBitmapFromUrl(imageUrl)
                } else null

                // Показываем уведомление
                withContext(Dispatchers.Main) {
                    NotificationHelper.showMessageNotification(
                        context = applicationContext,
                        senderName = displayName,
                        messageText = displayText,
                        avatarBitmap = avatarBitmap,
                        chatId = chatId,
                        messageId = messageId,
                        imageBitmap = imageBitmap
                    )
                }

                Log.d(TAG, "onMessageReceived: notification shown, hasAvatar=${avatarBitmap != null}, hasImage=${imageBitmap != null}")
            } catch (e: Exception) {
                Log.e(TAG, "onMessageReceived: error loading images", e)

                // Показываем уведомление без изображений
                withContext(Dispatchers.Main) {
                    NotificationHelper.showMessageNotification(
                        context = applicationContext,
                        senderName = displayName,
                        messageText = displayText,
                        avatarBitmap = null,
                        chatId = chatId,
                        messageId = messageId,
                        imageBitmap = null
                    )
                }
            }
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
            Log.w(TAG, "loadBitmapFromUrl: failed to load $url", e)
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
                val app = context.applicationContext as BarkFluffApplication
                val grpcManager = app.grpcManager

                if (grpcManager.usersClient == null) {
                    Log.d(TAG, "sendTokenToServer: usersClient не инициализирован, токен будет отправлен позже")
                    return@launch
                }

                val result = grpcManager.setFirebaseToken(token)
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
