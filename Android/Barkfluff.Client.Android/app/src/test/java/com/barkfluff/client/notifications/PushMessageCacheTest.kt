package com.barkfluff.client.notifications

import barkfluff.shared.Shared
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PushMessageCacheTest {

    private val payload = mapOf(
        "type" to "new_message",
        "chat_id" to "chat-1",
        "sender_id" to "42",
        "message_id" to "1001",
        "content_type" to "0",
        "attachment_count" to "0",
        "message_text" to "привет",
    )

    @Test
    fun plainTextPushBecomesCacheRow() {
        val message = pushedMessageForCache(payload, currentUserId = 7, receivedAtMillis = 1_700_000_000_123)!!

        assertEquals(1001L, message.id)
        assertEquals(42L, message.senderId)
        assertEquals("привет", message.content.text)
        assertEquals(Shared.MessageContentType.GENERIC, message.type)
        assertEquals(1_700_000_000L, message.sentAt.seconds)
        assertEquals(123_000_000, message.sentAt.nanos)
        assertTrue(message.isPushPreview())
        assertFalse(message.toBuilder().setClientOperationId("").build().isPushPreview())
    }

    @Test
    fun textAtServerLimitIsKeptButTruncatedTextIsSkipped() {
        val full = "a".repeat(100)
        val truncated = "a".repeat(100) + "..."

        assertEquals(full, pushedMessageForCache(payload + ("message_text" to full), 7, 1_000)?.content?.text)
        assertNull(pushedMessageForCache(payload + ("message_text" to truncated), 7, 1_000))
    }

    @Test
    fun attachmentsOwnAndIncompletePayloadsAreSkipped() {
        assertNull(pushedMessageForCache(payload + ("content_type" to "1"), 7, 1_000))
        assertNull(pushedMessageForCache(payload + ("attachment_count" to "2"), 7, 1_000))
        assertNull(pushedMessageForCache(payload, currentUserId = 42, receivedAtMillis = 1_000))
        assertNull(pushedMessageForCache(payload - "message_id", 7, 1_000))
        assertNull(pushedMessageForCache(payload + ("message_text" to "  "), 7, 1_000))
        assertNull(pushedMessageForCache(payload - "attachment_count", 7, 1_000))
    }
}
