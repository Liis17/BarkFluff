package com.barkfluff.client.cache

import androidx.room.Dao
import androidx.room.Entity
import androidx.room.Index
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

/** Persistent lifecycle of one logical regular-chat send operation. */
enum class OutgoingMessageState {
    STAGING,
    QUEUED,
    PREPARING,
    UPLOADING,
    SENDING,
    FAILED,
    SENT,
    CANCEL_REQUESTED
}

enum class OutgoingAttachmentKind {
    RAW_IMAGE,
    EDITED_IMAGE,
    VIDEO,
    DOCUMENT,
    STICKER,
    VOICE
}

enum class OutgoingFailureCategory {
    NETWORK,
    SERVER,
    AUTH_REQUIRED,
    ACCESS,
    VALIDATION,
    SOURCE_UNAVAILABLE,
    UNKNOWN
}

@Entity(
    tableName = "outgoing_messages",
    primaryKeys = ["scopeId", "operationId"],
    indices = [
        Index(value = ["scopeId", "chatId", "createdAtMillis"]),
        Index(value = ["scopeId", "state", "nextAttemptAtMillis"])
    ]
)
data class OutgoingMessageEntity(
    val scopeId: String,
    val operationId: String,
    val batchId: String?,
    val chatId: String,
    val chatTitle: String,
    val text: String,
    val replyToMessageId: Long,
    val draftGeneration: Long?,
    val sendAsFile: Boolean,
    /** Already-uploaded file ids, e.g. a sticker chosen from a server pack. */
    val existingFileIds: String,
    val createdAtMillis: Long,
    val state: String,
    val progress: Int,
    val attemptCount: Int,
    val nextAttemptAtMillis: Long,
    val lastFailureCategory: String?,
    val lastFailureDetail: String?,
    val leaseOwner: String?,
    val leaseExpiresAtMillis: Long,
    val serverMessageId: Long,
    val serverMessagePayload: ByteArray?
)

@Entity(
    tableName = "outgoing_attachments",
    primaryKeys = ["scopeId", "operationId", "attachmentIndex"],
    indices = [Index(value = ["scopeId", "operationId"])]
)
data class OutgoingAttachmentEntity(
    val scopeId: String,
    val operationId: String,
    val attachmentIndex: Int,
    val kind: String,
    val uploadFileTypeNumber: Int,
    val sourcePath: String,
    val preparedPath: String?,
    val previewPath: String?,
    val fileName: String?,
    val mimeType: String?,
    val uploadOperationId: String,
    val reservedFileId: String?,
    val finalFileId: String?,
    val trimStartMs: Long,
    val trimEndMs: Long,
    val compressTo480p: Boolean
)

data class OutgoingAttachmentRecord(
    val attachmentIndex: Int,
    val kind: OutgoingAttachmentKind,
    val uploadFileTypeNumber: Int,
    val sourcePath: String,
    val preparedPath: String?,
    val previewPath: String?,
    val fileName: String?,
    val mimeType: String?,
    val uploadOperationId: String,
    val reservedFileId: String?,
    val finalFileId: String?,
    val trimStartMs: Long,
    val trimEndMs: Long,
    val compressTo480p: Boolean
)

data class OutgoingMessageRecord(
    val operationId: String,
    val batchId: String?,
    val chatId: String,
    val chatTitle: String,
    val text: String,
    val replyToMessageId: Long,
    val draftGeneration: Long?,
    val sendAsFile: Boolean,
    val existingFileIds: List<String>,
    val createdAtMillis: Long,
    val state: OutgoingMessageState,
    val progress: Int,
    val attemptCount: Int,
    val nextAttemptAtMillis: Long,
    val failureCategory: OutgoingFailureCategory?,
    val failureDetail: String?,
    val leaseOwner: String?,
    val leaseExpiresAtMillis: Long,
    val serverMessageId: Long,
    val serverMessagePayload: ByteArray?,
    val attachments: List<OutgoingAttachmentRecord>
)

