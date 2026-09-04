package com.barkfluff.client.cache

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OutgoingMessageDaoTest {

    private lateinit var database: ChatCacheDatabase

    @Before
    fun setUp() {
        database = Room.inMemoryDatabaseBuilder(
            InstrumentationRegistry.getInstrumentation().targetContext,
            ChatCacheDatabase::class.java
        ).allowMainThreadQueries().build()
    }

    @After
    fun tearDown() {
        database.close()
    }

    @Test
    fun readyHeadsKeepsFifoInsideEachChatWhileAllowingTwoChats() = runBlocking {
        val dao = database.outgoingDao()
        dao.upsertMessage(message("a-first", "chat-a", 10))
        dao.upsertMessage(message("a-second", "chat-a", 20))
        dao.upsertMessage(message("b-first", "chat-b", 15))

        val ready = dao.readyHeads(
            scopeId = SCOPE,
            queuedState = OutgoingMessageState.QUEUED.name,
            sentState = OutgoingMessageState.SENT.name,
            cancelledState = OutgoingMessageState.CANCEL_REQUESTED.name,
            nowMillis = 100,
            limit = 2
        )

        assertEquals(listOf("a-first", "b-first"), ready.map { it.operationId })
    }

    @Test
    fun nextWakeAtSchedulesExpiryOfAbandonedLease() = runBlocking {
        val dao = database.outgoingDao()
        dao.upsertMessage(message("uploading", "chat-a", 10).copy(
            state = OutgoingMessageState.UPLOADING.name,
            leaseExpiresAtMillis = 12_345
        ))

        val wakeAt = dao.nextWakeAt(
            scopeId = SCOPE,
            queuedState = OutgoingMessageState.QUEUED.name,
            preparingState = OutgoingMessageState.PREPARING.name,
            uploadingState = OutgoingMessageState.UPLOADING.name,
            sendingState = OutgoingMessageState.SENDING.name
        )

        assertEquals(12_345L, wakeAt)
    }

    private fun message(operationId: String, chatId: String, createdAtMillis: Long) = OutgoingMessageEntity(
        scopeId = SCOPE,
        operationId = operationId,
        batchId = null,
        chatId = chatId,
        chatTitle = chatId,
        text = "text",
        replyToMessageId = 0,
        draftGeneration = null,
        sendAsFile = false,
        existingFileIds = "",
        createdAtMillis = createdAtMillis,
        state = OutgoingMessageState.QUEUED.name,
        progress = 0,
        attemptCount = 0,
        nextAttemptAtMillis = 0,
        lastFailureCategory = null,
        lastFailureDetail = null,
        leaseOwner = null,
        leaseExpiresAtMillis = 0,
        serverMessageId = 0,
        serverMessagePayload = null
    )

    private companion object {
        const val SCOPE = "server|user"
    }
}
