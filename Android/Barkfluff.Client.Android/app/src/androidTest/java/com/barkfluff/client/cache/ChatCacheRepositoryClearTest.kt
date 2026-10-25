package com.barkfluff.client.cache

import android.content.Context
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.ChatDraftGateway
import com.barkfluff.client.domain.model.ChatDraftData
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.drafts.ChatDraftSyncState
import com.barkfluff.client.drafts.ComposerAttachmentStore
import java.io.File
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.withTimeout
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ChatCacheRepositoryClearTest {

    private lateinit var context: Context
    private lateinit var cache: ChatCacheRepository
    private lateinit var scope: CacheScope
    private lateinit var repositories: MutableList<ChatCacheRepository>
    private var originalSocketBeacon = ""
    private var originalSocketMessages = ""
    private var originalUserId = 0L

    @Before
    fun setUp() {
        context = InstrumentationRegistry.getInstrumentation().targetContext
        val globalParam = GlobalParam(context)
        originalSocketBeacon = globalParam.socketBeacon
        originalSocketMessages = globalParam.socketMessages
        originalUserId = globalParam.userId
        globalParam.socketBeacon = "https://clear-cache-test.local"
        globalParam.userId = 987654321L
        scope = CacheScope("https://clear-cache-test.local|987654321")
        cache = ChatCacheRepository(context)
        repositories = mutableListOf(cache)
    }

    @After
    fun tearDown() = runBlocking {
        repositories.asReversed().forEach { it.clearAll() }
        GlobalParam(context).apply {
            socketBeacon = originalSocketBeacon
            socketMessages = originalSocketMessages
            userId = originalUserId
        }
        Unit
    }

    @Test
    fun clearHistoryCacheKeepsDurableStateAndDatabaseCanBeReopened() = runBlocking {
        cache.saveChatPage(scope, listOf(chat("chat")), totalCount = 1)
        cache.saveMessages(scope, "chat", listOf(message(1)))
        cache.savePrivateMessages(scope, "chat", listOf(encryptedMessage(2)))
        cache.saveSecretMessage(scope, "secret-chat", "secret-1", "Peer", "secret", 1)
        cache.saveChatDraft(scope, draft("chat", revision = "draft-revision"))
        saveComposerAttachment()
        cache.saveOutgoing(scope, outgoing("queued").copy(notificationReadMessageId = 42L))

        cache.clearHistoryCache()

        assertEquals(null, cache.readChatList(scope))
        assertEquals(0, cache.stats().chatCount)
        assertTrue(cache.latestMessages(scope, "chat", 10).isEmpty())
        assertTrue(cache.latestPrivateMessages(scope, "chat", 10).isEmpty())
        assertEquals(OutgoingMessageState.QUEUED, cache.outgoing(scope, "queued")?.state)
        assertEquals(42L, cache.readyPendingChatReadRanges(scope, nowMillis = Long.MAX_VALUE).single().upToMessageId)
        assertEquals(1, cache.readChatDrafts(scope).size)
        assertEquals(1, cache.readComposerAttachments(scope, "chat").size)
        assertEquals(1, cache.latestSecretMessages(scope, "secret-chat", 10).size)

        val reopenedCache = ChatCacheRepository(context)
        repositories += reopenedCache
        reopenedCache.saveMessages(scope, "chat", listOf(message(3)))
        assertEquals(3L, reopenedCache.latestMessages(scope, "chat", 10).single().id)
    }

    @Test
    fun pushedMessageFillsOnlyMissingRowAndServerCopyReplacesIt() = runBlocking {
        val server = message(1).toBuilder()
            .setContent(barkfluff.shared.Shared.MessageContent.newBuilder().setText("server"))
            .build()
        val pushed = { id: Long ->
            com.barkfluff.client.notifications.pushedMessageForCache(
                mapOf(
                    "message_id" to "$id", "sender_id" to "5", "content_type" to "0",
                    "attachment_count" to "0", "message_text" to "push",
                ),
                currentUserId = 7,
                receivedAtMillis = 1_000 * id,
            )!!
        }

        cache.saveMessages(scope, "chat", listOf(server))
        cache.saveMessageIfAbsent(scope, "chat", pushed(1))
        cache.saveMessageIfAbsent(scope, "chat", pushed(2))
        assertEquals("server", cache.cachedMessage(scope, "chat", 1)?.content?.text)
        assertEquals("push", cache.cachedMessage(scope, "chat", 2)?.content?.text)
        // Paging must not trust sparse push rows: the server fills the range instead.
        assertEquals(listOf(1L, 2L), cache.latestMessages(scope, "chat", 10).map { it.id }.sorted())
        assertEquals(emptyList<Long>(), cache.messagesAfter(scope, "chat", 1, 10).map { it.id })
        assertEquals(listOf(1L), cache.messagesBefore(scope, "chat", 3, 10).map { it.id })

        cache.saveMessages(scope, "chat", listOf(server.toBuilder().setId(2).build()))
        assertEquals("server", cache.cachedMessage(scope, "chat", 2)?.content?.text)
    }

    @Test
    fun secretHistoryHandoffIsVisibleOnReturnAndRetryUsesTheSameRow() = runBlocking {
        cache.saveSecretMessage(scope, "secret-chat", "init:durable-envelope", "Peer", "first text", 123000)
        val reopened = ChatCacheRepository(context)
        repositories += reopened
        assertEquals("first text", reopened.latestSecretMessages(scope, "secret-chat", 10).single().plaintext)
        reopened.saveSecretMessage(scope, "secret-chat", "init:durable-envelope", "Peer", "first text", 123000)
        val rows = cache.latestSecretMessages(scope, "secret-chat", 10)
        assertEquals(1, rows.size)
        assertEquals(123000L, rows.single().sentAtMillis)
    }

    @Test
    fun clearSecretHistoryLeavesOtherCachedDataAlone() = runBlocking {
        cache.saveChatPage(scope, listOf(chat("chat")), totalCount = 1)
        cache.saveMessages(scope, "chat", listOf(message(1)))
        cache.saveSecretMessage(scope, "secret-chat", "secret-1", "Peer", "secret", 1)
        cache.saveChatDraft(scope, draft("chat"))
        cache.saveOutgoing(scope, outgoing("queued"))

        cache.clearSecretHistory()

        assertTrue(cache.latestSecretMessages(scope, "secret-chat", 10).isEmpty())
        assertEquals(1, cache.latestMessages(scope, "chat", 10).size)
        assertEquals(1, cache.readChatDrafts(scope).size)
        assertNotNull(cache.outgoing(scope, "queued"))
    }

    @Test
    fun wipeAllClearsDraftRowsAndFlowButKeepsOutbox() = runBlocking {
        cache.saveChatDraft(scope, draft("dirty"))
        cache.saveChatDraft(
            scope,
            draft("delete-pending", revision = "remote-revision", state = ChatDraftSyncState.DELETE_PENDING)
        )
        cache.saveOutgoing(scope, outgoing("queued"))
        val remote = RecordingChatDraftGateway()
        val drafts = ChatDraftRepository(context, cache, remote)
        drafts.loadLocal()

        drafts.wipeAll(scope)

        assertTrue(cache.readChatDrafts(scope).isEmpty())
        assertTrue(drafts.drafts.value.isEmpty())
        assertEquals(setOf("queued"), cache.outgoingOperationIds(scope))
        assertEquals(listOf("delete-pending" to "remote-revision"), remote.deleted)
    }

    @Test
    fun wipeKeepsDraftsAndAttachmentsOfOtherAccountsAndServers() = runBlocking {
        val otherScopes = listOf(CacheScope("https://clear-cache-test.local|123"), CacheScope("https://other.local|987654321"))
        val store = ComposerAttachmentStore(context, cache)
        val selected = store.stageBytes(scope, "chat", byteArrayOf(1), 1, "DOCUMENT")
        val retained = otherScopes.map { other ->
            cache.saveChatDraft(other, draft("chat"))
            store.stageBytes(other, "chat", byteArrayOf(2), 1, "DOCUMENT")
        }
        cache.saveChatDraft(scope, draft("chat"))

        ChatDraftRepository(context, cache, RecordingChatDraftGateway()).wipeAll(scope)
        store.clearScope(scope)

        assertTrue(cache.readChatDrafts(scope).isEmpty())
        assertFalse(File(selected.path).exists())
        otherScopes.zip(retained).forEach { (other, attachment) ->
            assertEquals(listOf(draft("chat")), cache.readChatDrafts(other))
            assertEquals(listOf(attachment), store.restore(other, "chat"))
            assertTrue(File(attachment.path).isFile)
        }
    }

    @Test
    fun wipeWaitsForFirstUpsertAndDeletesItsRevisionBeforeRestore() = runBlocking {
        assertWipeWaitsForUpsert()
    }

    @Test
    fun wipeWaitsForUpsertEvenIfDraftWasEditedDuringSync() = runBlocking {
        assertWipeWaitsForUpsert(editWhileSync = true)
    }

    @Test
    fun wipeWaitsForClearAfterSentUpsert() = runBlocking {
        assertWipeWaitsForUpsert(afterSent = true)
    }

    private suspend fun assertWipeWaitsForUpsert(afterSent: Boolean = false, editWhileSync: Boolean = false) {
        withTimeout(10_000) {
            val started = CompletableDeferred<Unit>()
            val release = CompletableDeferred<Unit>()
            var serverDraft: ChatDraftData? = null
            val remote = object : ChatDraftGateway {
                override suspend fun get(chatId: String) = Result.success(serverDraft)
                override suspend fun upsert(chatId: String, text: String, replyToMessageId: Long): Result<ChatDraftData> {
                    started.complete(Unit)
                    release.await()
                    return Result.success(ChatDraftData(text, replyToMessageId, "first-revision", 1).also { serverDraft = it })
                }
                override suspend fun delete(chatId: String, expectedRevision: String): Result<Boolean> {
                    assertEquals("first-revision", expectedRevision)
                    serverDraft = null
                    return Result.success(true)
                }
            }
            val drafts = ChatDraftRepository(context, cache, remote)
            val draft = drafts.edit("chat", "unsynced", 0)!!
            val flush = async {
                if (afterSent) drafts.clearAfterSent("chat", draft.generation) else drafts.flushAll()
            }
            started.await()
            if (editWhileSync) drafts.edit("chat", "newer unsynced", 0)
            val wipe = async(start = CoroutineStart.UNDISPATCHED) { drafts.wipeAll(scope) }
            // Before the fix wipe completes immediately, although upsert has not returned.
            assertFalse("Wipe must wait for the in-flight upsert", wipe.isCompleted)
            release.complete(Unit)
            flush.await()
            wipe.await()

            assertEquals(null, serverDraft)
            assertTrue(cache.readChatDrafts(scope).isEmpty())
            assertEquals(null, drafts.restore("chat"))
        }
    }

    @Test
    fun wipePreservesAnEditMadeWhileRemoteDeleteIsInFlight() = runBlocking {
        withTimeout(10_000) {
            val started = CompletableDeferred<Unit>()
            val release = CompletableDeferred<Unit>()
            val remote = object : ChatDraftGateway {
                override suspend fun get(chatId: String): Result<ChatDraftData?> = Result.success(null)
                override suspend fun upsert(chatId: String, text: String, replyToMessageId: Long): Result<ChatDraftData> =
                    error("Unexpected upsert")
                override suspend fun delete(chatId: String, expectedRevision: String): Result<Boolean> {
                    started.complete(Unit)
                    release.await()
                    return Result.success(true)
                }
            }
            cache.saveChatDraft(scope, draft("chat", revision = "r1", state = ChatDraftSyncState.SYNCED))
            val drafts = ChatDraftRepository(context, cache, remote)
            val wipe = async { drafts.wipeAll(scope) }
            started.await()
            val newer = drafts.edit("chat", "new edit", 0)!!
            release.complete(Unit)
            wipe.await()

            assertEquals(newer, drafts.drafts.value["chat"])
            assertEquals("new edit", cache.readChatDrafts(scope).single().text)
            assertEquals(newer.generation, cache.readChatDrafts(scope).single().generation)
        }
    }

    @Test
    fun composerClearRemovesComposerFilesAndKeepsOutbox() = runBlocking {
        val store = ComposerAttachmentStore(context, cache)
        val selected = store.stageBytes(scope, "chat", byteArrayOf(1), 1, "DOCUMENT")
        cache.saveOutgoing(scope, outgoing("queued"))

        store.clearScope(scope)

        assertFalse(File(selected.path).exists())
        assertTrue(cache.readComposerAttachments(scope, "chat").isEmpty())
        assertEquals(setOf("queued"), cache.outgoingOperationIds(scope))
    }

    @Test
    fun clearOutgoingKeepReadsPreservesPendingReadsAndSent() = runBlocking {
        cache.saveOutgoing(scope, outgoing("queued").copy(notificationReadMessageId = 42L))
        cache.saveOutgoing(scope, outgoing("sent", state = OutgoingMessageState.SENT))

        cache.clearOutgoingKeepReads(scope)

        assertEquals(null, cache.outgoing(scope, "queued"))
        assertEquals(OutgoingMessageState.SENT, cache.outgoing(scope, "sent")?.state)
        assertEquals(listOf(42L), cache.readyPendingChatReadRanges(scope, nowMillis = Long.MAX_VALUE).map { it.upToMessageId })
    }

    private suspend fun saveComposerAttachment() {
        val file = File(File(context.noBackupFilesDir, "composer"), "preview.txt").apply {
            parentFile?.mkdirs()
            writeText("preview")
        }
        cache.saveComposerAttachments(
            scope,
            "chat",
            listOf(
                ComposerAttachment(
                    attachmentIndex = 0,
                    path = file.absolutePath,
                    kind = "DOCUMENT",
                    fileName = file.name,
                    mimeType = "text/plain",
                    generation = 1,
                    createdAtMillis = 1,
                )
            )
        )
    }

    private fun chat(chatId: String) = ChatSummary(
        id = chatId,
        title = chatId,
        picture = "",
        isGroupChat = false,
        lastMessage = null,
        memberIds = emptyList(),
        countUnread = 0,
        firstUnreadMessageId = 0,
    )

    private fun message(id: Long) = barkfluff.shared.Shared.Message.newBuilder()
        .setId(id)
        .build()

    private fun encryptedMessage(id: Long) = barkfluff.shared.Shared.EncryptedMessage.newBuilder()
        .setId(id)
        .build()

    private fun draft(
        chatId: String,
        revision: String = "",
        state: ChatDraftSyncState = ChatDraftSyncState.DIRTY,
    ) = CachedChatDraft(chatId, "draft", 0L, revision, 1L, state.ordinal)

    private fun outgoing(
        operationId: String,
        state: OutgoingMessageState = OutgoingMessageState.QUEUED,
    ) = OutgoingMessageRecord(
        operationId = operationId,
        batchId = null,
        chatId = "chat",
        chatTitle = "Chat",
        text = "queued",
        replyToMessageId = 0,
        draftGeneration = null,
        sendAsFile = false,
        existingFileIds = emptyList(),
        createdAtMillis = 1,
        state = state,
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
    )

    private class RecordingChatDraftGateway : ChatDraftGateway {
        val deleted = mutableListOf<Pair<String, String>>()

        override suspend fun get(chatId: String): Result<ChatDraftData?> = Result.success(null)

        override suspend fun upsert(chatId: String, text: String, replyToMessageId: Long): Result<ChatDraftData> =
            error("Unexpected draft upsert")

        override suspend fun delete(chatId: String, expectedRevision: String): Result<Boolean> {
            deleted += chatId to expectedRevision
            return Result.success(true)
        }
    }
}
