package com.barkfluff.client.domain.gateway

import android.content.Context
import barkfluff.files.FilesApiOuterClass
import barkfluff.shared.Shared
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.data.ServerDataElement
import com.barkfluff.client.domain.model.AuthenticationResult
import com.barkfluff.client.domain.model.ChatFolder
import com.barkfluff.client.domain.model.ChatMember
import com.barkfluff.client.domain.model.ChatPage
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.MediaUpload
import com.barkfluff.client.domain.model.PinnedMessagePage
import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.domain.model.toDomain
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.calls.CallRepository
import java.io.File
import kotlinx.coroutines.flow.Flow

class GrpcServerDiscoveryGateway(private val grpc: GrpcManager) : ServerDiscoveryGateway {
    override suspend fun listServers(): Result<List<ServerDataElement>> = grpc.getServerList()
    override suspend fun serverInfo(): Result<ServerInfo> = grpc.getServerInfo().map { it.toDomain() }
}

class GrpcAuthGateway(
    private val grpc: GrpcManager,
    private val context: Context,
) : AuthGateway {
    override suspend fun authenticate(
        email: String?,
        username: String?,
        password: String,
        otpCode: String?,
    ): AuthenticationResult = grpc.auth(email, username, password, otpCode, context).toDomain()

    override suspend fun ensureValid(forceRefresh: Boolean): Boolean =
        grpc.tokenCoordinator().ensureValid(forceRefresh)
}

class GrpcAccountSecurityGateway(private val grpc: GrpcManager) : AccountSecurityGateway {
    override suspend fun register(firstName: String, lastName: String, email: String, login: String): Result<String> =
        grpc.createAccount(firstName, lastName, email, login)

    override suspend fun resetPassword(email: String?, username: String?): Result<String> =
        grpc.resetPassword(email, username)

    override suspend fun confirmResetPassword(resetId: String, code: String): Result<Unit> =
        grpc.confirmResetPassword(resetId, code).map { Unit }
}

class GrpcUserProfileGateway(private val grpc: GrpcManager) : UserProfileGateway {
    override suspend fun currentUser(): Result<UserProfile> = grpc.getCurrentUserData().map { it.toDomain() }
    override suspend fun user(userId: Long): Result<UserProfile> = grpc.getUserData(userId).map { it.toDomain() }
    override suspend fun setFirebaseToken(token: String): Result<Unit> = grpc.setFirebaseToken(token)
}

class GrpcUserSettingsGateway(private val grpc: GrpcManager) : UserSettingsGateway {
    override suspend fun notificationsEnabled(): Result<Boolean> = grpc.getNotificationsEnabled()
    override suspend fun setNotificationsEnabled(enabled: Boolean): Result<Unit> = grpc.setNotificationsEnabled(enabled)
    override suspend fun privacySettings(): Result<UsersApiOuterClass.PrivacySettings> = grpc.getPrivacySettings()
}

class GrpcUserDirectoryGateway(private val grpc: GrpcManager) : UserDirectoryGateway {
    override suspend fun search(query: String, offset: Int, size: Int): Result<List<UserProfile>> =
        grpc.searchUsers(query, offset, size).map { users -> users.map { it.toDomain() } }

    override suspend fun checkEmail(email: String): Result<Boolean> = grpc.checkEmail(email)
    override suspend fun checkUsername(username: String): Result<Boolean> = grpc.checkUsername(username)
}

class GrpcChatDirectoryGateway(private val grpc: GrpcManager) : ChatDirectoryGateway {
    override suspend fun chats(offset: Int, size: Int): Result<ChatPage> =
        grpc.getChatsPage(offset, size).map { page ->
            ChatPage(page.chats.map { it.toDomain() }, page.totalCount)
        }

    override suspend fun chat(chatId: String): Result<ChatSummary> = grpc.getChat(chatId).map { chat -> chat.toDomain() }

    override suspend fun members(chatId: String): Result<List<ChatMember>> =
        grpc.listChatMembers(chatId).map { members -> members.map { ChatMember(it.userId, it.firstName, it.lastName) } }
}

