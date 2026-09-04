package com.barkfluff.client.send

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import java.io.File
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OutgoingMessageQueueTest {

    private lateinit var application: BarkFluffApplication
    private lateinit var globalParam: GlobalParam
    private lateinit var previousBeacon: String
    private var previousUserId = 0L

    @Before
    fun setUp() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        application = context.applicationContext as BarkFluffApplication
        globalParam = GlobalParam(context)
        previousBeacon = globalParam.socketBeacon
        previousUserId = globalParam.userId
        globalParam.socketBeacon = "outbox-test-${UUID.randomUUID()}"
        globalParam.userId = Long.MAX_VALUE - 7
    }

    @After
    fun tearDown() = runBlocking {
        application.outgoingMessageQueue.cancelAllForCurrentScope()
        globalParam.socketBeacon = previousBeacon
        globalParam.userId = previousUserId
    }

    @Test
    fun textIsPersistedBeforeEnqueueReturns() = runBlocking {
        val operationId = application.outgoingMessageQueue.enqueue(
            SendJob(chatId = "chat", chatTitle = "Chat", text = "offline text", attachments = emptyList())
        ).single()

        val record = application.chatCacheRepository.outgoing(scope(), operationId)

        assertNotNull(record)
        assertEquals("offline text", record!!.text)
        assertTrue(record.state != com.barkfluff.client.cache.OutgoingMessageState.STAGING)
    }

    @Test
    fun voiceFileIsCopiedToOutboxAndCancelDeletesItsCopy() = runBlocking {
        val source = File(application.cacheDir, "outbox-source-${UUID.randomUUID()}.ogg").apply {
            writeBytes(byteArrayOf(1, 2, 3, 4))
        }
        val operationId = application.outgoingMessageQueue.enqueue(
            SendJob(
                chatId = "chat",
                chatTitle = "Chat",
                text = "",
                attachments = listOf(AttachmentSpec.Voice(source))
            )
        ).single()
        source.delete()

        val copiedPath = application.chatCacheRepository.outgoing(scope(), operationId)
            ?.attachments
            ?.single()
            ?.sourcePath
        assertNotNull(copiedPath)
        assertTrue(File(copiedPath!!).isFile)

        application.outgoingMessageQueue.cancel(operationId)

        assertEquals(null, application.chatCacheRepository.outgoing(scope(), operationId))
        assertFalse(File(copiedPath).exists())
    }

    private fun scope() = requireNotNull(CacheScope.from(globalParam))
}
