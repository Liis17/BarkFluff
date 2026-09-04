package com.barkfluff.client.domain.model

import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.grpc.GrpcApiTransport
import barkfluff.messages.MessagesApiOuterClass

/** Stable domain names; gRPC generated messages stay below the gateway boundary. */
data class AuthSession(
    val accessToken: String,
    val accessTokenExpiration: Long,
    val refreshToken: String,
    val refreshTokenExpiration: Long,
)

sealed interface AuthenticationResult {
    data class Success(val session: AuthSession) : AuthenticationResult
    data object OtpRequired : AuthenticationResult
    data class Error(val message: String) : AuthenticationResult
}

data class ServerInfo(
    val name: String,
    val description: String,
    val color: ClientColors,
    val identityEndpoint: String,
    val usersEndpoint: String,
    val filesEndpoint: String,
    val messagesEndpoint: String,
    val updatesEndpoint: String,
    val onlinerEndpoint: String,
    val fastAuthEndpoint: String,
    val callsEndpoint: String,
    val livekitUrl: String,
    val filesMediaEndpoint: String = "",
)

data class UserProfile(
    val userId: Long,
    val username: String,
    val firstName: String,
    val lastName: String,
    val bio: String,
    val profilePictureUrl: String,
    val profilePicturePreviewUrl: String,
    val profilePictureFileId: String = "",
    val profilePicturePreviewFileId: String = "",
    val profilePosterFileId: String = "",
    val registrationDate: Long,
)

data class ChatSummary(
    val id: String,
    val title: String,
    val picture: String,
    val pictureFileId: String = "",
    val picturePreviewFileId: String = "",
    val isGroupChat: Boolean,
    val lastMessage: LastMessageSummary?,
    val memberIds: List<Long>,
    val countUnread: Long,
    val firstUnreadMessageId: Long,
    val chatType: barkfluff.shared.Shared.ChatType = barkfluff.shared.Shared.ChatType.CHAT_TYPE_REGULAR,
    val lastActivityAt: Long = lastMessage?.sentAt ?: 0L,
    val privateInviteState: barkfluff.shared.Shared.PrivateChatInviteState =
        barkfluff.shared.Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_ACCEPTED,
    val privateInviterUserId: Long = 0L,
    val hasDraft: Boolean = false,
    /** E2E handshake material used only by the private-chat controller. */
    val kdfSalt: ByteArray = byteArrayOf(),
    val passphraseVerifier: ByteArray = byteArrayOf(),
)

data class LastMessageSummary(
    val id: Long,
    val senderId: Long,
    val text: String,
    val sentAt: Long,
    val readBy: List<Long>,
)

data class ChatPage(val chats: List<ChatSummary>, val totalCount: Int)

data class ChatInfo(
    val chatId: String,
    val title: String,
    val pictureFileId: String,
    val isGroupChat: Boolean,
    val lastMessageId: Long,
    val firstUnreadMessageId: Long,
    val countUnread: Long,
    val memberIds: List<Long>,
    val muted: Boolean = false,
)

data class ChatMember(val userId: Long, val firstName: String, val lastName: String)

data class UserPresence(
    val userId: Long,
    val isOnline: Boolean,
    val lastSeenEpochMillis: Long,
)

data class ChatFolder(
    val folderId: String,
    val folderName: String,
    val folderIcon: String,
    val chatIds: List<String>,
    val sortOrder: Int,
)

data class MediaUpload(
    val url: String,
    val fileId: String,
)

data class PinnedMessagePage(
    val messages: List<barkfluff.shared.Shared.PinnedMessageInfo>,
    val totalCount: Int,
)

/** Mapping helpers remain in the production adapter, not in UI code. */
internal fun GrpcApiTransport.ServerInfo.toDomain() = ServerInfo(
    name = name,
    description = description,
    color = color,
    identityEndpoint = identityEndpoint,
    usersEndpoint = usersEndpoint,
    filesEndpoint = filesEndpoint,
    messagesEndpoint = messagesEndpoint,
    updatesEndpoint = updatesEndpoint,
    onlinerEndpoint = onlinerEndpoint,
    fastAuthEndpoint = fastAuthEndpoint,
    callsEndpoint = callsEndpoint,
    livekitUrl = livekitUrl,
    filesMediaEndpoint = filesMediaEndpoint,
)

internal fun GrpcApiTransport.UserData.toDomain() = UserProfile(
    userId = userId,
    username = username,
    firstName = firstName,
    lastName = lastName,
    bio = bio,
    profilePictureUrl = profilePictureUrl,
    profilePicturePreviewUrl = profilePicturePreviewUrl,
    profilePictureFileId = profilePictureFileId,
    profilePicturePreviewFileId = profilePicturePreviewFileId,
    profilePosterFileId = profilePosterFileId,
    registrationDate = registrationDate,
)

internal fun GrpcApiTransport.ChatData.toDomain() = ChatSummary(
    id = id,
    title = title,
    picture = picture,
    pictureFileId = pictureFileId,
    picturePreviewFileId = picturePreviewFileId,
    isGroupChat = isGroupChat,
    lastMessage = lastMessage?.let {
        LastMessageSummary(it.id, it.senderId, it.text, it.sentAt, it.readBy)
    },
    memberIds = memberIds,
    countUnread = countUnread,
    firstUnreadMessageId = firstUnreadMessageId,
    chatType = chatType,
    lastActivityAt = lastActivityAt,
    privateInviteState = privateInviteState,
    privateInviterUserId = privateInviterUserId,
    hasDraft = hasDraft,
)

internal fun GrpcApiTransport.ChatFolder.toDomain() = ChatFolder(
    folderId = folderId,
    folderName = folderName,
    folderIcon = folderIcon,
    chatIds = chatIds,
    sortOrder = sortOrder,
)

internal fun MessagesApiOuterClass.Chat.toDomain() = ChatSummary(
    id = id,
    title = title,
    picture = picture,
    pictureFileId = "",
    picturePreviewFileId = "",
    isGroupChat = isGroupChat,
    lastMessage = if (hasLastMessage()) {
        lastMessage.let { message ->
            LastMessageSummary(
                id = message.id,
                senderId = message.senderId,
                text = message.content?.text.orEmpty(),
                sentAt = message.sentAt.seconds * 1000,
                readBy = message.readByList,
            )
        }
    } else null,
    memberIds = membersList.map { it.userId },
    countUnread = countUnread,
    firstUnreadMessageId = firstUnreadMessageId,
    chatType = chatType,
    lastActivityAt = lastActivityAt.seconds * 1000,
    privateInviteState = privateInviteState,
    privateInviterUserId = privateInviterUserId,
    hasDraft = hasDraft,
    kdfSalt = kdfSalt.toByteArray(),
    passphraseVerifier = passphraseVerifier.toByteArray(),
)

internal fun GrpcApiTransport.AuthResult.toDomain() = when (this) {
    is GrpcApiTransport.AuthResult.Success -> AuthenticationResult.Success(
        AuthSession(accessToken, accessTokenExpiration, refreshToken, refreshTokenExpiration)
    )
    GrpcApiTransport.AuthResult.OtpRequired -> AuthenticationResult.OtpRequired
    is GrpcApiTransport.AuthResult.Error -> AuthenticationResult.Error(message)
}
