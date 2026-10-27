package com.barkfluff.client.cache

import androidx.room.Dao
import androidx.room.Entity
import androidx.room.Index
import androidx.room.Query

/** Durable per-chat read ceiling; newer requests coalesce without losing an older cursor. */
@Entity(
    tableName = "pending_chat_read_ranges",
    primaryKeys = ["scopeId", "chatId"],
    indices = [Index(value = ["scopeId", "nextAttemptAtMillis"])],
)
data class PendingChatReadRangeEntity(
    val scopeId: String,
    val chatId: String,
    val upToMessageId: Long,
    val legacyFallback: Boolean,
    val cursorBeforeMessageId: Long,
    val attemptCount: Int,
    val nextAttemptAtMillis: Long,
)

@Dao
interface PendingChatReadRangeDao {
    @Query("""
        INSERT INTO pending_chat_read_ranges
            (scopeId, chatId, upToMessageId, legacyFallback, cursorBeforeMessageId, attemptCount, nextAttemptAtMillis)
        VALUES (:scopeId, :chatId, :upToMessageId, 0, 0, 0, :nowMillis)
        ON CONFLICT(scopeId, chatId) DO UPDATE SET
            upToMessageId = MAX(upToMessageId, excluded.upToMessageId),
            cursorBeforeMessageId = CASE WHEN excluded.upToMessageId > upToMessageId THEN 0 ELSE cursorBeforeMessageId END,
            attemptCount = CASE WHEN excluded.upToMessageId > upToMessageId THEN 0 ELSE attemptCount END,
            nextAttemptAtMillis = MIN(nextAttemptAtMillis, excluded.nextAttemptAtMillis)
    """)
    suspend fun coalesce(scopeId: String, chatId: String, upToMessageId: Long, nowMillis: Long)

    @Query("SELECT * FROM pending_chat_read_ranges WHERE scopeId = :scopeId AND nextAttemptAtMillis <= :nowMillis ORDER BY nextAttemptAtMillis, chatId LIMIT :limit")
    suspend fun ready(scopeId: String, nowMillis: Long, limit: Int): List<PendingChatReadRangeEntity>

    @Query("SELECT MIN(nextAttemptAtMillis) FROM pending_chat_read_ranges WHERE scopeId = :scopeId")
    suspend fun nextWakeAt(scopeId: String): Long?

    @Query("UPDATE pending_chat_read_ranges SET legacyFallback = 1, cursorBeforeMessageId = 0 WHERE scopeId = :scopeId AND chatId = :chatId AND upToMessageId = :upToMessageId")
    suspend fun useLegacyFallback(scopeId: String, chatId: String, upToMessageId: Long): Int

    @Query("UPDATE pending_chat_read_ranges SET cursorBeforeMessageId = :cursorBeforeMessageId WHERE scopeId = :scopeId AND chatId = :chatId AND upToMessageId = :upToMessageId")
    suspend fun updateCursor(scopeId: String, chatId: String, upToMessageId: Long, cursorBeforeMessageId: Long): Int

    @Query("UPDATE pending_chat_read_ranges SET attemptCount = :attemptCount, nextAttemptAtMillis = :nextAttemptAtMillis WHERE scopeId = :scopeId AND chatId = :chatId AND upToMessageId = :upToMessageId")
    suspend fun retry(scopeId: String, chatId: String, upToMessageId: Long, attemptCount: Int, nextAttemptAtMillis: Long): Int

    @Query("DELETE FROM pending_chat_read_ranges WHERE scopeId = :scopeId AND chatId = :chatId AND upToMessageId <= :completedCutoff")
    suspend fun completeThrough(scopeId: String, chatId: String, completedCutoff: Long): Int

    @Query("DELETE FROM pending_chat_read_ranges WHERE scopeId = :scopeId")
    suspend fun clear(scopeId: String)
}
