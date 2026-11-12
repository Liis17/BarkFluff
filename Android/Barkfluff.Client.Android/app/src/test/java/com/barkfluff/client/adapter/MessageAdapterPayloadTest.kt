package com.barkfluff.client.adapter

import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.send.OutgoingAttachmentUi
import com.barkfluff.client.send.OutgoingAttachmentUploadState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MessageAdapterPayloadTest {
    @Test
    fun `outgoing progress changes use the partial payload`() {
        val old = optimistic()
        val new = old.copy(
            outgoingState = OutgoingMessageState.UPLOADING,
            uploadProgress = 5,
            outgoingAttachments = listOf(old.outgoingAttachments.single().copy(
                uploadState = OutgoingAttachmentUploadState.UPLOADING,
                progress = 5,
            )),
        )
        val diff = MessageAdapter.MessageDiffCallback()

        assertEquals(MessageAdapter.PAYLOAD_OUTGOING_PROGRESS, diff.getChangePayload(old, new))
    }

    @Test
    fun `selection changes use the selection payload without changing progress`() {
        val old = optimistic()
        val new = old.copy(selectionEnabled = true, isSelected = true)

        assertEquals(MessageAdapter.PAYLOAD_SELECTION, MessageAdapter.MessageDiffCallback().getChangePayload(old, new))
    }

    @Test
    fun `progress and selection changes combine their payload flags`() {
        val old = optimistic()
        val new = old.copy(
            outgoingState = OutgoingMessageState.UPLOADING,
            uploadProgress = 5,
            outgoingAttachments = listOf(old.outgoingAttachments.single().copy(
                uploadState = OutgoingAttachmentUploadState.UPLOADING,
                progress = 5,
            )),
            selectionEnabled = true,
            isSelected = true,
        )

        assertEquals(
            MessageAdapter.PAYLOAD_OUTGOING_PROGRESS or MessageAdapter.PAYLOAD_SELECTION,
            MessageAdapter.MessageDiffCallback().getChangePayload(old, new),
        )
    }

    @Test
    fun `payload flags are merged while sender info remains a string payload`() {
        val merged = MessageAdapter.mergePayloads(listOf(
            MessageAdapter.PAYLOAD_OUTGOING_PROGRESS,
            MessageAdapter.PAYLOAD_SELECTION,
            MessageAdapter.PAYLOAD_SENDER_INFO,
        ))

        assertEquals(
            MessageAdapter.PAYLOAD_OUTGOING_PROGRESS or MessageAdapter.PAYLOAD_SELECTION,
            merged?.flags,
        )
        assertEquals(true, merged?.senderInfoChanged)
    }

    @Test
    fun `empty and unknown payloads require a full bind`() {
        assertEquals(null, MessageAdapter.mergePayloads(emptyList()))
        assertEquals(null, MessageAdapter.mergePayloads(listOf(1 shl 4)))
        assertEquals(null, MessageAdapter.mergePayloads(listOf("unknown")))
    }

    @Test
    fun `confirmation with same local id remains the same adapter row`() {
        val old = optimistic()
        val confirmed = old.copy(
            messageId = 55L,
            readStatus = ReadStatus.SENT,
            outgoingState = null,
            uploadProgress = null,
            outgoingAttachments = emptyList(),
        )

        assertTrue(MessageAdapter.MessageDiffCallback().areItemsTheSame(old, confirmed))
    }

    private fun optimistic() = MessageItem(
        messageId = -55L,
        senderId = 7L,
        text = "",
        timestamp = 100L,
        attachments = emptyList(),
        readStatus = ReadStatus.SENDING,
        localId = "op-1",
        outgoingState = OutgoingMessageState.QUEUED,
        uploadProgress = 0,
        outgoingAttachments = listOf(
            OutgoingAttachmentUi(
                index = 0,
                kind = OutgoingAttachmentKind.VIDEO,
                fileName = "clip.mp4",
                mimeType = "video/mp4",
                previewPath = "/staged/clip.mp4",
                stagedPath = "/staged/clip.mp4",
                sizeBytes = 16L,
                uploadState = OutgoingAttachmentUploadState.WAITING,
                progress = 0,
            ),
        ),
    )
}
