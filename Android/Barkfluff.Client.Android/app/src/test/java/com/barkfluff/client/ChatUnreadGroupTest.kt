package com.barkfluff.client

import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.adapter.assignUnreadGroupPositions
import com.barkfluff.client.domain.model.ChatSummary
import org.junit.Assert.assertEquals
import org.junit.Test

class ChatUnreadGroupTest {

    @Test
    fun `consecutive unread rows receive one group position and read rows split groups`() {
        val grouped = assignUnreadGroupPositions(
            listOf(item("first", unread = true), item("middle", unread = true), item("last", unread = true),
                item("read", unread = false), item("single", unread = true))
        )

        assertEquals(
            listOf(
                ChatAdapter.UnreadGroupPosition.FIRST,
                ChatAdapter.UnreadGroupPosition.MIDDLE,
                ChatAdapter.UnreadGroupPosition.LAST,
                ChatAdapter.UnreadGroupPosition.NONE,
                ChatAdapter.UnreadGroupPosition.SINGLE,
            ),
            grouped.map { it.unreadGroupPosition },
        )
    }

    @Test
    fun `removing a group member recomputes remaining outer corners`() {
        val original = assignUnreadGroupPositions(listOf(item("a", true), item("b", true), item("c", true)))
        val afterRemoval = assignUnreadGroupPositions(original.drop(1))

        assertEquals(ChatAdapter.UnreadGroupPosition.FIRST, original.first().unreadGroupPosition)
        assertEquals(ChatAdapter.UnreadGroupPosition.MIDDLE, original[1].unreadGroupPosition)
        assertEquals(ChatAdapter.UnreadGroupPosition.FIRST, afterRemoval.first().unreadGroupPosition)
        assertEquals(ChatAdapter.UnreadGroupPosition.LAST, afterRemoval.last().unreadGroupPosition)
    }

    private fun item(id: String, unread: Boolean) = ChatAdapter.ChatDisplayItem(
        chatData = ChatSummary(
            id = id,
            title = "",
            picture = "",
            isGroupChat = false,
            lastMessage = null,
            memberIds = emptyList(),
            countUnread = if (unread) 1L else 0L,
            firstUnreadMessageId = 0L,
        ),
        displayTitle = id,
        displayAvatarFileId = null,
    )
}
