package com.barkfluff.client.utils

import barkfluff.shared.Shared
import com.barkfluff.client.domain.model.ChatSummary
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SavedMessagesTest {
    private fun chat(members: List<Long>) = ChatSummary(
        id = "self", title = "Name", picture = "", isGroupChat = false,
        lastMessage = null, memberIds = members, countUnread = 0, firstUnreadMessageId = 0,
    )

    @Test fun `only known own membership identifies saved messages`() {
        assertTrue(chat(listOf(7)).isSavedMessages(7))
        assertTrue(chat(listOf(7, 7)).isSavedMessages(7))
        assertFalse(chat(emptyList()).isSavedMessages(7))
        assertFalse(chat(listOf(7, 8)).isSavedMessages(7))
        assertFalse(chat(listOf(8)).isSavedMessages(7))
        assertFalse(chat(listOf(0)).isSavedMessages(0))
    }

    @Test fun `groups and private chats keep their own presentation`() {
        val self = chat(listOf(7))
        assertFalse(self.copy(isGroupChat = true).isSavedMessages(7))
        assertFalse(self.copy(chatType = Shared.ChatType.CHAT_TYPE_PRIVATE).isSavedMessages(7))
    }
}
