package com.barkfluff.client.notifications

import barkfluff.shared.Shared
import com.google.protobuf.Timestamp

// CloudMessaging FirebaseService.TruncateMessage(messagePreview, 100): longer text arrives cut with "...".
private const val PUSH_TEXT_LIMIT = 100

// Marks a push-built row. Server messages carry a UUID or nothing here, and the ID is only matched for own messages.
private const val PUSH_PREVIEW_OPERATION_ID = "push-preview"

/**
 * Push rows are sparse: they may sit next to messages that never reached the cache. Paging treats a cached
 * page as complete, so it must skip them and fall back to the server.
 */
internal fun Shared.Message.isPushPreview(): Boolean = clientOperationId == PUSH_PREVIEW_OPERATION_ID

/**
 * Builds a cache row from a regular-chat FCM payload so a chat opened from the notification shows the
 * new message before the server page arrives. Only plain text that the server did not truncate qualifies:
 * attachments, replies and own messages cannot be reconstructed from the payload. The server page later
 * replaces the row by message ID.
 */
internal fun pushedMessageForCache(
    data: Map<String, String>,
    currentUserId: Long,
    receivedAtMillis: Long,
): Shared.Message? {
    val messageId = data["message_id"]?.toLongOrNull()?.takeIf { it > 0 } ?: return null
    val senderId = data["sender_id"]?.toLongOrNull()?.takeIf { it > 0 && it != currentUserId } ?: return null
    if (data["content_type"] != "0" || data["attachment_count"] != "0") return null
    val text = data["message_text"]?.takeIf { it.isNotBlank() && it.length <= PUSH_TEXT_LIMIT } ?: return null
    if (receivedAtMillis <= 0) return null

    return Shared.Message.newBuilder()
        .setId(messageId)
        .setSenderId(senderId)
        .setSentAt(
            Timestamp.newBuilder()
                .setSeconds(receivedAtMillis / 1_000)
                .setNanos(((receivedAtMillis % 1_000) * 1_000_000).toInt())
        )
        .setContent(Shared.MessageContent.newBuilder().setText(text))
        .setType(Shared.MessageContentType.GENERIC)
        .setClientOperationId(PUSH_PREVIEW_OPERATION_ID)
        .build()
}
