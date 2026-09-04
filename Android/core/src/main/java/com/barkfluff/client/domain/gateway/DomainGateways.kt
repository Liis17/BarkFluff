package com.barkfluff.client.domain.gateway

import android.content.Context
import barkfluff.calls.CallsApiOuterClass
import barkfluff.fast.auth.FastAuthApiOuterClass
import barkfluff.onliner.OnlinerApiOuterClass
import barkfluff.shared.Shared
import barkfluff.updates.UpdatesApiOuterClass
import com.barkfluff.client.domain.model.AuthenticationResult
import com.barkfluff.client.domain.model.AuthResult
import com.barkfluff.client.domain.model.ChatFolder
import com.barkfluff.client.domain.model.ChatInfo
import com.barkfluff.client.domain.model.ChatMember
import com.barkfluff.client.domain.model.ChatPage
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.MediaUpload
import com.barkfluff.client.domain.model.PinnedMessagePage
import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.domain.model.ConfirmAccountResult
import com.barkfluff.client.domain.model.ConfirmResetPasswordResult
import com.barkfluff.client.domain.model.InvalidOldPasswordException
import com.barkfluff.client.domain.model.OtpSetupResult
import com.barkfluff.client.domain.model.OtpStatus
import com.barkfluff.client.domain.model.PrivateChatCreateResult
import com.barkfluff.client.domain.model.SecretInviteSent
import com.barkfluff.client.domain.model.SecretMessageSent
import com.barkfluff.client.domain.model.SessionData
import com.barkfluff.client.domain.model.StorageInfo
import com.barkfluff.client.domain.model.SyncedChatBackgroundSettings
import com.barkfluff.client.grpc.TokenCoordinator
import java.io.File
import kotlinx.coroutines.flow.Flow

/** Remote portion of the durable composer journal. Attachments never cross this boundary. */
interface ChatDraftGateway {
    suspend fun get(chatId: String): Result<com.barkfluff.client.domain.model.ChatDraftData?>
    suspend fun upsert(chatId: String, text: String, replyToMessageId: Long): Result<com.barkfluff.client.domain.model.ChatDraftData>
    suspend fun delete(chatId: String, expectedRevision: String): Result<Boolean>
}

interface ServerDiscoveryGateway {
    suspend fun listServers(): Result<List<com.barkfluff.client.data.ServerDataElement>>
    suspend fun serverInfo(): Result<ServerInfo>
    fun createNavigator(address: String = "https://navigator.barkfluff.com"): Result<Unit>
    fun createBeacon(address: String): Result<Unit>
    fun normalizeEndpoint(address: String): String
}

interface AuthGateway {
    suspend fun authenticate(
        email: String?,
        username: String?,
        password: String,
        otpCode: String?,
    ): AuthenticationResult

