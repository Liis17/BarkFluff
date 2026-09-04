package com.barkfluff.client.domain.model

import barkfluff.messages.MessagesApiOuterClass
import barkfluff.users.UsersApiOuterClass

/**
 * Names retained while callers move from the nested GrpcManager DTOs to domain packages.
 * They are aliases, not a second representation, so cache and gateway migrations stay lossless.
 */
typealias UserData = UserProfile
typealias ChatData = ChatSummary
typealias LastMessageData = LastMessageSummary
typealias ChatMemberInfo = ChatMember
typealias UploadUrlResult = MediaUpload

data class ConfirmAccountResult(
    val refreshToken: String,
    val refreshTokenExpiration: Long,
)

data class ChatDraftData(
    val text: String,
    val replyToMessageId: Long,
    val revision: String,
    val updatedAtMillis: Long,
)

data class OtpSetupResult(
    val qrBase64: String,
    val justCode: String,
)

data class SessionData(
    val id: Long,
    val createdAt: Long,
    val expirationAt: Long,
    val deviceId: String,
    val originalName: String,
    val customName: String,
    val appName: String,
    val os: String,
    val location: String,
)

data class OtpStatus(
    val authenticatorEnabled: Boolean,
    val emailEnabled: Boolean,
)

data class StorageInfo(
    val totalUsed: Long,
    val limit: Long,
    val byType: Map<String, Long>,
)

data class ConfirmResetPasswordResult(
    val accessToken: String,
    val accessTokenExpiration: Long,
    val refreshToken: String,
    val refreshTokenExpiration: Long,
)

data class SyncedChatBackgroundSettings(
    val globalChatBackgroundFileId: String,
    val chatBackgroundFileIds: Map<String, String>,
)

data class PrivateChatCreateResult(
    val chat: MessagesApiOuterClass.Chat,
    val created: Boolean,
)

data class SecretInviteSent(val inviteId: String, val expiresAtSeconds: Long)
data class SecretMessageSent(val messageId: String, val expiresAtSeconds: Long)

sealed interface AuthResult {
    data class Success(
        val accessToken: String,
        val accessTokenExpiration: Long,
        val refreshToken: String,
        val refreshTokenExpiration: Long,
    ) : AuthResult

    data object OtpRequired : AuthResult
    data class Error(val message: String) : AuthResult
}

class InvalidOldPasswordException : Exception("Неверный старый пароль")

class PinErrorException(val errorCode: String?, cause: Throwable) : Exception(cause) {
    companion object {
        const val ERROR_TOO_MANY_PINNED = "F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23"
    }

    val isTooManyPinned: Boolean get() = errorCode.equals(ERROR_TOO_MANY_PINNED, ignoreCase = true)
}

/** Adapter for generated users data when a gateway is used without GrpcManager. */
internal fun UsersApiOuterClass.User.toDomainProfile(mediaUrl: (String) -> String): UserProfile {
    val pictureFileId = extractFileId(profilePicture)
    val previewFileId = extractFileId(profilePicturePreview)
    return UserProfile(
        userId = id,
        username = username,
        firstName = firstName,
        lastName = lastName,
        bio = bio,
        profilePictureUrl = mediaUrl(profilePicture),
        profilePicturePreviewUrl = mediaUrl(profilePicturePreview),
        profilePictureFileId = pictureFileId,
        profilePicturePreviewFileId = previewFileId,
        profilePosterFileId = profilePosterFileId,
        registrationDate = registrationDate.seconds * 1000,
    )
}

private fun extractFileId(value: String): String {
    if (value.isBlank()) return value
    if (!value.startsWith("http://") && !value.startsWith("https://")) return value
    val segment = value.substringAfterLast('/')
    return if (segment.matches(Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOption.IGNORE_CASE))) {
        segment
    } else {
        value
    }
}
