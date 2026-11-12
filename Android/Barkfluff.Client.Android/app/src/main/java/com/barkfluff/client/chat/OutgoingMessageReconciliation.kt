package com.barkfluff.client.chat

/** Matches an own server echo to its optimistic row, including when the server omits operation ID. */
internal fun MessageRowUi.matchesOutgoingEcho(
    senderId: Long,
    currentUserId: Long,
    messageId: Long,
    clientOperationId: String,
    text: String,
    attachmentCount: Int,
): Boolean {
    if (type != MessageType.MESSAGE || localId == null || senderId != currentUserId) return false
    if ((clientOperationId.isNotBlank() && localId == clientOperationId) || this.messageId == messageId) return true
    // Эхо с operation ID принадлежит конкретной операции: по тексту его к чужой строке не привязываем.
    if (clientOperationId.isNotBlank()) return false
    if (this.text != text) return false

    val hasOutgoingMarker = outgoingState != null || uploadProgress != null ||
        localPreviewUris.isNotEmpty() || outgoingAttachments.isNotEmpty()
    val modeledAttachmentCount = when {
        outgoingAttachments.isNotEmpty() -> outgoingAttachments.size
        localPreviewUris.isNotEmpty() -> localPreviewUris.size
        else -> attachments.size
    }
    return hasOutgoingMarker && modeledAttachmentCount == attachmentCount
}

internal fun findOutgoingEchoMatch(
    rows: List<MessageRowUi>,
    senderId: Long,
    currentUserId: Long,
    messageId: Long,
    clientOperationId: String,
    text: String,
    attachmentCount: Int,
): Int {
    // Сообщение уже есть в ленте (повторная подача подтверждённого) — это не эхо новой отправки:
    // иначе оно «усыновляет» другой исходящий пузырь с тем же текстом и числом вложений.
    val known = rows.indexOfFirst { it.type == MessageType.MESSAGE && it.messageId == messageId }
    if (known >= 0) return if (rows[known].localId != null) known else -1
    return rows.indexOfFirst { row ->
        row.matchesOutgoingEcho(
            senderId = senderId,
            currentUserId = currentUserId,
            messageId = messageId,
            clientOperationId = clientOperationId,
            text = text,
            attachmentCount = attachmentCount,
        )
    }
}
