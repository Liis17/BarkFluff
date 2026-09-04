package com.barkfluff.client.chat

import barkfluff.shared.Shared
import com.barkfluff.client.cache.OutgoingMessageState

/** Immutable row contract shared by regular, pinned and E2E timelines. */
enum class MessageType { MESSAGE, DATE_SEPARATOR, UNREAD_SEPARATOR, FOOTER, SYSTEM }

/** Immutable row consumed by every message renderer and adapter. */
data class MessageRowUi(
    val messageId: Long,
    val senderId: Long,
    val senderName: String? = null,
    val senderAvatarFileId: String? = null,
    val text: String,
    val timestamp: Long,
    val attachments: List<Shared.MessageAttachment>,
    val replyTo: Shared.ReplyInfo? = null,
    val readStatus: ReadStatus = ReadStatus.NONE,
    val type: MessageType = MessageType.MESSAGE,
    val dateText: String = "",
    val isEdited: Boolean = false,
    val localId: String? = null,
    val clientOperationId: String? = null,
    val outgoingState: OutgoingMessageState? = null,
    val uploadProgress: Int? = null,
    val localPreviewUris: List<android.net.Uri> = emptyList(),
    val isSelected: Boolean = false,
    val selectionEnabled: Boolean = false,
) {
    companion object {
        fun createDateSeparator(dateText: String) = MessageRowUi(
            messageId = 0,
            senderId = 0,
            text = "",
            timestamp = 0,
            attachments = emptyList(),
            type = MessageType.DATE_SEPARATOR,
            dateText = dateText,
        )

        fun createUnreadSeparator(label: String) = MessageRowUi(
            messageId = -2,
            senderId = 0,
            text = "",
            timestamp = 0,
            attachments = emptyList(),
            type = MessageType.UNREAD_SEPARATOR,
            dateText = label,
        )

        fun createFooter() = MessageRowUi(
            messageId = Long.MIN_VALUE,
            senderId = 0,
            text = "",
            timestamp = 0,
            attachments = emptyList(),
            type = MessageType.FOOTER,
        )
    }
}

/** Source-compatible name retained for E2E and pinned consumers during the row migration. */
typealias MessageItem = MessageRowUi

enum class ReadStatus { NONE, SENDING, SENT, DELIVERED, READ, FAILED }