@Dao
interface OutgoingMessageDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertMessage(message: OutgoingMessageEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertAttachments(attachments: List<OutgoingAttachmentEntity>)

    @Query("SELECT * FROM outgoing_messages WHERE scopeId = :scopeId AND operationId = :operationId")
    suspend fun message(scopeId: String, operationId: String): OutgoingMessageEntity?

    @Query("SELECT * FROM outgoing_attachments WHERE scopeId = :scopeId AND operationId = :operationId ORDER BY attachmentIndex")
    suspend fun attachments(scopeId: String, operationId: String): List<OutgoingAttachmentEntity>

    @Query("SELECT * FROM outgoing_messages WHERE scopeId = :scopeId AND chatId = :chatId ORDER BY createdAtMillis, operationId")
    fun observeMessages(scopeId: String, chatId: String): Flow<List<OutgoingMessageEntity>>

    @Query("""
        SELECT * FROM outgoing_messages AS candidate
        WHERE candidate.scopeId = :scopeId
          AND candidate.state = :queuedState
          AND candidate.nextAttemptAtMillis <= :nowMillis
          AND NOT EXISTS (
              SELECT 1 FROM outgoing_messages AS earlier
              WHERE earlier.scopeId = candidate.scopeId
                AND earlier.chatId = candidate.chatId
                AND (earlier.createdAtMillis < candidate.createdAtMillis
                    OR (earlier.createdAtMillis = candidate.createdAtMillis AND earlier.operationId < candidate.operationId))
                AND earlier.state NOT IN (:sentState, :cancelledState)
          )
        ORDER BY candidate.createdAtMillis, candidate.operationId
        LIMIT :limit
    """)
    suspend fun readyHeads(
        scopeId: String,
        queuedState: String,
        sentState: String,
        cancelledState: String,
        nowMillis: Long,
        limit: Int
    ): List<OutgoingMessageEntity>

    @Query("""
        UPDATE outgoing_messages
        SET state = :queuedState, leaseOwner = NULL, leaseExpiresAtMillis = 0
        WHERE scopeId = :scopeId
          AND state IN (:preparingState, :uploadingState, :sendingState)
          AND leaseExpiresAtMillis > 0
          AND leaseExpiresAtMillis < :nowMillis
    """)
    suspend fun recoverExpiredLeases(
        scopeId: String,
        queuedState: String,
        preparingState: String,
        uploadingState: String,
        sendingState: String,
        nowMillis: Long
    ): Int

    @Query("DELETE FROM outgoing_attachments WHERE scopeId = :scopeId AND operationId = :operationId")
    suspend fun deleteAttachments(scopeId: String, operationId: String)

    @Query("DELETE FROM outgoing_messages WHERE scopeId = :scopeId AND operationId = :operationId")
    suspend fun deleteMessage(scopeId: String, operationId: String)

    @Query("SELECT MIN(nextAttemptAtMillis) FROM outgoing_messages WHERE scopeId = :scopeId AND state = :queuedState")
    suspend fun nextQueuedAttempt(scopeId: String, queuedState: String): Long?

    @Query("SELECT * FROM outgoing_messages WHERE scopeId = :scopeId AND state = :sentState AND createdAtMillis < :beforeMillis")
    suspend fun oldSent(scopeId: String, sentState: String, beforeMillis: Long): List<OutgoingMessageEntity>

    @Query("SELECT operationId FROM outgoing_messages WHERE scopeId = :scopeId")
    suspend fun operationIds(scopeId: String): List<String>

    @Query("DELETE FROM outgoing_messages WHERE scopeId = :scopeId AND state = :stagingState")
    suspend fun deleteStaging(scopeId: String, stagingState: String): Int

    @Query("DELETE FROM outgoing_attachments WHERE scopeId = :scopeId AND operationId NOT IN (SELECT operationId FROM outgoing_messages WHERE scopeId = :scopeId)")
    suspend fun deleteDetachedAttachments(scopeId: String): Int

    @Query("DELETE FROM outgoing_attachments WHERE scopeId = :scopeId")
    suspend fun deleteAllAttachments(scopeId: String)

    @Query("DELETE FROM outgoing_messages WHERE scopeId = :scopeId")
    suspend fun deleteAllMessages(scopeId: String)
}
