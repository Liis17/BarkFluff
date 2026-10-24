package com.barkfluff.client.cache

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class PendingChatReadRangeDaoTest {

    private lateinit var database: ChatCacheDatabase

    @Before
    fun setUp() {
        database = Room.inMemoryDatabaseBuilder(
            InstrumentationRegistry.getInstrumentation().targetContext,
            ChatCacheDatabase::class.java,
        ).allowMainThreadQueries().build()
    }

    @After
    fun tearDown() = database.close()

    @Test
    fun rangesCoalesceMonotonicallyAndRejectStaleCompletionWhileExactReadsStaySeparate() = runBlocking {
        val ranges = database.pendingChatReadRangeDao()
        val exact = database.pendingReadDao()
        exact.insert(PendingMessageReadEntity(SCOPE, 7, CHAT))

        ranges.coalesce(SCOPE, CHAT, 10, 100)
        ranges.updateCursor(SCOPE, CHAT, 10, 8)
        ranges.useLegacyFallback(SCOPE, CHAT, 10)
        ranges.retry(SCOPE, CHAT, 10, 4, 500)

        ranges.coalesce(SCOPE, CHAT, 15, 600)
        val increased = ranges.ready(SCOPE, 600, 10).single()
        assertEquals(15L, increased.upToMessageId)
        assertTrue(increased.legacyFallback)
        assertEquals(0L, increased.cursorBeforeMessageId)
        assertEquals(0, increased.attemptCount)
        assertEquals(500L, increased.nextAttemptAtMillis)

        ranges.updateCursor(SCOPE, CHAT, 15, 13)
        ranges.retry(SCOPE, CHAT, 15, 2, 900)
        ranges.coalesce(SCOPE, CHAT, 12, 700)
        val unchanged = ranges.ready(SCOPE, 900, 10).single()
        assertEquals(15L, unchanged.upToMessageId)
        assertEquals(13L, unchanged.cursorBeforeMessageId)
        assertEquals(2, unchanged.attemptCount)
        assertEquals(700L, unchanged.nextAttemptAtMillis)

        assertEquals(0, ranges.completeThrough(SCOPE, CHAT, 12))
        assertEquals(15L, ranges.ready(SCOPE, 900, 10).single().upToMessageId)
        assertEquals(listOf(7L), exact.ready(SCOPE, 0, 10).map { it.messageId })
        assertEquals(1, ranges.completeThrough(SCOPE, CHAT, 15))
        assertFalse(ranges.ready(SCOPE, Long.MAX_VALUE, 10).isNotEmpty())
    }

    private companion object {
        const val SCOPE = "server|user"
        const val CHAT = "regular-chat"
    }
}