class GrpcMessageGateway(
    private val repository: ChatRepository,
    private val grpc: GrpcManager,
) : MessageGateway {
    override suspend fun loadMessages(
        chatId: String,
        fromMessageId: Long,
        offsetBefore: Int,
        offsetAfter: Int,
        count: Int,
    ): Result<List<Shared.Message>> = repository.loadMessages(chatId, fromMessageId, offsetBefore, offsetAfter, count)

    override suspend fun sendMessage(
        chatId: String,
        text: String,
        fileIds: List<String>,
        replyToMessageId: Long,
        forwardedMessageIds: List<Long>,
        clientOperationId: String?,
    ): Result<Shared.Message> = repository.sendMessage(
        chatId,
        text,
        fileIds,
        replyToMessageId,
        forwardedMessageIds,
        clientOperationId,
    )

    override suspend fun editMessage(messageId: Long, text: String, fileIds: List<String>): Result<Shared.Message> =
        repository.editMessage(messageId, text, fileIds)

    override suspend fun deleteMessage(messageId: Long): Result<Unit> = repository.deleteMessage(messageId)
    override suspend fun markAsRead(messageIds: List<Long>): Result<Unit> = repository.markAsRead(messageIds)
    override suspend fun chatInfo(chatId: String): Result<ChatRepository.ChatInfo> = repository.getChatInfo(chatId)

    override suspend fun pinnedMessages(chatId: String, offset: Int, size: Int): Result<PinnedMessagePage> =
        grpc.listPinnedMessages(chatId, offset, size).map { (messages, total) -> PinnedMessagePage(messages, total) }

    override suspend fun pinMessage(chatId: String, messageId: Long): Result<Shared.PinnedMessageInfo> =
        grpc.pinMessage(chatId, messageId)

    override suspend fun unpinMessage(chatId: String, messageId: Long): Result<Unit> =
        grpc.unpinMessage(chatId, messageId)

    override suspend fun unpinAllMessages(chatId: String): Result<Int> = grpc.unpinAllMessages(chatId)
}

class GrpcChatFolderGateway(private val grpc: GrpcManager) : ChatFolderGateway {
    override suspend fun folders(): Result<List<ChatFolder>> = grpc.getChatFolders().map { folders -> folders.map { it.toDomain() } }
}

class GrpcFileMediaGateway(private val repository: ChatRepository) : FileMediaGateway {
    override suspend fun downloadUrl(fileId: String): Result<String> = repository.getFileDownloadUrl(fileId)

    override suspend fun uploadUrl(fileType: FilesApiOuterClass.UploadFileType): Result<MediaUpload> =
        repository.getUploadUrl(fileType).map { MediaUpload(it.url, it.fileId) }

    override suspend fun upload(file: File, fileType: FilesApiOuterClass.UploadFileType): Result<String> =
        repository.uploadFile(file, fileType)

    override suspend fun download(fileId: String, onProgress: (Int) -> Unit): File? =
        repository.downloadFile(fileId, onProgress)
}

class GrpcStickerGateway(private val grpc: GrpcManager) : StickerGateway {
    override suspend fun stickerPack(packId: String): Result<Any> =
        grpc.getStickerPack(packId)?.let { Result.success(it) }
            ?: Result.failure(IllegalStateException("Sticker pack is unavailable"))
}

class GrpcRealtimeGateway(private val realtime: RealtimeService) : RealtimeGateway {
    override val newMessages: Flow<barkfluff.updates.UpdatesApiOuterClass.NewMessageEvent> = realtime.newMessages
    override val messagesRead: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageReadEvent> = realtime.messagesRead
    override val messageEdited: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageEditedEvent> = realtime.messageEdited
    override val messageDeleted: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageDeletedEvent> = realtime.messageDeleted
    override val messagePinned: Flow<barkfluff.updates.UpdatesApiOuterClass.MessagePinnedEvent> = realtime.messagePinned
    override val messageUnpinned: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageUnpinnedEvent> = realtime.messageUnpinned
    override val allMessagesUnpinned: Flow<barkfluff.updates.UpdatesApiOuterClass.AllMessagesUnpinnedEvent> = realtime.allMessagesUnpinned
    override val onlineStatuses: Flow<barkfluff.onliner.OnlinerApiOuterClass.UserOnlineStatus> = realtime.onlineStatuses
    override val typingEvents: Flow<barkfluff.onliner.OnlinerApiOuterClass.TypingEvent> = realtime.typingEvents

    override fun resume() = realtime.resume()
    override fun pause() = realtime.pause()
    override fun shutdown() = realtime.shutdown()
    override fun changeOnlineSubscription(userIds: List<Long>) = realtime.changeOnlineSubscription(userIds)
    override fun changeTypingSubscription(chatIds: List<String>) = realtime.changeTypingSubscription(chatIds)
}

class GrpcCallGateway(private val repository: CallRepository) : CallGateway {
    override suspend fun initiateDirect(
        calleeUserId: Long,
        mediaType: barkfluff.calls.CallsApiOuterClass.CallMediaType,
    ) = repository.initiateDirect(calleeUserId, mediaType)

    override suspend fun initiateGroup(
        chatId: String,
        mediaType: barkfluff.calls.CallsApiOuterClass.CallMediaType,
    ) = repository.initiateGroup(chatId, mediaType)
}

class GrpcFastAuthGateway(private val grpc: GrpcManager) : FastAuthGateway {
    override suspend fun scan(fastAuthId: String) = grpc.scanFastAuth(fastAuthId)
    override suspend fun accept(fastAuthId: String, confirmationCode: String) = grpc.acceptFastAuth(fastAuthId, confirmationCode)
    override suspend fun reject(fastAuthId: String, confirmationCode: String) = grpc.rejectFastAuth(fastAuthId, confirmationCode)
}
