package com.barkfluff.client.drafts

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatDraftJournalTest {

    @Test
    fun `editing a draft creates a dirty active entry`() {
        val entry = ChatDraftJournal.edit(null, "Привет", 42L)

        assertEquals("Привет", entry.text)
        assertEquals(42L, entry.replyToMessageId)
        assertEquals(ChatDraftSyncState.DIRTY, entry.syncState)
        assertTrue(entry.isActive)
    }

    @Test
    fun `clearing the last draft value creates a delete tombstone`() {
        val edited = ChatDraftJournal.edit(null, "Привет", 0L)
        val cleared = ChatDraftJournal.edit(edited, "", 0L)

        assertEquals(ChatDraftSyncState.DELETE_PENDING, cleared.syncState)
        assertFalse(cleared.isActive)
        assertTrue(cleared.generation > edited.generation)
    }

    @Test
    fun `stale sync response cannot overwrite a newer edit`() {
        val first = ChatDraftJournal.edit(null, "Первый", 0L)
        val second = ChatDraftJournal.edit(first, "Второй", 0L)

        val result = ChatDraftJournal.markSynced(second, first.generation, "old-revision")

        assertEquals(second, result)
    }

    @Test
    fun `matching sync response records server revision`() {
        val entry = ChatDraftJournal.edit(null, "Черновик", 0L)

        val result = ChatDraftJournal.markSynced(entry, entry.generation, "server-revision")

        assertEquals(ChatDraftSyncState.SYNCED, result.syncState)
        assertEquals("server-revision", result.revision)
    }

    @Test
    fun `deleting only removes the sent generation`() {
        val sent = ChatDraftJournal.markSynced(ChatDraftJournal.edit(null, "Первый", 0L), 1L, "r1")
        val newer = ChatDraftJournal.edit(sent, "Второй", 0L)

        val result = ChatDraftJournal.removeIfGenerationMatches(newer, sent.generation)

        assertEquals(newer, result)
    }
}
