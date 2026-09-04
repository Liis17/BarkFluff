package com.barkfluff.client.domain.gateway

import android.content.Context
import barkfluff.files.FilesApiOuterClass
import barkfluff.shared.Shared
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.data.ServerDataElement
import com.barkfluff.client.domain.model.*
import com.barkfluff.client.domain.model.toDomain
import com.barkfluff.client.grpc.GrpcTransportFacade
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.calls.CallRepository
import java.io.File
import kotlinx.coroutines.flow.Flow

class GrpcServerDiscoveryGateway(private val grpc: GrpcTransportFacade) : ServerDiscoveryGateway {
    override suspend fun listServers(): Result<List<ServerDataElement>> = grpc.getServerList()
    override suspend fun serverInfo(): Result<ServerInfo> = grpc.getServerInfo().map { it.toDomain() }
    override suspend fun probe(address: String): Result<ServerInfo> =
        grpc.createOnlyBeaconClient(address).fold(
            onSuccess = { grpc.getServerInfo().map { it.toDomain() } },
            onFailure = { Result.failure(it) },
        )
    override fun createNavigator(address: String): Result<Unit> = grpc.createNavigatorClient(address)
    override fun createBeacon(address: String): Result<Unit> = grpc.createOnlyBeaconClient(address)
    override fun normalizeEndpoint(address: String): String = grpc.normalizeEndpointAddress(address)
}

class GrpcChatDraftGateway(private val repository: ChatRepository) : ChatDraftGateway {
    override suspend fun get(chatId: String): Result<ChatDraftData?> =
        repository.getChatDraft(chatId).map { it?.let { draft ->
            ChatDraftData(draft.text, draft.replyToMessageId, draft.revision, draft.updatedAtMillis)
        } }

    override suspend fun upsert(chatId: String, text: String, replyToMessageId: Long): Result<ChatDraftData> =
        repository.upsertChatDraft(chatId, text, replyToMessageId).map { draft ->
            ChatDraftData(draft.text, draft.replyToMessageId, draft.revision, draft.updatedAtMillis)
        }

    override suspend fun delete(chatId: String, expectedRevision: String): Result<Boolean> =
        repository.deleteChatDraft(chatId, expectedRevision)
}

class GrpcAuthGateway(
    private val grpc: GrpcTransportFacade,
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

    override suspend fun refresh(
        refreshToken: String,
        currentRefreshTokenExpiration: Long,
    ): Result<com.barkfluff.client.grpc.TokenRefreshResult> =
        grpc.refreshAccessToken(refreshToken, currentRefreshTokenExpiration)

    override suspend fun logout(): Result<Unit> = grpc.logout()

    override fun createIdentity(address: String, context: Context?, includeDeviceInfo: Boolean): Result<Unit> =
        grpc.createIdentityClient(address, context, includeDeviceInfo)
}

class GrpcAccountSecurityGateway(private val grpc: GrpcTransportFacade) : AccountSecurityGateway {
    override suspend fun register(firstName: String, lastName: String, email: String, login: String): Result<String> =
        grpc.createAccount(firstName, lastName, email, login)

    override suspend fun resetPassword(email: String?, username: String?): Result<String> =
        grpc.resetPassword(email, username)

    override suspend fun confirmAccount(codeId: String, verificationCode: String): Result<ConfirmAccountResult> =
        grpc.confirmAccount(codeId, verificationCode).map {
            ConfirmAccountResult(it.refreshToken, it.refreshTokenExpiration)
        }

    override suspend fun confirmResetPassword(resetId: String, code: String): Result<ConfirmResetPasswordResult> =
        grpc.confirmResetPassword(resetId, code).map {
            ConfirmResetPasswordResult(
                accessToken = it.accessToken,
                accessTokenExpiration = it.accessTokenExpiration,
                refreshToken = it.refreshToken,
                refreshTokenExpiration = it.refreshTokenExpiration,
            )
        }

    override suspend fun setPasswordAfterReset(newPassword: String): Result<Unit> =
        grpc.setPasswordAfterReset(newPassword)
}