    suspend fun ensureValid(forceRefresh: Boolean = false): Boolean
    suspend fun refresh(refreshToken: String, currentRefreshTokenExpiration: Long = 0L): Result<com.barkfluff.client.grpc.TokenRefreshResult>
    fun createIdentity(address: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit>
}

interface AccountSecurityGateway {
    suspend fun register(firstName: String, lastName: String, email: String, login: String): Result<String>
    suspend fun resetPassword(email: String?, username: String?): Result<String>
    suspend fun confirmAccount(codeId: String, verificationCode: String): Result<ConfirmAccountResult>
    suspend fun confirmResetPassword(resetId: String, code: String): Result<ConfirmResetPasswordResult>
    suspend fun setPasswordAfterReset(newPassword: String): Result<Unit>
}

interface UserProfileGateway {
    suspend fun currentUser(): Result<UserProfile>
    suspend fun user(userId: Long): Result<UserProfile>
    suspend fun setFirebaseToken(token: String): Result<Unit>
    suspend fun changeName(firstName: String, lastName: String): Result<Unit>
    suspend fun changeBio(bio: String): Result<Unit>
    suspend fun changeUsername(username: String): Result<Unit>
    suspend fun setProfilePicture(fileId: String): Result<Unit>
    suspend fun uploadAvatar(bytes: ByteArray): Result<String>
    suspend fun getProfilePoster(): Result<String>
    suspend fun setProfilePoster(fileId: String): Result<Unit>
    suspend fun uploadProfilePoster(bytes: ByteArray): Result<String>
    suspend fun getActiveSessions(context: Context): Result<List<SessionData>>
    suspend fun renameDevice(deviceId: String, customName: String): Result<Unit>
    suspend fun removeActiveSession(deviceId: String): Result<Unit>
    suspend fun password(password: String): Result<Unit>
    suspend fun otpSetup(): Result<OtpSetupResult>
    suspend fun confirmOtpSetup(code: String): Result<Unit>
    suspend fun otpStatus(): Result<OtpStatus>
    suspend fun enableOtpEmail(): Result<Unit>
    suspend fun disableOtp(type: barkfluff.identity.IdentityApiOuterClass.OtpTypeId, code: String): Result<Unit>
    suspend fun changePassword(oldPassword: String, newPassword: String): Result<Unit>
    suspend fun storageInfo(): Result<StorageInfo>
}

interface UserSettingsGateway {
    suspend fun notificationsEnabled(): Result<Boolean>
    suspend fun setNotificationsEnabled(enabled: Boolean): Result<Unit>
    suspend fun privacySettings(): Result<barkfluff.users.UsersApiOuterClass.PrivacySettings>
    suspend fun updatePrivacySettings(settings: barkfluff.users.UsersApiOuterClass.PrivacySettings): Result<Unit>
    suspend fun setChatMuted(chatId: String, muted: Boolean, mutedUntilEpochSeconds: Long? = null): Result<Unit>
    suspend fun mutedChats(): Result<Set<String>>
    suspend fun personalization(): Result<List<String>>
    suspend fun updatePersonalizationBackgrounds(fileIds: List<String>): Result<Unit>
    suspend fun syncedChatBackgrounds(): Result<SyncedChatBackgroundSettings>
    suspend fun setGlobalChatBackground(fileId: String): Result<Unit>
    suspend fun setChatBackground(chatId: String, fileId: String): Result<Unit>
}

interface UserDirectoryGateway {
    suspend fun search(query: String, offset: Int = 0, size: Int = 50): Result<List<UserProfile>>
    suspend fun checkEmail(email: String): Result<Boolean>
    suspend fun checkUsername(username: String): Result<Boolean>
    suspend fun personChatId(userId: Long): Result<String>
    suspend fun peerDevices(userId: Long): Result<List<barkfluff.users.UsersApiOuterClass.PeerDeviceInfo>>
}

interface ChatDirectoryGateway {
    suspend fun chats(offset: Int = 0, size: Int = 50): Result<ChatPage>
    suspend fun chat(chatId: String): Result<ChatSummary>
    suspend fun members(chatId: String): Result<List<ChatMember>>
    suspend fun addMember(chatId: String, userId: Long): Result<Unit>
    suspend fun removeMember(chatId: String, userId: Long): Result<Unit>
    suspend fun createGroup(userIds: List<Long>, title: String, pictureFileId: String? = null): Result<ChatSummary>
    suspend fun updateGroup(chatId: String, title: String? = null, pictureFileId: String? = null): Result<ChatSummary>
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
    suspend fun chatInfo(chatId: String): Result<ChatInfo>
    suspend fun pinnedMessages(chatId: String, offset: Int = 0, size: Int = 50): Result<PinnedMessagePage>
    suspend fun pinMessage(chatId: String, messageId: Long): Result<Shared.PinnedMessageInfo>
    suspend fun unpinMessage(chatId: String, messageId: Long): Result<Unit>
    suspend fun unpinAllMessages(chatId: String): Result<Int>
}

interface ChatFolderGateway {
    suspend fun folders(): Result<List<ChatFolder>>
    suspend fun create(name: String, icon: String): Result<ChatFolder>
    suspend fun update(folderId: String, name: String, icon: String, chatIds: List<String>): Result<ChatFolder>
    suspend fun delete(folderId: String): Result<Unit>
    suspend fun addChat(folderId: String, chatId: String): Result<ChatFolder>
    suspend fun removeChat(folderId: String, chatId: String): Result<ChatFolder>
    suspend fun reorder(orders: List<Pair<String, Int>>): Result<Unit>
}

interface FileMediaGateway {
    suspend fun downloadUrl(fileId: String): Result<String>
    suspend fun uploadUrl(fileType: barkfluff.files.FilesApiOuterClass.UploadFileType): Result<MediaUpload>
    suspend fun upload(bytes: ByteArray, fileType: barkfluff.files.FilesApiOuterClass.UploadFileType): Result<String>
    suspend fun upload(file: File, fileType: barkfluff.files.FilesApiOuterClass.UploadFileType): Result<String>
    suspend fun download(fileId: String, onProgress: (Int) -> Unit = {}): File?
}

interface StickerGateway {
    suspend fun packs(offset: Int = 0, size: Int = 50): Result<List<barkfluff.files.FilesApiOuterClass.StickerPackInfo>>
    suspend fun stickerPack(packId: String): Result<List<barkfluff.files.FilesApiOuterClass.StickerInfo>>
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
    suspend fun listHistory(
        filter: CallsApiOuterClass.CallHistoryFilter,
        limit: Int = 50,
    ): Result<CallsApiOuterClass.ListCallHistoryResponse>

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
