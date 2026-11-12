package com.barkfluff.client.chat

import android.net.Uri
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.send.OutgoingAttachmentUi
import com.barkfluff.client.send.OutgoingAttachmentUploadState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class OutgoingMessageReconciliationTest {
    @Test
    fun `echo before send response keeps local identity for document without preview`() {
        assertEchoKeepsIdentity(
            item = optimistic(
                state = OutgoingMessageState.UPLOADING,
                text = "",
                attachment = outgoingAttachment(OutgoingAttachmentKind.DOCUMENT),
            ),
            text = "",
            attachmentCount = 1,
        )
    }

    @Test
    fun `send response before echo keeps local identity for voice without preview`() {
        assertEchoKeepsIdentity(
            item = optimistic(
                state = OutgoingMessageState.SENT,
                status = ReadStatus.SENT,
                progress = 100,
                text = "",
                attachment = outgoingAttachment(OutgoingAttachmentKind.VOICE),
            ),
            text = "",
            attachmentCount = 1,
        )
    }

    @Test
    fun `text bubble retains hidden reconciliation marker without attachments`() {
        val item = optimistic(
            state = OutgoingMessageState.QUEUED,
            text = "hello",
            progress = 0,
        )

        assertEquals(0, findOutgoingEchoMatch(
            rows = listOf(item),
            senderId = 7L,
            currentUserId = 7L,
            messageId = 55L,
            clientOperationId = "",
            text = "hello",
            attachmentCount = 0,
        ))
        assertEquals(0, item.uploadProgress)
        assertEquals(emptyList<Uri>(), item.localPreviewUris)
    }

    private fun assertEchoKeepsIdentity(item: MessageRowUi, text: String, attachmentCount: Int) {
        val match = findOutgoingEchoMatch(
            rows = listOf(item),
            senderId = 7L,
            currentUserId = 7L,
            messageId = 55L,
            clientOperationId = "",
            text = text,
            attachmentCount = attachmentCount,
        )
        assertEquals(0, match)
        val confirmed = item.copy(
            messageId = 55L,
            localId = item.localId,
            readStatus = ReadStatus.SENT,
            outgoingState = null,
            uploadProgress = null,
            outgoingAttachments = emptyList(),
        )
        val decision = ChatScrollDecision.decide(
            committed = listOf(item),
            next = listOf(confirmed),
            currentUserId = 7L,
            wasAtBottom = false,
        )
        assertFalse(decision.scrollToBottom)
        assertEquals(item.localId, confirmed.localId)
    }

    private fun optimistic(
        state: OutgoingMessageState,
        status: ReadStatus = ReadStatus.SENDING,
        progress: Int = 25,
        text: String,
        attachment: OutgoingAttachmentUi? = null,
    ) = MessageRowUi(
        messageId = -55L,
        senderId = 7L,
        text = text,
        timestamp = 100L,
        attachments = emptyList(),
        readStatus = status,
        localId = "op-1",
        outgoingState = state,
        uploadProgress = progress,
        localPreviewUris = emptyList(),
        outgoingAttachments = listOfNotNull(attachment),
    )

    private fun outgoingAttachment(kind: OutgoingAttachmentKind) = OutgoingAttachmentUi(
        index = 0,
        kind = kind,
        fileName = "staged-file",
        mimeType = "application/octet-stream",
        previewPath = null,
        stagedPath = "/staged/file",
        sizeBytes = 16L,
        uploadState = OutgoingAttachmentUploadState.WAITING,
        progress = 0,
    )
}
