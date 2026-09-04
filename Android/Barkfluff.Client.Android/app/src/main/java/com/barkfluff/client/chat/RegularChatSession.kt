package com.barkfluff.client.chat

import barkfluff.shared.Shared
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.domain.gateway.MessageGateway

/**
 * Deep module for a regular-chat timeline. It owns cache-first reads and cursor semantics; the
 * ViewModel only turns returned protobuf messages into immutable UI rows.
 */
class RegularChatSession(
    private val messages: MessageGateway,
    private val cache: ChatCacheRepository,
) {
    data class Page(
        val messages: List<Shared.Message>,
        val hasMoreBefore: Boolean,
        val hasMoreAfter: Boolean,
        val fromCache: Boolean = false,
    )

    suspend fun cached(scope: CacheScope?, chatId: String, pageSize: Int = 30): List<Shared.Message> =
        scope?.let { cache.latestMessages(it, chatId, pageSize) }.orEmpty()

    suspend fun initial(
        scope: CacheScope?,
        chatId: String,
        firstUnreadMessageId: Long = 0L,
        pageSize: Int = 30,
    ): Result<Page> {
        val cached = scope?.let { cache.latestMessages(it, chatId, pageSize) }.orEmpty()
        if (cached.isNotEmpty()) {
            return Result.success(Page(cached, hasMoreBefore = cached.size >= pageSize, hasMoreAfter = false, fromCache = true))
        }
        return messages.loadMessages(
            chatId = chatId,
            fromMessageId = firstUnreadMessageId,
            offsetBefore = if (firstUnreadMessageId > 0L) 15 else 0,
            offsetAfter = if (firstUnreadMessageId > 0L) pageSize else 0,
            count = if (firstUnreadMessageId > 0L) pageSize else pageSize,
        ).map { loaded ->
            scope?.let { cache.saveMessages(it, chatId, loaded) }
            Page(
                messages = loaded,
                hasMoreBefore = loaded.size >= if (firstUnreadMessageId > 0L) 15 else pageSize,
                hasMoreAfter = firstUnreadMessageId > 0L,
            )
        }
    }

    suspend fun before(
        scope: CacheScope?,
        chatId: String,
        beforeMessageId: Long,
        pageSize: Int = 30,
    ): Result<Page> {
        if (beforeMessageId <= 0L) return Result.success(Page(emptyList(), false, false, true))
        val cached = scope?.let { cache.messagesBefore(it, chatId, beforeMessageId, pageSize) }.orEmpty()
        if (cached.isNotEmpty()) {
            return Result.success(Page(cached, cached.size >= pageSize, hasMoreAfter = true, fromCache = true))
        }
        return messages.loadMessages(chatId, beforeMessageId, offsetBefore = pageSize, offsetAfter = 0).map { loaded ->
            scope?.let { cache.saveMessages(it, chatId, loaded) }
            Page(loaded, loaded.size >= pageSize, hasMoreAfter = true)
        }
    }

    suspend fun after(
        scope: CacheScope?,
        chatId: String,
        afterMessageId: Long,
        pageSize: Int = 30,
    ): Result<Page> {
        if (afterMessageId <= 0L) return Result.success(Page(emptyList(), false, false, true))
        val cached = scope?.let { cache.messagesAfter(it, chatId, afterMessageId, pageSize) }.orEmpty()
        if (cached.isNotEmpty()) {
            return Result.success(Page(cached, hasMoreBefore = true, hasMoreAfter = cached.size >= pageSize, fromCache = true))
        }
        return messages.loadMessages(chatId, afterMessageId, offsetBefore = 0, offsetAfter = pageSize).map { loaded ->
            scope?.let { cache.saveMessages(it, chatId, loaded) }
            Page(loaded, hasMoreBefore = true, hasMoreAfter = loaded.size >= pageSize)
        }
    }

    suspend fun cacheMessage(scope: CacheScope?, chatId: String, message: Shared.Message) {
        scope?.let { cache.saveMessages(it, chatId, listOf(message)) }
    }

    suspend fun markRead(messageIds: List<Long>): Result<Unit> =
        if (messageIds.isEmpty()) Result.success(Unit) else messages.markAsRead(messageIds)
}