class GrpcUserProfileGateway(private val grpc: GrpcTransportFacade) : UserProfileGateway {
    override suspend fun currentUser(): Result<UserProfile> = grpc.getCurrentUserData().map { it.toDomain() }
    override suspend fun user(userId: Long): Result<UserProfile> = grpc.getUserData(userId).map { it.toDomain() }
    override suspend fun setFirebaseToken(token: String): Result<Unit> = grpc.setFirebaseToken(token)
    override suspend fun changeName(firstName: String, lastName: String): Result<Unit> = grpc.changeName(firstName, lastName)
    override suspend fun changeBio(bio: String): Result<Unit> = grpc.changeBio(bio)
    override suspend fun changeUsername(username: String): Result<Unit> = grpc.changeUsername(username)
    override suspend fun setProfilePicture(fileId: String): Result<Unit> = grpc.setProfilePicture(fileId)
    override suspend fun uploadAvatar(bytes: ByteArray): Result<String> = grpc.uploadUserAvatar(bytes)
    override suspend fun getProfilePoster(): Result<String> = grpc.getProfilePoster()
    override suspend fun setProfilePoster(fileId: String): Result<Unit> = grpc.setProfilePoster(fileId)
    override suspend fun uploadProfilePoster(bytes: ByteArray): Result<String> = grpc.uploadProfilePoster(bytes)
    override suspend fun getActiveSessions(context: Context): Result<List<SessionData>> =
        grpc.getActiveSessions(context).map { sessions -> sessions.map(::toDomain) }
    override suspend fun renameDevice(deviceId: String, customName: String): Result<Unit> = grpc.renameDevice(deviceId, customName)
    override suspend fun removeActiveSession(deviceId: String): Result<Unit> = grpc.removeActiveSession(deviceId)
    override suspend fun password(password: String): Result<Unit> = grpc.setPassword(password)
    override suspend fun otpSetup(): Result<OtpSetupResult> = grpc.getOtpSetup().map { OtpSetupResult(it.qrBase64, it.justCode) }
    override suspend fun confirmOtpSetup(code: String): Result<Unit> = grpc.confirmOtpSetup(code)
    override suspend fun otpStatus(): Result<OtpStatus> = grpc.listOtpVerification().map { OtpStatus(it.authenticatorEnabled, it.emailEnabled) }
    override suspend fun enableOtpEmail(): Result<Unit> = grpc.enableOtpEmail()
    override suspend fun disableOtp(type: barkfluff.identity.IdentityApiOuterClass.OtpTypeId, code: String): Result<Unit> = grpc.disableOtpVerification(type, code)
    override suspend fun changePassword(oldPassword: String, newPassword: String): Result<Unit> = grpc.changePassword(oldPassword, newPassword)
    override suspend fun storageInfo(): Result<StorageInfo> = grpc.getUserStorageInfo().map(::toDomain)
}

class GrpcUserSettingsGateway(private val grpc: GrpcTransportFacade) : UserSettingsGateway {
    override suspend fun notificationsEnabled(): Result<Boolean> = grpc.getNotificationsEnabled()
    override suspend fun setNotificationsEnabled(enabled: Boolean): Result<Unit> = grpc.setNotificationsEnabled(enabled)
    override suspend fun privacySettings(): Result<UsersApiOuterClass.PrivacySettings> = grpc.getPrivacySettings()
    override suspend fun updatePrivacySettings(settings: UsersApiOuterClass.PrivacySettings): Result<Unit> = grpc.updatePrivacySettings(settings)
    override suspend fun setChatMuted(chatId: String, muted: Boolean, mutedUntilEpochSeconds: Long?): Result<Unit> =
        grpc.setChatMuted(chatId, muted, mutedUntilEpochSeconds)
    override suspend fun mutedChats(): Result<Set<String>> = grpc.getMutedChats()
    override suspend fun personalization(): Result<List<String>> = grpc.getPersonalization()
    override suspend fun updatePersonalizationBackgrounds(fileIds: List<String>): Result<Unit> = grpc.updatePersonalizationBackgrounds(fileIds)
    override suspend fun syncedChatBackgrounds(): Result<SyncedChatBackgroundSettings> = grpc.getUserSettings().map {
        SyncedChatBackgroundSettings(it.globalChatBackgroundFileId, it.chatBackgroundFileIds)
    }
    override suspend fun setGlobalChatBackground(fileId: String): Result<Unit> = grpc.setGlobalChatBackground(fileId)
    override suspend fun setChatBackground(chatId: String, fileId: String): Result<Unit> = grpc.setChatBackground(chatId, fileId)
}

class GrpcUserDirectoryGateway(private val grpc: GrpcTransportFacade) : UserDirectoryGateway {
    override suspend fun search(query: String, offset: Int, size: Int): Result<List<UserProfile>> =
        grpc.searchUsers(query, offset, size).map { users -> users.map { it.toDomain() } }

