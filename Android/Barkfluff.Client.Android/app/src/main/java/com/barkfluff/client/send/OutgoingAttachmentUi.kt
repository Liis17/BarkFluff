package com.barkfluff.client.send

import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.cache.OutgoingMessageRecord
import com.barkfluff.client.cache.OutgoingMessageState
import java.io.File
import kotlin.math.floor

/** UI projection of one durable outbox attachment. Paths stay inside app-private staging. */
data class OutgoingAttachmentUi(
    val index: Int,
    val kind: OutgoingAttachmentKind,
    val fileName: String,
    val mimeType: String,
    val previewPath: String?,
    val stagedPath: String,
    val sizeBytes: Long,
    val uploadState: OutgoingAttachmentUploadState,
    val progress: Int,
)

enum class OutgoingAttachmentUploadState { WAITING, PREPARING, UPLOADING, DONE }

data class LiveAttachmentProgress(val attachmentIndex: Int, val progress: Int)

/** Builds a stable, index-ordered UI snapshot without changing the durable outbox record. */
fun buildOutgoingAttachmentUi(
    record: OutgoingMessageRecord,
    liveProgress: LiveAttachmentProgress? = null,
): List<OutgoingAttachmentUi> {
    val attachments = record.attachments.sortedBy { it.attachmentIndex }
    if (attachments.isEmpty()) return emptyList()

    val doneCount = attachments.count { !it.finalFileId.isNullOrBlank() }
    val current = attachments.firstOrNull { it.finalFileId.isNullOrBlank() }
    val recoveredProgress = recoveredAttachmentProgress(record.progress, attachments.size, doneCount)

    return attachments.map { attachment ->
        val isDone = !attachment.finalFileId.isNullOrBlank()
        val isCurrent = !isDone && record.state == OutgoingMessageState.UPLOADING &&
            current?.attachmentIndex == attachment.attachmentIndex
        val state = when {
            isDone -> OutgoingAttachmentUploadState.DONE
            isCurrent -> OutgoingAttachmentUploadState.UPLOADING
            else -> OutgoingAttachmentUploadState.WAITING
        }
        val path = attachment.preparedPath?.takeIf(String::isNotBlank)
            ?: attachment.sourcePath
        val file = File(path)
        OutgoingAttachmentUi(
            index = attachment.attachmentIndex,
            kind = attachment.kind,
            fileName = attachment.fileName.orEmpty(),
            mimeType = attachment.mimeType.orEmpty(),
            previewPath = attachment.previewPath,
            stagedPath = path,
            sizeBytes = file.takeIf(File::isFile)?.length() ?: 0L,
            uploadState = state,
            progress = when {
                isDone -> 100
                isCurrent -> liveProgress
                    ?.takeIf { it.attachmentIndex == attachment.attachmentIndex }
                    ?.progress?.coerceIn(0, 99)
                    ?: recoveredProgress
                else -> 0
            },
        )
    }
}

/** Inverts uploadingProgress approximately; its persisted value is rounded across all attachments. */
fun recoveredAttachmentProgress(overallProgress: Int, attachmentCount: Int, doneCount: Int): Int {
    if (attachmentCount <= 0) return 0
    return floor(
        overallProgress.coerceIn(0, 99) * attachmentCount.toDouble() -
            doneCount.coerceIn(0, attachmentCount) * 100.0
    ).toInt().coerceIn(0, 99)
}
