package com.barkfluff.client.notifications

import android.content.Context
import android.graphics.Bitmap
import android.graphics.drawable.BitmapDrawable
import android.util.Log
import barkfluff.updates.UpdatesApiOuterClass
import coil.request.ImageRequest
import coil.request.SuccessResult
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeSideEffects
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.widget.WidgetUpdater
import java.util.concurrent.ConcurrentHashMap

/**
 * App-реализация [RealtimeSideEffects]: уведомления о новых сообщениях ([NotificationHelper]),
 * загрузка аватара/превью ([AvatarLoader] + Coil) и обновление виджетов ([WidgetUpdater]).
 *
 * Вынесено из RealtimeService (модуль :core), чтобы ядро не зависело от UI/Notification/Widget/Coil.
 */
class RealtimeSideEffectsImpl(
    private val context: Context,
    private val grpcManager: GrpcManager
) : RealtimeSideEffects {

    private val globalParam = GlobalParam(context)
    private val userInfoCache = ConcurrentHashMap<Long, CachedUserInfo>()

    override fun onChatChanged(chatId: String) {
        WidgetUpdater.scheduleRefreshForChat(context, chatId)
    }

    override fun dismissChatNotifications(chatId: String) {
        NotificationHelper.dismissForChat(context, chatId)
    }

    override suspend fun showMessageNotification(event: UpdatesApiOuterClass.NewMessageEvent) {
        if (!globalParam.notificationsEnabled) return

        val msg = event.message ?: return
        val senderId = msg.senderId
        if (senderId == globalParam.userId) return

        val chatId = event.chatId
        // Чат открыт — не показываем уведомление
        if (OpenChatManager.isOpen(chatId)) return

        val messageId = msg.id
        val messageText = msg.content?.text ?: ""

        // Инфо об отправителе (cache first)
        val userInfo = userInfoCache[senderId] ?: run {
            val result = grpcManager.getUserData(senderId)
            if (result.isFailure) {
                Log.w(TAG, "Failed to get user data for notification: senderId=$senderId")
                return
            }
            val user = result.getOrNull()!!
            val displayName = "${user.firstName} ${user.lastName}".trim().ifEmpty { user.username }
            val info = CachedUserInfo(
                displayName,
                user.profilePicturePreviewFileId,
                user.profilePictureFileId,
                user.profilePicturePreviewUrl,
                user.profilePictureUrl
            )
            userInfoCache[senderId] = info
            info
        }

        // Аватар (headless, без View)
        var avatarBitmap: Bitmap? = null
        val avatarFileId = userInfo.avatarFileId.ifBlank { userInfo.avatarFullFileId }
        if (avatarFileId.isNotBlank()) {
            avatarBitmap = loadBitmapByFileId(avatarFileId)
        }
        if (avatarBitmap == null) {
            val directUrl = userInfo.avatarPreviewUrl.ifBlank { userInfo.avatarFullUrl }
            if (directUrl.isNotBlank()) {
                avatarBitmap = loadBitmapByUrl(directUrl)
            }
        }

        // Превью вложения-изображения, если есть
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
    }

    private suspend fun loadBitmapByFileId(fileId: String): Bitmap? {
        return try {
            // Сначала проверяем URL-кэш (заполняется AvatarLoader при загрузке списка чатов)
            var url = AvatarLoader.urlCache[fileId]
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
                AvatarLoader.urlCache[fileId] = url
            }
            val imageLoader = AvatarLoader.getImageLoader(context)
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
                Log.w(TAG, "Coil failed to load bitmap for fileId=$fileId")
                null
            }
        } catch (e: Exception) {
            Log.w(TAG, "Failed to load bitmap for fileId=$fileId", e)
            null
        }
    }

    private suspend fun loadBitmapByUrl(url: String): Bitmap? {
        return try {
            val imageLoader = AvatarLoader.getImageLoader(context)
            val request = ImageRequest.Builder(context)
                .data(url)
                .allowHardware(false)
                .build()
            val imageResult = imageLoader.execute(request)
            if (imageResult is SuccessResult) {
                (imageResult.drawable as? BitmapDrawable)?.bitmap
            } else null
        } catch (e: Exception) {
            Log.w(TAG, "Failed to load bitmap from URL directly", e)
            null
        }
    }

    private data class CachedUserInfo(
        val displayName: String,
        val avatarFileId: String,
        val avatarFullFileId: String,
        val avatarPreviewUrl: String,
        val avatarFullUrl: String
    )

    companion object {
        private const val TAG = "RealtimeSideEffects"
    }
}
