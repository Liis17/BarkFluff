package com.barkfluff.client

import com.barkfluff.client.domain.model.ChatSummary
import org.junit.Assert.assertEquals
import org.junit.Test

class ChatListIntegrityTest {

    @Test
    fun cachedChatsWithInvalidIdsAreDropped() {
        val invalid = chat(id = "", title = "test test", lastActivityAt = 2L)
        val valid = chat(id = "00000000-0000-0000-0000-000000000001", title = "Alice", lastActivityAt = 1L)

        assertEquals(listOf(valid), validChatSummaries(listOf(invalid, valid)))
    }

    @Test
    fun completeRemoteSnapshotReplacesCachedOnlyChats() {
        val stale = chat(id = "00000000-0000-0000-0000-000000000001", title = "test test", lastActivityAt = 2L)
        val current = chat(id = "00000000-0000-0000-0000-000000000002", title = "nast iv", lastActivityAt = 1L)

        assertEquals(
            listOf(current),
            reconcileChatList(
                cachedChats = listOf(stale),
                remoteChats = listOf(current),
                isCompleteSnapshot = true,
            )
        )
    }

    private fun chat(id: String, title: String, lastActivityAt: Long) = ChatSummary(
        id = id,
        title = title,
        picture = "",
        isGroupChat = false,
        lastMessage = null,
        memberIds = emptyList(),
        countUnread = 0L,
        firstUnreadMessageId = 0L,
        lastActivityAt = lastActivityAt,
    )
}