    override suspend fun checkEmail(email: String): Result<Boolean> = grpc.checkEmail(email)
    override suspend fun checkUsername(username: String): Result<Boolean> = grpc.checkUsername(username)
    override suspend fun personChatId(userId: Long): Result<String> = grpc.getPersonChatId(userId)
    override suspend fun peerDevices(userId: Long): Result<List<barkfluff.users.UsersApiOuterClass.PeerDeviceInfo>> = grpc.listPeerDevices(userId)
}

class GrpcPresenceGateway(
    private val registry: GrpcClientRegistry,
) : PresenceGateway {
    override suspend fun status(userIds: List<Long>): Result<List<UserPresence>> = runCatching {
        val client = registry.onlinerClient ?: error("Onliner client is unavailable")
        val request = barkfluff.onliner.OnlinerApiOuterClass.GetOnlineStatusRequest.newBuilder()
            .addAllUserIds(userIds.filter { it > 0L })
            .build()
        client.getOnlineStatus(request).usersStatusesList.map { value ->
            UserPresence(
                userId = value.userId,
                isOnline = value.status == barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE,
                lastSeenEpochMillis = value.lastSeen.seconds * 1000 + value.lastSeen.nanos / 1_000_000,
            )
        }
    }
}

class GrpcChatDirectoryGateway(private val grpc: GrpcTransportFacade) : ChatDirectoryGateway {
    override suspend fun chats(offset: Int, size: Int): Result<ChatPage> =
        grpc.getChatsPage(offset, size).map { page ->
            ChatPage(page.chats.map { it.toDomain() }, page.totalCount)
        }

    override suspend fun chat(chatId: String): Result<ChatSummary> = grpc.getChat(chatId).map { chat -> chat.toDomain() }

    override suspend fun members(chatId: String): Result<List<ChatMember>> =
        grpc.listChatMembers(chatId).map { members -> members.map { ChatMember(it.userId, it.firstName, it.lastName) } }

    override suspend fun addMember(chatId: String, userId: Long): Result<Unit> = grpc.addUser(chatId, userId)
    override suspend fun removeMember(chatId: String, userId: Long): Result<Unit> = grpc.kickUser(chatId, userId)
    override suspend fun createGroup(userIds: List<Long>, title: String, pictureFileId: String?): Result<ChatSummary> =
        grpc.createGroupChat(userIds, title, pictureFileId).map { it.toDomain() }
    override suspend fun updateGroup(chatId: String, title: String?, pictureFileId: String?): Result<ChatSummary> =
        grpc.updateGroupChat(chatId, title, pictureFileId).map { it.toDomain() }
}

class GrpcMessageGateway(
    private val repository: ChatRepository,
    private val grpc: GrpcTransportFacade,
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
    override suspend fun chatInfo(chatId: String): Result<ChatInfo> = repository.getChatInfo(chatId).map { value ->
        ChatInfo(
            chatId = value.chatId,
            title = value.title,
            pictureFileId = value.pictureFileId,
            isGroupChat = value.isGroupChat,
            lastMessageId = value.lastMessageId,
            firstUnreadMessageId = value.firstUnreadMessageId,
            countUnread = value.countUnread,
            memberIds = value.memberIds,
            muted = value.muted,
        )
    }

    override suspend fun pinnedMessages(chatId: String, offset: Int, size: Int): Result<PinnedMessagePage> =
        grpc.listPinnedMessages(chatId, offset, size).map { (messages, total) -> PinnedMessagePage(messages, total) }

    override suspend fun pinMessage(chatId: String, messageId: Long): Result<Shared.PinnedMessageInfo> {
        val result = grpc.pinMessage(chatId, messageId)
        val cause = result.exceptionOrNull()
        return if (cause is GrpcTransportFacade.PinErrorException) {
            Result.failure(PinErrorException(cause.errorCode, cause))
        } else {
            result
        }
    }

    override suspend fun unpinMessage(chatId: String, messageId: Long): Result<Unit> =
        grpc.unpinMessage(chatId, messageId)

    override suspend fun unpinAllMessages(chatId: String): Result<Int> = grpc.unpinAllMessages(chatId)

    override suspend fun attachments(
        chatId: String,
        type: Shared.MessageAttachmentType,
        pageSize: Int,
        fileNameQuery: String,
    ) = repository.getChatAttachments(chatId, type, pageSize, fileNameQuery)
}

