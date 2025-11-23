package com.barkfluff.messenger.domain.model

import java.time.Instant

data class Chat(
    val id: String,
    val title: String,
    val pictureUrl: String?,
    val isGroupChat: Boolean,
    val lastMessage: Message?,
    val members: List<ChatMember>,
    val unreadCount: Int
)

data class ChatMember(
    val userId: Long,
    val joinedAt: Instant
)

data class Message(
    val id: String,
    val senderId: Long,
    val chatId: String?,
    val content: MessageContent,
    val sentAt: Instant,
    val readBy: List<Long>,
    val type: MessageType
) {
    val isRead: Boolean
        get() = readBy.isNotEmpty()
}

data class MessageContent(
    val text: String?,
    val attachments: List<MessageAttachment>
)

data class MessageAttachment(
    val id: String,
    val type: MessageAttachmentType,
    val fileId: String,
    val previewUrl: String?,
    val size: Long
)

enum class MessageType {
    UNKNOWN,
    GENERIC,
    SYSTEM
}

enum class MessageAttachmentType {
    UNKNOWN,
    IMAGE,
    VIDEO,
    GIF,
    DOCUMENT
}
