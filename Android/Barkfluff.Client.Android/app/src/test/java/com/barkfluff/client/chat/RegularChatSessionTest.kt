package com.barkfluff.client.chat

import barkfluff.shared.Shared
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.domain.gateway.MessageGateway
import com.barkfluff.client.domain.model.ChatInfo
import com.barkfluff.client.domain.model.PinnedMessagePage
import com.barkfluff.client.insertMessageInChronologicalOrder
import com.google.protobuf.Timestamp
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class RegularChatSessionTest {
    @Test
    fun outOfOrderSentReadsAndRealtimeMessagesStayChronological() = runTest {
        val timeline = ConfirmedMessageTimeline()
        val rows = mutableListOf<MessageItem>()
        val sent341 = CompletableDeferred<Shared.Message>()
        val sent346 = CompletableDeferred<Shared.Message>()
        val read341 = async { addConfirmedMessage(timeline, rows, sent341.await()) }
        val read346 = async { addConfirmedMessage(timeline, rows, sent346.await()) }

        sent346.complete(message(346))
        runCurrent()
        addConfirmedMessage(timeline, rows, message(345))
        sent341.complete(message(341))
        runCurrent()
        read346.await()
        read341.await()

        assertEquals(listOf(341L, 345L, 346L), rows.filter(::isMessage).map { it.messageId })
        assertEquals(1, rows.count { it.type == MessageType.DATE_SEPARATOR })
    }

    @Test
    fun confirmedMessagesUseNanosecondsAndIdAsTieBreakers() {
        val timeline = ConfirmedMessageTimeline()
        val rows = mutableListOf<MessageItem>()
        listOf(
            message(9, seconds = 100, nanos = 200),
            message(10, seconds = 100, nanos = 100),
            message(8, seconds = 100, nanos = 100),
        ).forEach { addConfirmedMessage(timeline, rows, it) }

        assertEquals(listOf(8L, 10L, 9L), rows.filter(::isMessage).map { it.messageId })
    }

    @Test
    fun catchUpBypassesShortCachedTailAndLoadsServerPagesUntilShortPage() = runTest {
        val serverMessages = (11L..15L).map { message(it) }
        val gateway = RecordingMessageGateway { cursor, limit ->
            serverMessages.filter { it.id > cursor }.take(limit)
        }
        val shortCachedTail = listOf(message(11))
        var cacheReads = 0
        val savedPages = mutableListOf<List<Long>>()
        val session = RegularChatSession(
            messages = gateway,
            cache = null,
            readAfterCache = { _, _, cursor, limit ->
                cacheReads++
                shortCachedTail.filter { it.id > cursor }.take(limit)
            },
            saveMessages = { _, _, page -> savedPages += page.map { it.id } },
        )

        val loaded = session.catchUpAfter(CacheScope("test"), "chat", afterMessageId = 10, pageSize = 2).getOrThrow()

        assertEquals(listOf(11L, 12L, 13L, 14L, 15L), loaded.messages.map { it.id })
        assertFalse(loaded.hasMoreAfter)
        assertEquals(listOf(10L, 12L, 14L), gateway.cursors)
        assertEquals(0, cacheReads)
        assertEquals(listOf(listOf(11L, 12L), listOf(13L, 14L), listOf(15L)), savedPages)
    }

    @Test
    fun catchUpStopsAtPageLimitAndReportsRemainingTail() = runTest {
        val serverMessages = (11L..20L).map { message(it) }
        val gateway = RecordingMessageGateway { cursor, limit ->
            serverMessages.filter { it.id > cursor }.take(limit)
        }
        val session = RegularChatSession(messages = gateway, cache = null, saveMessages = { _, _, _ -> })

        val loaded = session.catchUpAfter(CacheScope("test"), "chat", afterMessageId = 10, pageSize = 2, maxPages = 2)
            .getOrThrow()

        assertEquals(listOf(11L, 12L, 13L, 14L), loaded.messages.map { it.id })
        assertTrue(loaded.hasMoreAfter)
        assertEquals(listOf(10L, 12L), gateway.cursors)
    }

    @Test
    fun normalForwardPaginationStillUsesCacheFirst() = runTest {
        val gateway = RecordingMessageGateway { _, _ -> emptyList() }
        val session = RegularChatSession(
            messages = gateway,
            cache = null,
            readAfterCache = { _, _, _, _ -> listOf(message(11)) },
        )

        val page = session.after(CacheScope("test"), "chat", afterMessageId = 10, pageSize = 30).getOrThrow()

        assertEquals(listOf(11L), page.messages.map { it.id })
        assertFalse(page.hasMoreAfter)
        assertEquals(emptyList<Long>(), gateway.cursors)
    }

    private fun message(id: Long, seconds: Long = id, nanos: Int = 0): Shared.Message =
        Shared.Message.newBuilder()
            .setId(id)
            .setSentAt(Timestamp.newBuilder().setSeconds(seconds).setNanos(nanos).build())
            .build()

    private fun addConfirmedMessage(
        timeline: ConfirmedMessageTimeline,
        rows: MutableList<MessageItem>,
        message: Shared.Message,
    ) {
        timeline.upsert(message)
        insertMessageInChronologicalOrder(
            target = rows,
            message = message,
            item = MessageItem(
                messageId = message.id,
                senderId = message.senderId,
                text = "",
                timestamp = message.sentAt.seconds * 1000,
                attachments = emptyList(),
            ),
            messageForId = timeline::message,
            startOfDay = { it / 86_400_000L },
            dateSeparator = { MessageItem.createDateSeparator("day:$it") },
        )
    }

    private fun isMessage(item: MessageItem): Boolean =
        item.type == MessageType.MESSAGE || item.type == MessageType.SYSTEM

    private class RecordingMessageGateway(
        private val loadPage: (Long, Int) -> List<Shared.Message>,
    ) : MessageGateway {
        val cursors = mutableListOf<Long>()

        override suspend fun loadMessages(
            chatId: String,
            fromMessageId: Long,
            offsetBefore: Int,
            offsetAfter: Int,
            count: Int,
        ): Result<List<Shared.Message>> {
            cursors += fromMessageId
            return Result.success(loadPage(fromMessageId, offsetAfter))
        }

        override suspend fun sendMessage(
            chatId: String,
            text: String,
            fileIds: List<String>,
            replyToMessageId: Long,
            forwardedMessageIds: List<Long>,
            clientOperationId: String?,
        ): Result<Shared.Message> = unused()

        override suspend fun editMessage(messageId: Long, text: String, fileIds: List<String>): Result<Shared.Message> = unused()
        override suspend fun deleteMessage(messageId: Long): Result<Unit> = unused()
        override suspend fun markAsRead(messageIds: List<Long>): Result<Unit> = unused()
        override suspend fun markChatAsReadUpTo(chatId: String, upToMessageId: Long): Result<Boolean> = unused()
        override suspend fun chatInfo(chatId: String): Result<ChatInfo> = unused()
        override suspend fun pinnedMessages(chatId: String, offset: Int, size: Int): Result<PinnedMessagePage> = unused()
        override suspend fun pinMessage(chatId: String, messageId: Long): Result<Shared.PinnedMessageInfo> = unused()
        override suspend fun unpinMessage(chatId: String, messageId: Long): Result<Unit> = unused()
        override suspend fun unpinAllMessages(chatId: String): Result<Int> = unused()
        override suspend fun attachments(
            chatId: String,
            type: Shared.MessageAttachmentType,
            pageSize: Int,
            fileNameQuery: String,
        ): Result<List<barkfluff.messages.MessagesApiOuterClass.ChatAttachmentInfo>> = unused()

        private fun <T> unused(): Result<T> = Result.failure(UnsupportedOperationException("unused test gateway method"))
    }
}
