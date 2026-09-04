package com.barkfluff.client.domain.gateway

import barkfluff.calls.CallsApiOuterClass
import barkfluff.fast.auth.FastAuthApiOuterClass
import barkfluff.onliner.OnlinerApiOuterClass
import barkfluff.shared.Shared
import barkfluff.updates.UpdatesApiOuterClass
import com.barkfluff.client.domain.model.AuthenticationResult
import com.barkfluff.client.domain.model.ChatFolder
import com.barkfluff.client.domain.model.ChatMember
import com.barkfluff.client.domain.model.ChatPage
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.MediaUpload
import com.barkfluff.client.domain.model.PinnedMessagePage
import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.grpc.TokenCoordinator
import java.io.File
import kotlinx.coroutines.flow.Flow

interface ServerDiscoveryGateway {
    suspend fun listServers(): Result<List<com.barkfluff.client.data.ServerDataElement>>
    suspend fun serverInfo(): Result<ServerInfo>
}

interface AuthGateway {
    suspend fun authenticate(
        email: String?,
        username: String?,
        password: String,
        otpCode: String?,
    ): AuthenticationResult

    suspend fun ensureValid(forceRefresh: Boolean = false): Boolean
}

interface AccountSecurityGateway {
    suspend fun register(firstName: String, lastName: String, email: String, login: String): Result<String>
    suspend fun resetPassword(email: String?, username: String?): Result<String>
    suspend fun confirmResetPassword(resetId: String, code: String): Result<Unit>
}

interface UserProfileGateway {
    suspend fun currentUser(): Result<UserProfile>
    suspend fun user(userId: Long): Result<UserProfile>
    suspend fun setFirebaseToken(token: String): Result<Unit>
}

interface UserSettingsGateway {
    suspend fun notificationsEnabled(): Result<Boolean>
    suspend fun setNotificationsEnabled(enabled: Boolean): Result<Unit>
    suspend fun privacySettings(): Result<barkfluff.users.UsersApiOuterClass.PrivacySettings>
}

interface UserDirectoryGateway {
    suspend fun search(query: String, offset: Int = 0, size: Int = 50): Result<List<UserProfile>>
    suspend fun checkEmail(email: String): Result<Boolean>
    suspend fun checkUsername(username: String): Result<Boolean>
}

interface ChatDirectoryGateway {
    suspend fun chats(offset: Int = 0, size: Int = 50): Result<ChatPage>
    suspend fun chat(chatId: String): Result<ChatSummary>
    suspend fun members(chatId: String): Result<List<ChatMember>>
}

interface MessageGateway {
    suspend fun loadMessages(
        chatId: String,
        fromMessageId: Long = 0L,
        offsetBefore: Int = 0,
        offsetAfter: Int = 0,
        count: Int = 30,
    ): Result<List<Shared.Message>>

    suspend fun sendMessage(
        chatId: String,
        text: String,
        fileIds: List<String> = emptyList(),
        replyToMessageId: Long = 0L,
        forwardedMessageIds: List<Long> = emptyList(),
        clientOperationId: String? = null,
    ): Result<Shared.Message>

    suspend fun editMessage(messageId: Long, text: String, fileIds: List<String> = emptyList()): Result<Shared.Message>
    suspend fun deleteMessage(messageId: Long): Result<Unit>
    suspend fun markAsRead(messageIds: List<Long>): Result<Unit>
    suspend fun chatInfo(chatId: String): Result<com.barkfluff.client.repository.ChatRepository.ChatInfo>
    suspend fun pinnedMessages(chatId: String, offset: Int = 0, size: Int = 50): Result<PinnedMessagePage>
    suspend fun pinMessage(chatId: String, messageId: Long): Result<Shared.PinnedMessageInfo>
    suspend fun unpinMessage(chatId: String, messageId: Long): Result<Unit>
    suspend fun unpinAllMessages(chatId: String): Result<Int>
}

interface ChatFolderGateway {
    suspend fun folders(): Result<List<ChatFolder>>
}

interface FileMediaGateway {
    suspend fun downloadUrl(fileId: String): Result<String>
    suspend fun uploadUrl(fileType: barkfluff.files.FilesApiOuterClass.UploadFileType): Result<MediaUpload>
    suspend fun upload(file: File, fileType: barkfluff.files.FilesApiOuterClass.UploadFileType): Result<String>
    suspend fun download(fileId: String, onProgress: (Int) -> Unit = {}): File?
}

interface StickerGateway {
    suspend fun stickerPack(packId: String): Result<Any>
}

interface RealtimeGateway {
    val newMessages: Flow<UpdatesApiOuterClass.NewMessageEvent>
    val messagesRead: Flow<UpdatesApiOuterClass.MessageReadEvent>
    val messageEdited: Flow<UpdatesApiOuterClass.MessageEditedEvent>
    val messageDeleted: Flow<UpdatesApiOuterClass.MessageDeletedEvent>
    val messagePinned: Flow<UpdatesApiOuterClass.MessagePinnedEvent>
    val messageUnpinned: Flow<UpdatesApiOuterClass.MessageUnpinnedEvent>
    val allMessagesUnpinned: Flow<UpdatesApiOuterClass.AllMessagesUnpinnedEvent>
    val onlineStatuses: Flow<OnlinerApiOuterClass.UserOnlineStatus>
    val typingEvents: Flow<OnlinerApiOuterClass.TypingEvent>
    fun resume()
    fun pause()
    fun shutdown()
    fun changeOnlineSubscription(userIds: List<Long>)
    fun changeTypingSubscription(chatIds: List<String>)
}

interface CallGateway {
    suspend fun initiateDirect(
        calleeUserId: Long,
        mediaType: CallsApiOuterClass.CallMediaType,
    ): Result<CallsApiOuterClass.InitiateCallResponse>

    suspend fun initiateGroup(
        chatId: String,
        mediaType: CallsApiOuterClass.CallMediaType,
    ): Result<CallsApiOuterClass.InitiateCallResponse>
}

interface FastAuthGateway {
    suspend fun scan(fastAuthId: String): Result<FastAuthApiOuterClass.ScanFastAuthResponse>
    suspend fun accept(fastAuthId: String, confirmationCode: String): Result<Unit>
    suspend fun reject(fastAuthId: String, confirmationCode: String): Result<Unit>
}

interface PrivateChatGateway {
    suspend fun send(chatId: String, plaintext: String): Result<Unit>
}

interface SecretChatGateway {
    suspend fun send(chatId: String, plaintext: String): Result<Unit>
}

interface PrekeyGateway {
    suspend fun replenish(): Result<Unit>
}

/** Common fake-friendly token dependency for workers and gateways. */
interface TokenAwareGateway {
    val tokenCoordinator: TokenCoordinator
}
