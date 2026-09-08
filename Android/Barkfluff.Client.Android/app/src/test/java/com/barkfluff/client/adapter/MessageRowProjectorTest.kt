package com.barkfluff.client.adapter

import barkfluff.shared.Shared
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MessageRowProjectorTest {
    private val projector = MessageRowProjector()

    private fun message(id: Long, text: String = id.toString()) = MessageItem(
        messageId = id,
        senderId = 7L,
        text = text,
        timestamp = id * 1_000L,
        attachments = emptyList<Shared.MessageAttachment>(),
        type = MessageType.MESSAGE,
    )

    @Test
    fun projectDeduplicatesMessagesAndKeepsOneFooter() {
        val result = projector.project(listOf(message(1L), message(1L, "new"), message(2L)))

        assertEquals(listOf(1L, 2L, Long.MIN_VALUE), result.map { it.messageId })
        assertEquals("1", result.first().text)
        assertEquals(MessageType.FOOTER, result.last().type)
    }

    @Test
    fun selectionProjectionDoesNotMutateRowsOrStructuralItems() {
        val initial = listOf(message(1L), MessageItem.createDateSeparator("today"), message(2L))
        val selected = projector.withSelection(initial, setOf(2L), enabled = true)

        assertTrue(selected[0].selectionEnabled)
        assertEquals(false, selected[0].isSelected)
        assertTrue(selected[2].isSelected)
        assertEquals(false, initial[0].selectionEnabled)
        assertEquals(false, initial[2].isSelected)
        assertEquals(initial[1], selected[1])
    }
}
