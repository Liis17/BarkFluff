package com.barkfluff.client

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatStateModulesTest {
    private val selection = SelectionReducer()
    private val composer = ChatComposer()

    @Test
    fun selectionToggleIsImmutableAndClosesWhenLastItemRemoved() {
        val initial = SelectionState()
        val selected = selection.toggle(initial, 42L)

        assertTrue(selected.isActive)
        assertEquals(setOf(42L), selected.selectedMessageIds)
        assertEquals(emptySet<Long>(), initial.selectedMessageIds)
        assertEquals(SelectionState(), selection.toggle(selected, 42L))
    }

    @Test
    fun missingMessagesAreDroppedFromRestoredSelection() {
        val state = SelectionState(setOf(1L, 2L, 3L), isActive = true)
        val restored = selection.removeMissing(state, setOf(2L, 4L))

        assertEquals(setOf(2L), restored.selectedMessageIds)
        assertTrue(restored.isActive)
        assertEquals(SelectionState(), selection.removeMissing(state, emptySet()))
    }

    @Test
    fun replyAndEditAreMutuallyExclusive() {
        val reply = PendingReply(1L, 2L, "Alice", "hello", emptyList())
        val edit = PendingEdit(3L, "edited", listOf("file"))

        val replying = composer.beginReply(ComposerState(), reply)
        assertEquals(reply, replying.pendingReply)
        assertFalse(replying.pendingEdit != null)

        val editing = composer.beginEdit(replying, edit)
        assertEquals(edit, editing.pendingEdit)
        assertEquals("edited", editing.text)
        assertFalse(editing.pendingReply != null)
    }

    @Test
    fun presenceIgnoresOwnUserAndOtherChatAndExpiresTyping() {
        val presence = ChatPresence(currentUserId = 10L)
        val initial = PresenceState()
        assertEquals(initial, presence.online(initial, 10L, true))

        val online = presence.online(initial, 11L, true)
        assertEquals(setOf(11L), online.onlineUserIds)
        val wrongChat = presence.typing(online, "chat-a", "chat-b", 11L, true, 100L)
        assertEquals(emptySet<Long>(), wrongChat.typingUserIds)

        val typing = presence.typing(online, "chat-a", "chat-a", 11L, true, 100L)
        assertEquals(setOf(11L), typing.typingUserIds)
        assertEquals(emptySet<Long>(), presence.expire(typing, 5_100L).typingUserIds)
    }
}
