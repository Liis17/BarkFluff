package com.barkfluff.client.send

import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.cache.OutgoingAttachmentRecord
import com.barkfluff.client.cache.OutgoingMessageRecord
import com.barkfluff.client.cache.OutgoingMessageState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class OutgoingAttachmentUiTest {
    @Test
    fun `mixed document voice and video preserve stable indexes and uploading state`() {
        val ui = buildOutgoingAttachmentUi(
            record(
                state = OutgoingMessageState.UPLOADING,
                progress = 35,
                attachments = listOf(
                    attachment(2, OutgoingAttachmentKind.VOICE, "voice.ogg"),
                    attachment(0, OutgoingAttachmentKind.VIDEO, "clip.mp4", preview = "/clip.mp4"),
                    attachment(1, OutgoingAttachmentKind.DOCUMENT, "notes.pdf"),
                ),
            ),
            liveProgress = LiveAttachmentProgress(0, 45),
        )

        assertEquals(listOf(0, 1, 2), ui.map { it.index })
        assertEquals(OutgoingAttachmentKind.VIDEO, ui[0].kind)
        assertEquals(OutgoingAttachmentUploadState.UPLOADING, ui[0].uploadState)
        assertEquals(45, ui[0].progress)
        assertEquals(OutgoingAttachmentUploadState.WAITING, ui[1].uploadState)
        assertEquals(OutgoingAttachmentKind.VOICE, ui[2].kind)
        assertEquals(OutgoingAttachmentUploadState.WAITING, ui[2].uploadState)
    }

    @Test
    fun `partial album keeps completed cells and recovers current progress without memory`() {
        val ui = buildOutgoingAttachmentUi(
            record(
                state = OutgoingMessageState.UPLOADING,
                progress = 40,
                attachments = listOf(
                    attachment(0, OutgoingAttachmentKind.RAW_IMAGE, "a.jpg", preview = "/a.jpg", finalId = "file-a"),
                    attachment(1, OutgoingAttachmentKind.EDITED_IMAGE, "b.jpg", preview = "/b.jpg"),
                    attachment(2, OutgoingAttachmentKind.RAW_IMAGE, "c.jpg", preview = "/c.jpg"),
                ),
            ),
        )

        assertEquals(OutgoingAttachmentUploadState.DONE, ui[0].uploadState)
        assertEquals(100, ui[0].progress)
        assertEquals(OutgoingAttachmentUploadState.UPLOADING, ui[1].uploadState)
        assertEquals(20, ui[1].progress)
        assertEquals(OutgoingAttachmentUploadState.WAITING, ui[2].uploadState)
    }

    @Test
    fun `rounded overall progress is inverted approximately and capped below done`() {
        assertEquals(50, recoveredAttachmentProgress(overallProgress = 5, attachmentCount = 10, doneCount = 0))
        assertEquals(99, recoveredAttachmentProgress(overallProgress = 10, attachmentCount = 10, doneCount = 0))
    }

    @Test
    fun `preparing operation keeps every pending attachment indeterminate`() {
        val ui = buildOutgoingAttachmentUi(
            record(
                state = OutgoingMessageState.PREPARING,
                progress = 35,
                attachments = listOf(
                    attachment(0, OutgoingAttachmentKind.VIDEO, "clip.mp4"),
                    attachment(1, OutgoingAttachmentKind.DOCUMENT, "notes.pdf"),
                ),
            ),
        )

        assertEquals(listOf(OutgoingAttachmentUploadState.WAITING, OutgoingAttachmentUploadState.WAITING), ui.map { it.uploadState })
        assertEquals(listOf(0, 0), ui.map { it.progress })
    }

    @Test
    fun `failed operation hides pending attachment progress but retains done ids`() {
        val ui = buildOutgoingAttachmentUi(
            record(
                state = OutgoingMessageState.FAILED,
                progress = 55,
                attachments = listOf(
                    attachment(0, OutgoingAttachmentKind.DOCUMENT, "done.pdf", finalId = "file-done"),
                    attachment(1, OutgoingAttachmentKind.VOICE, "pending.ogg"),
                ),
            ),
        )

        assertEquals(OutgoingAttachmentUploadState.DONE, ui[0].uploadState)
        assertEquals(100, ui[0].progress)
        assertEquals(OutgoingAttachmentUploadState.WAITING, ui[1].uploadState)
        assertEquals(0, ui[1].progress)
    }

    @Test
    fun `retry starts waiting after done attachments remain complete`() {
        val ui = buildOutgoingAttachmentUi(
            record(
                state = OutgoingMessageState.QUEUED,
                progress = 0,
                attachments = listOf(
                    attachment(0, OutgoingAttachmentKind.RAW_IMAGE, "done-a.jpg", finalId = "file-done-a"),
                    attachment(1, OutgoingAttachmentKind.RAW_IMAGE, "done-b.jpg", finalId = "file-done-b"),
                    attachment(2, OutgoingAttachmentKind.VIDEO, "done-c.mp4", finalId = "file-done-c"),
                    attachment(3, OutgoingAttachmentKind.DOCUMENT, "retry.pdf"),
                ),
            ),
        )

        assertEquals(
            listOf(
                OutgoingAttachmentUploadState.DONE,
                OutgoingAttachmentUploadState.DONE,
                OutgoingAttachmentUploadState.DONE,
                OutgoingAttachmentUploadState.WAITING,
            ),
            ui.map { it.uploadState },
        )
        assertEquals(listOf(100, 100, 100, 0), ui.map { it.progress })
        assertNull(ui[3].previewPath)
    }

    private fun record(
        state: OutgoingMessageState,
        progress: Int,
        attachments: List<OutgoingAttachmentRecord>,
    ) = OutgoingMessageRecord(
        operationId = "op-1",
        batchId = null,
        chatId = "chat-1",
        chatTitle = "chat",
        text = "",
        replyToMessageId = 0,
        draftGeneration = null,
        sendAsFile = false,
        existingFileIds = emptyList(),
        createdAtMillis = 1L,
        state = state,
        progress = progress,
        attemptCount = 0,
        nextAttemptAtMillis = 0L,
        failureCategory = null,
        failureDetail = null,
        leaseOwner = null,
        leaseExpiresAtMillis = 0L,
        serverMessageId = 0L,
        serverMessagePayload = null,
        attachments = attachments,
    )

    private fun attachment(
        index: Int,
        kind: OutgoingAttachmentKind,
        name: String,
        preview: String? = null,
        finalId: String? = null,
    ) = OutgoingAttachmentRecord(
        attachmentIndex = index,
        kind = kind,
        uploadFileTypeNumber = 0,
        sourcePath = "/staged/$name",
        preparedPath = null,
        previewPath = preview,
        fileName = name,
        mimeType = null,
        uploadOperationId = "upload-$index",
        reservedFileId = null,
        finalFileId = finalId,
        trimStartMs = 0L,
        trimEndMs = -1L,
        compressTo480p = false,
    )
}
