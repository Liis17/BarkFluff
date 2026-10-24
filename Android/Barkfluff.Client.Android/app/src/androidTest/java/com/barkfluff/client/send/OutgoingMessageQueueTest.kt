package com.barkfluff.client.send

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.work.WorkManager
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.OutgoingFailureCategory
import com.barkfluff.client.cache.OutgoingMessageRecord
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcApiTransport
import com.barkfluff.client.grpc.TokenCoordinator
import com.barkfluff.client.grpc.TokenValidity
import com.barkfluff.client.repository.ChatRepository
import barkfluff.shared.Shared
import com.google.protobuf.Timestamp
import java.io.File
import java.io.IOException
import java.util.UUID
import java.security.MessageDigest
import io.grpc.Metadata
import io.grpc.Status
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
    fun draftHandoffHoldsFromAcceptanceThroughDeliveryAndEndsOnCancel() = runBlocking {
        val queue = application.outgoingMessageQueue
        val operationId = queue.enqueue(
            SendJob(chatId = "chat", chatTitle = "Chat", text = "offline text", attachments = emptyList(), draftGeneration = 7L)
        ).single()

        assertTrue(queue.hasDraftHandoff("chat", 7L, "offline text", 0L))
        assertFalse(queue.hasDraftHandoff("chat", 7L, "newer text", 0L))
        assertFalse(queue.hasDraftHandoff("chat", 8L, "offline text", 0L))

        // The worker may deliver while the chat is closed; the unacknowledged draft must still be recognised.
        val record = application.chatCacheRepository.outgoing(scope(), operationId)!!
        application.chatCacheRepository.saveOutgoing(
            scope(), record.copy(state = com.barkfluff.client.cache.OutgoingMessageState.SENT)
        )
        assertTrue(queue.hasDraftHandoff("chat", 7L, "offline text", 0L))

        queue.cancel(operationId)
        assertFalse(queue.hasDraftHandoff("chat", 7L, "offline text", 0L))
    }

    @Test
    fun notificationReplyAndReadAreDurableBeforeAcceptanceAndCancelOnlyDeletesSend() = runBlocking {
        val operationId = enqueueReply()
        val record = application.chatCacheRepository.outgoing(scope(), operationId)!!
        assertEquals(0L, record.replyToMessageId)
        assertEquals(null, record.draftGeneration)
        assertEquals("reply", record.text)
        assertEquals(42L, record.notificationReadMessageId)
        assertEquals(42L, application.chatCacheRepository
            .readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single().upToMessageId)

        application.outgoingMessageQueue.cancel(operationId)

        assertEquals(null, application.chatCacheRepository.outgoing(scope(), operationId))
        assertEquals(42L, application.chatCacheRepository
            .readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single().upToMessageId)
    }

    @Test
    fun staleNotificationIsRejectedBeforeEitherJournalIsWritten() = runBlocking {
        val result = runCatching {
            application.outgoingMessageQueue.enqueue(SendJob("chat", "Chat", "reply", emptyList(),
                notificationReply = NotificationReplyMetadata("different-account", 42)))
        }
        assertTrue(result.isFailure)
        assertTrue(application.chatCacheRepository.outgoingOperationIds(scope()).isEmpty())
        assertTrue(application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).isEmpty())
        assertTrue(application.chatCacheRepository.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun readRetriesIndependentlyAfterSendWasCancelled() = runBlocking {
        val operationId = enqueueReply()
        application.outgoingMessageQueue.cancel(operationId)
        application.outgoingMessageQueue.drainPendingChatReadRanges(
            scope(), Long.MAX_VALUE,
            markRange = { _, _ ->
            Result.failure(IOException("offline"))
            },
        )
        assertTrue(application.chatCacheRepository.outgoingOperationIds(scope()).isEmpty())
        val pending = application.chatCacheRepository.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single()
        assertEquals(42L, pending.upToMessageId)
        assertTrue(pending.attemptCount > 0)
        assertTrue(pending.nextAttemptAtMillis > System.currentTimeMillis())

        application.outgoingMessageQueue.drainPendingChatReadRanges(
            scope(), Long.MAX_VALUE,
            markRange = { chatId, cutoff ->
                assertEquals("chat", chatId)
                assertEquals(42L, cutoff)
                Result.success(false)
            },
        )
        assertTrue(application.chatCacheRepository.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun rangeWorkerUsesOnlyUnimplementedForFallbackAndHonorsFiveCallBudget() = runBlocking {
        val cache = application.chatCacheRepository
        val scope = scope()
        cache.enqueueChatReadRange(scope, "chat", 100)
        var rangeCalls = 0
        var infoCalls = 0
        val queue = queueWith(TokenValidity.VALID)

        val used = queue.drainPendingChatReadRanges(
            scope, System.currentTimeMillis(),
            markRange = { _, _ -> rangeCalls++; Result.success(true) },
            getChatInfo = { infoCalls++; Result.failure(AssertionError("range API must be used")) },
        )

        assertEquals(5, used)
        assertEquals(5, rangeCalls)
        assertEquals(0, infoCalls)
        assertEquals(1, cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).size)
    }

    @Test
    fun permanentRangeRejectionRemovesOnlyTheRejectedCutoffWhenANewerOneArrives() = runBlocking {
        val cache = application.chatCacheRepository
        val scope = scope()
        cache.enqueueChatReadRange(scope, "chat", 100)
        val queue = queueWith(TokenValidity.VALID)

        queue.drainPendingChatReadRanges(
            scope,
            System.currentTimeMillis(),
            markRange = { chatId, _ ->
                cache.enqueueChatReadRange(scope, chatId, 120)
                Result.failure(permanentRangeError("604DD334-0484-4C6B-8113-354B9D2FDF2A"))
            },
        )

        val pending = cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).single()
        assertEquals(120L, pending.upToMessageId)
    }

    @Test
    fun transientRangeFailureIsDeferredInsteadOfFallingBackOrBeingRemoved() = runBlocking {
        val cache = application.chatCacheRepository
        val scope = scope()
        cache.enqueueChatReadRange(scope, "chat", 100)

        queueWith(TokenValidity.VALID).drainPendingChatReadRanges(
            scope,
            System.currentTimeMillis(),
            markRange = { _, _ -> Result.failure(IOException("offline")) },
        )

        val pending = cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).single()
        assertEquals(100L, pending.upToMessageId)
        assertTrue(pending.attemptCount > 0)
        assertTrue(pending.nextAttemptAtMillis > System.currentTimeMillis())
    }

    @Test
    fun deletedLegacyCursorResetsAndDefersTheRangeForRetry() = runBlocking {
        val cache = application.chatCacheRepository
        val scope = scope()
        cache.enqueueChatReadRange(scope, "chat", 100)
        cache.useLegacyReadRangeFallback(scope, "chat", 100)
        cache.updatePendingChatReadRangeCursor(scope, "chat", 100, 55)

        val used = queueWith(TokenValidity.VALID).drainPendingChatReadRanges(
            scope,
            System.currentTimeMillis(),
            getChatInfo = { Result.success(ChatRepository.ChatInfo("chat", "Chat", "", false, 200, 50, 2, emptyList())) },
            loadPage = { _, cursor ->
                assertEquals(55L, cursor)
                Result.failure(permanentRangeError("C0EEF1D9-BE99-4645-9EBD-95FF36A2BF45"))
            },
        )

        assertEquals(2, used)
        val pending = cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).single()
        assertTrue(pending.legacyFallback)
        assertEquals(0L, pending.cursorBeforeMessageId)
        assertTrue(pending.attemptCount > 0)
    }

    @Test
    fun legacyFallbackContinuesAfterPageCrossesFirstUnreadIdAndPersistsCursor() = runBlocking {
        val cache = application.chatCacheRepository
        val scope = scope()
        cache.enqueueChatReadRange(scope, "chat", 100)
        val queue = queueWith(TokenValidity.VALID)
        var rangeCalls = 0
        var pageCalls = 0
        val markedBatches = mutableListOf<List<Long>>()

        val used = queue.drainPendingChatReadRanges(
            scope, System.currentTimeMillis(),
            markRange = { _, _ ->
                rangeCalls++
                Result.failure(Status.UNIMPLEMENTED.asRuntimeException())
            },
            getChatInfo = { chatId ->
                assertEquals("chat", chatId)
                Result.success(ChatRepository.ChatInfo("chat", "Chat", "", true, 200, 50, 2, emptyList()))
            },
            loadPage = { _, cursor ->
                pageCalls++
                if (pageCalls == 1) assertEquals(0L, cursor)
                else assertEquals(60L, cursor)
                val page = if (cursor == 0L) {
                    buildList {
                        add(message(60, 6_000))
                        repeat(49) { index -> add(message(1_000L + index, 6_100L + index * 100)) }
                    }
                } else {
                    buildList {
                        add(message(999, 0))
                        add(message(55, 1_000))
                        add(message(60, 2_000))
                        repeat(47) { index -> add(message(2_000L + index, 2_100L + index * 50)) }
                        add(message(60, 6_000))
                    }
                }
                Result.success(page)
            },
            markExact = { ids -> markedBatches += ids; Result.success(Unit) },
        )

        assertEquals(5, used)
        assertEquals(1, rangeCalls)
        assertEquals(2, pageCalls)
        assertEquals(listOf(listOf(60L)), markedBatches)
        val pending = cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).single()
        assertTrue(pending.legacyFallback)
        assertEquals(60L, pending.cursorBeforeMessageId)
        assertEquals(0, pending.attemptCount)

        val resumedCalls = queue.drainPendingChatReadRanges(
            scope, System.currentTimeMillis(),
            markRange = { _, _ -> Result.failure(AssertionError("legacy fallback must remain selected")) },
            getChatInfo = { Result.success(ChatRepository.ChatInfo("chat", "Chat", "", true, 200, 50, 2, emptyList())) },
            loadPage = { _, cursor ->
                when (cursor) {
                    60L -> Result.success(buildList {
                        add(message(999, 0))
                        add(message(55, 1_000))
                        add(message(60, 2_000))
                        repeat(47) { index -> add(message(2_000L + index, 2_100L + index * 50)) }
                        add(message(60, 6_000))
                    })
                    999L -> Result.success(emptyList())
                    else -> throw AssertionError("Unexpected legacy cursor: $cursor")
                }
            },
            markExact = { ids -> markedBatches += ids; Result.success(Unit) },
        )

        assertEquals(4, resumedCalls)
        assertEquals(listOf(listOf(60L), listOf(55L)), markedBatches)
        assertTrue(cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun legacyDeletedCursorResetsAndRetriesInsteadOfCompleting() = runBlocking {
        val cache = application.chatCacheRepository
        val scope = scope()
        cache.enqueueChatReadRange(scope, "chat", 100)
        cache.useLegacyReadRangeFallback(scope, "chat", 100)
        cache.updatePendingChatReadRangeCursor(scope, "chat", 100, 77)
        val trailerKey = Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
        val trailers = Metadata().apply {
            put(trailerKey, "C0EEF1D9-BE99-4645-9EBD-95FF36A2BF45")
        }

        queueWith(TokenValidity.VALID).drainPendingChatReadRanges(
            scope, System.currentTimeMillis(),
            getChatInfo = { Result.success(ChatRepository.ChatInfo("chat", "Chat", "", false, 100, 50, 1, emptyList())) },
            loadPage = { _, _ -> Result.failure(Status.NOT_FOUND.asRuntimeException(trailers)) },
        )

        val pending = cache.readyPendingChatReadRanges(scope, Long.MAX_VALUE).single()
        assertEquals(0L, pending.cursorBeforeMessageId)
        assertEquals(1, pending.attemptCount)
        assertTrue(pending.nextAttemptAtMillis > System.currentTimeMillis())
    }

    @Test
    fun cancellingPendingSendsSchedulesRetainedReadWithoutAnotherWake() = runBlocking {
        val cache = application.chatCacheRepository
        // A future attempt cannot be consumed by a worker racing this assertion.
        val operationId = saveQueuedText("cancel with read", notificationReadMessageId = 42L)
        cache.retryPendingChatReadRange(scope(), "chat", 42L, 1, System.currentTimeMillis() + 3_600_000)

        application.outgoingMessageQueue.cancelPendingOutgoing()

        assertEquals(null, cache.outgoing(scope(), operationId))
        assertEquals(42L, cache.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single().upToMessageId)
        val scopeHash = MessageDigest.getInstance("SHA-256").digest(scope().id.toByteArray())
            .joinToString("") { "%02x".format(it) }
        val work = WorkManager.getInstance(application)
            .getWorkInfosForUniqueWork("outgoing-retry-$scopeHash").get()
        assertTrue("Retained reads must have scheduled work", work.any { !it.state.isFinished })
    }

    @Test
    fun notificationReplyProcessesThroughSentWhileReadRetriesIndependentlyWithoutResending() = runBlocking {
        val cache = application.chatCacheRepository
        val operationId = saveQueuedText("notification reply", notificationReadMessageId = 42L)
        var sends = 0
        val reply = message(9001, System.currentTimeMillis())
        val queue = queueWith(
            TokenValidity.VALID,
            sendMessageCall = { chatId, text, fileIds, replyToMessageId, operation ->
                assertEquals("chat", chatId)
                assertEquals("notification reply", text)
                assertTrue(fileIds.isEmpty())
                assertEquals(0L, replyToMessageId)
                assertEquals(operationId, operation)
                sends++
                Result.success(reply)
            },
            markChatReadRangeCall = { _, _ -> Result.failure(IOException("offline")) },
        )

        queue.processReady()

        val sent = cache.outgoing(scope(), operationId)!!
        assertEquals(OutgoingMessageState.SENT, sent.state)
        assertEquals(42L, sent.notificationReadMessageId)
        assertEquals(1, sends)
        val deferredRead = cache.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single()
        assertEquals(42L, deferredRead.upToMessageId)
        assertTrue(deferredRead.attemptCount > 0)
        assertTrue(deferredRead.nextAttemptAtMillis > System.currentTimeMillis())

        cache.retryPendingChatReadRange(scope(), "chat", 42L, 0, System.currentTimeMillis())
        queueWith(TokenValidity.VALID,
            sendMessageCall = { _, _, _, _, _ -> sends++; Result.success(reply) },
            markChatReadRangeCall = { _, cutoff ->
                assertEquals(42L, cutoff)
                Result.success(false)
            },
        ).processReady()

        assertEquals(1, sends)
        assertTrue(cache.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).isEmpty())

        var metadataRecord = sent
        for (state in listOf(
            OutgoingMessageState.PREPARING,
            OutgoingMessageState.SENDING,
            OutgoingMessageState.FAILED,
            OutgoingMessageState.CANCEL_REQUESTED,
        )) {
            cache.saveOutgoing(scope(), metadataRecord.copy(state = state))
            metadataRecord = cache.outgoing(scope(), operationId)!!
            assertEquals(42L, metadataRecord.notificationReadMessageId)
            assertTrue(cache.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).isEmpty())
        }
    }

    private suspend fun enqueueReply() = application.outgoingMessageQueue.enqueue(
        SendJob("chat", "Chat", "reply", emptyList(),
            notificationReply = NotificationReplyMetadata(scope().id, 42))
    ).single()

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

    @Test
    fun unreachableIdentityKeepsTheSendQueuedForRetryInsteadOfFailingIt() = runBlocking {
        val operationId = saveQueuedText("sent right after the network returned")

        queueWith(TokenValidity.UNAVAILABLE).processReady()

        val record = application.chatCacheRepository.outgoing(scope(), operationId)!!
        assertEquals(OutgoingMessageState.QUEUED, record.state)
        assertEquals(OutgoingFailureCategory.NETWORK, record.failureCategory)
        assertEquals(1, record.attemptCount)
        assertTrue(record.nextAttemptAtMillis > System.currentTimeMillis())
    }

    @Test
    fun rejectedRefreshTokenStillFailsTheSendForTheUser() = runBlocking {
        val operationId = saveQueuedText("session was revoked")

        queueWith(TokenValidity.REJECTED).processReady()

        val record = application.chatCacheRepository.outgoing(scope(), operationId)!!
        assertEquals(OutgoingMessageState.FAILED, record.state)
        assertEquals(OutgoingFailureCategory.AUTH_REQUIRED, record.failureCategory)
    }

    /** Writes a QUEUED row directly: unlike enqueue() it does not wake the real worker that could race the test. */
    private suspend fun saveQueuedText(text: String, notificationReadMessageId: Long? = null): String {
        val operationId = UUID.randomUUID().toString()
        application.chatCacheRepository.saveOutgoing(scope(), OutgoingMessageRecord(
            operationId = operationId,
            batchId = null,
            chatId = "chat",
            chatTitle = "Chat",
            text = text,
            replyToMessageId = 0L,
            draftGeneration = null,
            sendAsFile = false,
            existingFileIds = emptyList(),
            createdAtMillis = System.currentTimeMillis(),
            state = OutgoingMessageState.QUEUED,
            progress = 0,
            attemptCount = 0,
            nextAttemptAtMillis = 0,
            failureCategory = null,
            failureDetail = null,
            leaseOwner = null,
            leaseExpiresAtMillis = 0,
            serverMessageId = 0,
            serverMessagePayload = null,
            attachments = emptyList(),
            notificationReadMessageId = notificationReadMessageId,
        ))
        return operationId
    }

    private fun queueWith(
        validity: TokenValidity,
        sendMessageCall: suspend (String, String, List<String>, Long, String) -> Result<Shared.Message> =
            { _, _, _, _, _ -> Result.failure(IOException("send callback not configured")) },
        markChatReadRangeCall: suspend (String, Long) -> Result<Boolean> =
            { _, _ -> Result.failure(IOException("read callback not configured")) },
    ): OutgoingMessageQueue {
        val context = application.applicationContext
        val coordinator = object : TokenCoordinator {
            override suspend fun validity(forceRefresh: Boolean) = validity
        }
        return OutgoingMessageQueue(
            context, application.chatCacheRepository, ChatRepository(context, GrpcApiTransport(context)), coordinator,
            sendMessageCall = sendMessageCall,
            markChatReadRangeCall = markChatReadRangeCall,
        )
    }

    private fun scope() = requireNotNull(CacheScope.from(globalParam))

    private fun permanentRangeError(code: String) = Status.FAILED_PRECONDITION.asRuntimeException(
        Metadata().apply { put(Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER), code) },
    )

    private fun message(id: Long, timestampMillis: Long) = Shared.Message.newBuilder()
        .setId(id)
        .setSentAt(Timestamp.newBuilder()
            .setSeconds(timestampMillis / 1_000L)
            .setNanos(((timestampMillis % 1_000L) * 1_000_000L).toInt())
            .build())
        .build()
}
