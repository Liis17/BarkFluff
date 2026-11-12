package com.barkfluff.client

import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.ChatPage
import com.barkfluff.client.domain.model.LastMessageSummary
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatListRealtimeMergeTest {

    @Test
    fun `new message during list load survives remote response and is cached`() = runTest {
        val original = chat(countUnread = 1L, lastMessageId = 4L, lastActivityAt = 40L)
        val gateway = DeferredChatGateway()
        val journal = ChatListEventJournal()
        val cache = MemoryChatListCache(listOf(original))
        val load = async(start = CoroutineStart.UNDISPATCHED) {
            val page = gateway.chats()
            mergeChatListSnapshot(listOf(original), page.chats, true, journal.snapshot(), USER_ID).also {
                cache.save(it)
            }
        }

        journal.record(
            ChatListRealtimeEvent.NewMessage(
                chatId = CHAT_ID,
                senderId = PEER_ID,
                messageId = 5L,
                text = "realtime",
                sentAtMillis = 50L,
            )
        )
        gateway.complete(listOf(chat(countUnread = 1L, lastMessageId = 4L, lastActivityAt = 40L)))

        val merged = load.await().single()
        assertEquals(5L, merged.lastMessage?.id)
        assertEquals(50L, merged.lastActivityAt)
        assertEquals(2L, merged.countUnread)
        assertEquals(listOf(merged), cache.read())
    }

    @Test
    fun `read during list load clears stale unread count in memory and cache`() = runTest {
        val original = chat(countUnread = 3L, lastMessageId = 12L, lastActivityAt = 120L, firstUnreadId = 10L)
        val gateway = DeferredChatGateway()
        val journal = ChatListEventJournal()
        val cache = MemoryChatListCache(listOf(original))
        val load = async(start = CoroutineStart.UNDISPATCHED) {
            val page = gateway.chats()
            mergeChatListSnapshot(listOf(original), page.chats, true, journal.snapshot(), USER_ID).also {
                cache.save(it)
            }
        }

        journal.record(ChatListRealtimeEvent.Read(CHAT_ID, throughMessageId = 12L))
        gateway.complete(listOf(chat(countUnread = 3L, lastMessageId = 12L, lastActivityAt = 120L, firstUnreadId = 10L)))

        val merged = load.await().single()
        assertEquals(0L, merged.countUnread)
        assertEquals(0L, merged.firstUnreadMessageId)
        assertEquals(listOf(merged), cache.read())
    }

    @Test
    fun `incoming message after read during load remains unread`() = runTest {
        val original = chat(countUnread = 4L, lastMessageId = 12L, lastActivityAt = 120L, firstUnreadId = 10L)
        val gateway = DeferredChatGateway()
        val journal = ChatListEventJournal()
        val cache = MemoryChatListCache(listOf(original))
        val load = async(start = CoroutineStart.UNDISPATCHED) {
            val page = gateway.chats()
            mergeChatListSnapshot(listOf(original), page.chats, true, journal.snapshot(), USER_ID).also {
                cache.save(it)
            }
        }

        journal.record(ChatListRealtimeEvent.Read(CHAT_ID, throughMessageId = 12L))
        journal.record(
            ChatListRealtimeEvent.NewMessage(
                chatId = CHAT_ID,
                senderId = PEER_ID,
                messageId = 13L,
                text = "after read",
                sentAtMillis = 130L,
            )
        )
        gateway.complete(listOf(chat(countUnread = 4L, lastMessageId = 12L, lastActivityAt = 120L, firstUnreadId = 10L)))

        val merged = load.await().single()
        assertEquals(13L, merged.lastMessage?.id)
        assertEquals(1L, merged.countUnread)
        assertEquals(listOf(merged), cache.read())
    }

    @Test
    fun `new message raises its chat and private message creates one unread`() {
        val existing = chat(countUnread = 0L, lastMessageId = 4L, lastActivityAt = 40L)
        val older = chat(id = OTHER_CHAT_ID, countUnread = 0L, lastMessageId = 3L, lastActivityAt = 30L)
        val privateChatId = "00000000-0000-0000-0000-000000000003"
        val privateMessage = ChatListRealtimeEvent.PrivateMessage(
            chatId = privateChatId,
            senderId = PEER_ID,
            messageId = 21L,
            sentAtMillis = 130L,
        )

        val merged = mergeChatListSnapshot(
            cachedChats = listOf(existing, older),
            remoteChats = listOf(existing, older),
            isCompleteSnapshot = true,
            realtimeEvents = listOf(
                ChatListRealtimeEvent.NewMessage(CHAT_ID, PEER_ID, 5L, "new", 50L),
                ChatListRealtimeEvent.Read(privateChatId, throughMessageId = 20L),
                privateMessage,
                privateMessage,
            ),
            currentUserId = USER_ID,
        )

        assertEquals(privateChatId, merged.first().id)
        assertEquals(CHAT_ID, merged[1].id)
        assertEquals(5L, merged[1].lastMessage?.id)
        assertEquals(1L, merged.first().countUnread)
        assertEquals(21L, merged.first().lastMessage?.id)
    }

    @Test
    fun `hidden chat during list load is not restored or cached`() = runTest {
        val gateway = DeferredChatGateway()
        val journal = ChatListEventJournal()
        val cache = MemoryChatListCache(listOf(chat()))
        val load = async(start = CoroutineStart.UNDISPATCHED) {
            val page = gateway.chats()
            mergeChatListSnapshot(emptyList(), page.chats, true, journal.snapshot(), USER_ID).also {
                cache.save(it)
            }
        }

        journal.record(ChatListRealtimeEvent.Hidden(CHAT_ID))
        gateway.complete(listOf(chat()))

        assertTrue(load.await().isEmpty())
        assertTrue(cache.read().isEmpty())
    }

    private class DeferredChatGateway {
        private val response = CompletableDeferred<ChatPage>()

        suspend fun chats(): ChatPage = response.await()

        fun complete(chats: List<ChatSummary>) {
            response.complete(ChatPage(chats, chats.size))
        }
    }

    private class MemoryChatListCache(initial: List<ChatSummary>) {
        private var rows = initial

        suspend fun save(chats: List<ChatSummary>) {
            rows = chats
        }

        suspend fun read(): List<ChatSummary> = rows
    }

    private fun chat(
        id: String = CHAT_ID,
        countUnread: Long = 0L,
        lastMessageId: Long? = null,
        lastActivityAt: Long = lastMessageId?.times(10) ?: 0L,
        firstUnreadId: Long = 0L,
    ) = ChatSummary(
        id = id,
        title = "Alice",
        picture = "",
        isGroupChat = false,
        lastMessage = lastMessageId?.let { id ->
            LastMessageSummary(id, PEER_ID, "message-$id", lastActivityAt, listOf(PEER_ID))
        },
        memberIds = listOf(USER_ID, PEER_ID),
        countUnread = countUnread,
        firstUnreadMessageId = firstUnreadId,
        lastActivityAt = lastActivityAt,
    )

    private companion object {
        const val CHAT_ID = "00000000-0000-0000-0000-000000000001"
        const val USER_ID = 1L
        const val PEER_ID = 2L
        const val OTHER_CHAT_ID = "00000000-0000-0000-0000-000000000002"
    }
}
