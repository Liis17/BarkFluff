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
): Int = rows.indexOfFirst { row ->
    row.matchesOutgoingEcho(
        senderId = senderId,
        currentUserId = currentUserId,
        messageId = messageId,
        clientOperationId = clientOperationId,
        text = text,
        attachmentCount = attachmentCount,
    )
}