class GrpcChatFolderGateway(private val grpc: GrpcTransportFacade) : ChatFolderGateway {
    override suspend fun folders(): Result<List<ChatFolder>> = grpc.getChatFolders().map { folders -> folders.map { it.toDomain() } }
    override suspend fun create(name: String, icon: String): Result<ChatFolder> = grpc.createChatFolder(name, icon).map { it.toDomain() }
    override suspend fun update(folderId: String, name: String, icon: String, chatIds: List<String>): Result<ChatFolder> =
        grpc.updateChatFolder(folderId, name, icon, chatIds).map { it.toDomain() }
    override suspend fun delete(folderId: String): Result<Unit> = grpc.deleteChatFolder(folderId)
    override suspend fun addChat(folderId: String, chatId: String): Result<ChatFolder> = grpc.addChatToFolder(folderId, chatId).map { it.toDomain() }
    override suspend fun removeChat(folderId: String, chatId: String): Result<ChatFolder> = grpc.removeChatFromFolder(folderId, chatId).map { it.toDomain() }
    override suspend fun reorder(orders: List<Pair<String, Int>>): Result<Unit> = grpc.reorderChatFolders(orders)
}

class GrpcFileMediaGateway(private val repository: ChatRepository) : FileMediaGateway {
    override suspend fun downloadUrl(fileId: String): Result<String> = repository.getFileDownloadUrl(fileId)

    override suspend fun uploadUrl(fileType: FilesApiOuterClass.UploadFileType): Result<MediaUpload> =
        repository.getUploadUrl(fileType).map { MediaUpload(it.url, it.fileId) }

    override suspend fun upload(bytes: ByteArray, fileType: FilesApiOuterClass.UploadFileType): Result<String> =
        repository.uploadFile(bytes, fileType)

    override suspend fun upload(file: File, fileType: FilesApiOuterClass.UploadFileType): Result<String> =
        repository.uploadFile(file, fileType)

    override suspend fun download(fileId: String, onProgress: (Int) -> Unit): File? =
        repository.downloadFile(fileId, onProgress)
}

class GrpcStickerGateway(private val grpc: GrpcTransportFacade) : StickerGateway {
    override suspend fun packs(offset: Int, size: Int): Result<List<FilesApiOuterClass.StickerPackInfo>> =
        grpc.listStickerPacks(offset, size)?.let { Result.success(it) }
            ?: Result.failure(IllegalStateException("Sticker packs are unavailable"))

    override suspend fun stickerPack(packId: String): Result<List<FilesApiOuterClass.StickerInfo>> =
        grpc.getStickerPack(packId)?.let { Result.success(it) }
            ?: Result.failure(IllegalStateException("Sticker pack is unavailable"))
}

private fun toDomain(value: GrpcTransportFacade.SessionData): SessionData = SessionData(
    id = value.id,
    createdAt = value.createdAt,
    expirationAt = value.expirationAt,
    deviceId = value.deviceId,
    originalName = value.originalName,
    customName = value.customName,
    appName = value.appName,
    os = value.os,
    location = value.location,
)

private fun toDomain(value: GrpcTransportFacade.StorageInfo): StorageInfo = StorageInfo(
    totalUsed = value.totalUsed,
    limit = value.limit,
    byType = value.byType,
)

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
    override suspend fun listHistory(
        filter: barkfluff.calls.CallsApiOuterClass.CallHistoryFilter,
        limit: Int,
    ) = repository.listCallHistory(filter, limit)

    override suspend fun initiateDirect(
        calleeUserId: Long,
        mediaType: barkfluff.calls.CallsApiOuterClass.CallMediaType,
    ) = repository.initiateDirect(calleeUserId, mediaType)

    override suspend fun initiateGroup(
        chatId: String,
        mediaType: barkfluff.calls.CallsApiOuterClass.CallMediaType,
    ) = repository.initiateGroup(chatId, mediaType)

    override suspend fun end(callId: String): Result<Unit> = repository.end(callId)
    override suspend fun reject(callId: String): Result<Unit> = repository.reject(callId)
}

class GrpcFastAuthGateway(private val grpc: GrpcTransportFacade) : FastAuthGateway {
    override suspend fun scan(fastAuthId: String) = grpc.scanFastAuth(fastAuthId)
    override suspend fun accept(fastAuthId: String, confirmationCode: String) = grpc.acceptFastAuth(fastAuthId, confirmationCode)
    override suspend fun reject(fastAuthId: String, confirmationCode: String) = grpc.rejectFastAuth(fastAuthId, confirmationCode)
}
