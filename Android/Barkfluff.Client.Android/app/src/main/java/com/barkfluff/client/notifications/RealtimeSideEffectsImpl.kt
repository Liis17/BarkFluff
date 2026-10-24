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
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.grpc.RealtimeSideEffects
import com.barkfluff.client.repository.SecretChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.widget.WidgetUpdater
import java.util.concurrent.ConcurrentHashMap
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

/**
 * App-реализация [RealtimeSideEffects]: уведомления о новых сообщениях ([NotificationHelper]),
 * загрузка аватара/превью ([AvatarLoader] + Coil) и обновление виджетов ([WidgetUpdater]).
 *
 * Вынесено из RealtimeService (модуль :core), чтобы ядро не зависело от UI/Notification/Widget/Coil.
 */
class RealtimeSideEffectsImpl(
    private val context: Context,
    private val userProfileGateway: UserProfileGateway,
    private val fileMediaGateway: FileMediaGateway,
    private val secretChatRepository: SecretChatRepository,
    private val chatRepository: ChatRepository,
    private val chatCacheRepository: ChatCacheRepository,
    private val chatInfoLookup: suspend (String) -> Result<ChatRepository.ChatInfo> = { chatId ->
        chatRepository.getChatInfo(chatId)
    },
) : RealtimeSideEffects {

    private val globalParam = GlobalParam(context)
    private val userInfoCache = ConcurrentHashMap<Pair<String, Long>, CachedUserInfo>()
    private val notificationEnrichmentPermits = Semaphore(2)
    private val notificationEnrichmentScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    internal var notificationEnrichmentFinished: ((String, String) -> Unit)? = null

    override fun onChatChanged(chatId: String) {
        WidgetUpdater.scheduleRefreshForChat(context, chatId)
    }

    override fun dismissChatNotifications(chatId: String, messageId: Long) {
        NotificationHelper.dismissForChat(context, chatId, messageId)
    }

    override suspend fun showMessageNotification(event: UpdatesApiOuterClass.NewMessageEvent) {
        if (!globalParam.notificationsEnabled) return
        val scopeId = CacheScope.from(globalParam)?.id ?: return

        val msg = event.message ?: return
        val senderId = msg.senderId
        if (senderId == globalParam.userId) return

        val chatId = event.chatId
        val scope = CacheScope(scopeId)
        // Чат открыт — не показываем уведомление
        if (OpenChatManager.isOpen(chatId) || chatId in globalParam.mutedChatIds) return

        val cachedChat = chatCacheRepository.readChatList(scope)?.chats?.firstOrNull { it.id == chatId }
        if (cachedChat != null && cachedChat.chatType != barkfluff.shared.Shared.ChatType.CHAT_TYPE_REGULAR) return
        val fallbackName = context.getString(com.barkfluff.client.R.string.notification_unknown_sender_id, senderId)
        val group = cachedChat?.isGroupChat ?: false
        val cachedTitle = cachedChat?.title?.takeIf(String::isNotBlank)
        val initialTitle = cachedTitle ?: fallbackName
        val currentBeforeInitial = GlobalParam(context)
        if (CacheScope.from(currentBeforeInitial)?.id != scopeId || !currentBeforeInitial.notificationsEnabled ||
            chatId in currentBeforeInitial.mutedChatIds || OpenChatManager.isOpen(chatId)) return
        val initial = RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = msg.id,
            senderId = senderId,
            senderName = fallbackName,
            chatTitle = initialTitle,
            isGroupChat = group,
            timestampMillis = msg.sentAt.seconds * 1_000L + msg.sentAt.nanos / 1_000_000L,
            text = msg.content?.text.orEmpty(),
            attachmentKind = notificationAttachmentKind(msg.content?.attachmentsList?.firstOrNull()?.type),
            attachmentCount = msg.content?.attachmentsCount?.coerceAtLeast(1) ?: 1,
            senderNameIsFallback = true,
            chatTitleIsFallback = cachedTitle == null,
            contentIsComplete = true,
        )
        NotificationHelper.showRegularMessageNotification(context, initial)

        notificationEnrichmentScope.launch {
            try {
                notificationEnrichmentPermits.withPermit {
                    val current = GlobalParam(context)
                    if (CacheScope.from(current)?.id != scopeId || !current.notificationsEnabled ||
                        chatId in current.mutedChatIds || OpenChatManager.isOpen(chatId)) return@withPermit
                    withTimeout(NOTIFICATION_ENRICHMENT_TIMEOUT_MILLIS) {
                        enrichMessageNotification(scopeId, chatId, senderId, cachedChat, initial)
                    }
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "Unable to enrich realtime message notification", e)
            } finally {
                notificationEnrichmentFinished?.invoke(scopeId, chatId)
            }
        }
    }

    private suspend fun enrichMessageNotification(
        scopeId: String,
        chatId: String,
        senderId: Long,
        cachedChat: com.barkfluff.client.domain.model.ChatSummary?,
        initial: RegularNotificationInput,
    ) {
        val enrichedChat = if (cachedChat == null) chatInfoLookup(chatId).getOrNull() else null
        val userKey = scopeId to senderId
        val userInfo = userInfoCache[userKey] ?: userProfileGateway.user(senderId).getOrNull()?.let { user ->
            val displayName = "${user.firstName} ${user.lastName}".trim().ifEmpty { user.username }
            CachedUserInfo(displayName, user.profilePicturePreviewFileId, user.profilePictureFileId,
                user.profilePicturePreviewUrl, user.profilePictureUrl).also { userInfoCache[userKey] = it }
        }
        val groupChat = cachedChat?.isGroupChat ?: enrichedChat?.isGroupChat ?: false
        val title = if (groupChat) cachedChat?.title?.takeIf(String::isNotBlank)
                ?: enrichedChat?.title?.takeIf(String::isNotBlank)
                ?: context.getString(com.barkfluff.client.R.string.notification_group_chat)
            else userInfo?.displayName ?: initial.chatTitle
        val hasChatTitle = cachedChat?.title?.isNotBlank() == true || enrichedChat?.title?.isNotBlank() == true ||
            (!groupChat && userInfo?.displayName?.isNotBlank() == true)
        val enrichedInput = initial.copy(senderName = userInfo?.displayName ?: initial.senderName,
            chatTitle = title.ifBlank { initial.chatTitle }, isGroupChat = groupChat,
            senderNameIsFallback = userInfo?.displayName.isNullOrBlank(),
            chatTitleIsFallback = !hasChatTitle)
        val currentAfterMetadata = GlobalParam(context)
        if (CacheScope.from(currentAfterMetadata)?.id != scopeId || !currentAfterMetadata.notificationsEnabled ||
            chatId in currentAfterMetadata.mutedChatIds || OpenChatManager.isOpen(chatId)) return
        NotificationHelper.showRegularMessageNotification(
            context,
            enrichedInput,
        )

        val senderBitmap = userInfo?.let { info ->
            val avatarId = info.avatarFileId.ifBlank { info.avatarFullFileId }
            (if (avatarId.isNotBlank()) loadBitmapByFileId(avatarId) else null)
                ?: loadBitmapByUrl(info.avatarPreviewUrl.ifBlank { info.avatarFullUrl })
        }
        val cachedAvatarId = cachedChat?.let { it.picturePreviewFileId.ifBlank { it.pictureFileId } }
        val chatAvatarId = if (groupChat) cachedAvatarId ?: enrichedChat?.pictureFileId.orEmpty()
            else cachedAvatarId ?: userInfo?.avatarFileId?.ifBlank { userInfo.avatarFullFileId }.orEmpty()
        val chatBitmap = if (chatAvatarId.isNotBlank()) loadBitmapByFileId(chatAvatarId)
            else senderBitmap.takeUnless { groupChat }
        val currentAfterAvatar = GlobalParam(context)
        if (CacheScope.from(currentAfterAvatar)?.id != scopeId || !currentAfterAvatar.notificationsEnabled ||
            chatId in currentAfterAvatar.mutedChatIds || OpenChatManager.isOpen(chatId)) return
        NotificationHelper.showRegularMessageNotification(
            context,
            enrichedInput,
            senderAvatarBitmap = senderBitmap,
            chatAvatarBitmap = chatBitmap,
        )
    }

    private fun notificationAttachmentKind(type: barkfluff.shared.Shared.MessageAttachmentType?): String = when (type) {
        barkfluff.shared.Shared.MessageAttachmentType.IMAGE -> "image"
        barkfluff.shared.Shared.MessageAttachmentType.VIDEO -> "video"
        barkfluff.shared.Shared.MessageAttachmentType.GIF -> "gif"
        barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT -> "document"
        barkfluff.shared.Shared.MessageAttachmentType.AUDIO -> "audio"
        barkfluff.shared.Shared.MessageAttachmentType.VOICE -> "voice"
        barkfluff.shared.Shared.MessageAttachmentType.STICKER -> "sticker"
        else -> ""
    }

    override suspend fun showPrivateMessageNotification(chatId: String, messageId: Long, senderUserId: Long, expectedScopeId: String?) {
        val scopeId = expectedScopeId ?: CacheScope.from(globalParam)?.id ?: return
        if (CacheScope.from(globalParam)?.id != scopeId) return
        if (!globalParam.notificationsEnabled || senderUserId <= 0 || senderUserId == globalParam.userId ||
            chatId.isBlank() || messageId <= 0 || chatId in globalParam.mutedChatIds || OpenChatManager.isOpen(chatId)) return
        NotificationHelper.showEncryptedMessageNotification(
            context, NotificationHelper.KIND_PRIVATE, chatId, messageId.toString(), expectedScopeId = scopeId
        )
    }

    override suspend fun showSecretMessageNotification(messageId: String, senderUserId: Long, senderDeviceId: String, expectedScopeId: String?) {
        val scopeId = expectedScopeId ?: CacheScope.from(globalParam)?.id ?: return
        if (CacheScope.from(globalParam)?.id != scopeId) return
        if (!globalParam.secretChatsEnabled || !globalParam.notificationsEnabled || senderUserId <= 0) return
        val eventId = runCatching { UUID.fromString(messageId).toString() }.getOrNull() ?: return
        val deviceId = runCatching { UUID.fromString(senderDeviceId).toString() }.getOrNull() ?: return
        if (senderUserId == globalParam.userId && deviceId.equals(globalParam.deviceId, ignoreCase = true)) return
        val chat = try {
            withContext(Dispatchers.IO) { secretChatRepository.findByPeer(senderUserId, deviceId) }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Secret notification chat lookup failed", e)
            null
        }
        if (chat != null && (chat.id in globalParam.mutedChatIds || OpenChatManager.isOpen(chat.id))) return
        val threadId = "$senderUserId:$deviceId"
        NotificationHelper.showEncryptedMessageNotification(
            context, NotificationHelper.KIND_SECRET, threadId, eventId, chatId = chat?.id ?: threadId,
            expectedScopeId = scopeId
        )
    }

    private suspend fun loadBitmapByFileId(fileId: String): Bitmap? {
        return try {
            // Сначала проверяем URL-кэш (заполняется AvatarLoader при загрузке списка чатов)
            var url = AvatarLoader.urlCache[fileId]
            if (url == null) {
                val urlResult = fileMediaGateway.downloadUrl(fileId)
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
        } catch (e: CancellationException) {
            throw e
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
        } catch (e: CancellationException) {
            throw e
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
        private const val NOTIFICATION_ENRICHMENT_TIMEOUT_MILLIS = 8_000L
    }
}
