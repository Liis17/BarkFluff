package com.barkfluff.client.cache

import androidx.room.Dao
import androidx.room.Entity
import androidx.room.Index
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

/** Independent of the send operation: cancelling/pruning a send must not lose its read receipt. */
@Entity(
    tableName = "pending_message_reads",
    primaryKeys = ["scopeId", "messageId"],
    indices = [Index(value = ["scopeId", "nextAttemptAtMillis"])]
)
data class PendingMessageReadEntity(
    val scopeId: String,
    val messageId: Long,
    val chatId: String,
    val attemptCount: Int = 0,
    val nextAttemptAtMillis: Long = 0
)

@Dao
interface PendingMessageReadDao {
    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun insert(read: PendingMessageReadEntity)

    @Query("SELECT * FROM pending_message_reads WHERE scopeId = :scopeId AND nextAttemptAtMillis <= :nowMillis ORDER BY nextAttemptAtMillis, messageId LIMIT :limit")
    suspend fun ready(scopeId: String, nowMillis: Long, limit: Int): List<PendingMessageReadEntity>

    @Query("SELECT MIN(nextAttemptAtMillis) FROM pending_message_reads WHERE scopeId = :scopeId")
    suspend fun nextWakeAt(scopeId: String): Long?

    @Query("UPDATE pending_message_reads SET attemptCount = :attemptCount, nextAttemptAtMillis = :nextAttemptAtMillis WHERE scopeId = :scopeId AND messageId = :messageId")
    suspend fun retry(scopeId: String, messageId: Long, attemptCount: Int, nextAttemptAtMillis: Long)

    @Query("DELETE FROM pending_message_reads WHERE scopeId = :scopeId AND messageId = :messageId")
    suspend fun delete(scopeId: String, messageId: Long)

    @Query("DELETE FROM pending_message_reads WHERE scopeId = :scopeId")
    suspend fun clear(scopeId: String)
}
