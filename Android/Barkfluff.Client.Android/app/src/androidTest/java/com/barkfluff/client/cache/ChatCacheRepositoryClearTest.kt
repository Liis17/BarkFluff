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
        cache.saveOutgoing(scope, outgoing("queued"), notificationReadMessageId = 42L)

        cache.clearHistoryCache()

        assertEquals(null, cache.readChatList(scope))
        assertEquals(0, cache.stats().chatCount)
        assertTrue(cache.latestMessages(scope, "chat", 10).isEmpty())
        assertTrue(cache.latestPrivateMessages(scope, "chat", 10).isEmpty())
        assertEquals(OutgoingMessageState.QUEUED, cache.outgoing(scope, "queued")?.state)
        assertEquals(42L, cache.readyPendingReads(scope, nowMillis = 0).single().messageId)
        assertEquals(1, cache.readChatDrafts(scope).size)
        assertEquals(1, cache.readComposerAttachments(scope, "chat").size)
        assertEquals(1, cache.latestSecretMessages(scope, "secret-chat", 10).size)

        val reopenedCache = ChatCacheRepository(context)
        repositories += reopenedCache
        reopenedCache.saveMessages(scope, "chat", listOf(message(3)))
        assertEquals(3L, reopenedCache.latestMessages(scope, "chat", 10).single().id)
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

        drafts.wipeAll()

        assertTrue(cache.readChatDrafts(scope).isEmpty())
        assertTrue(drafts.drafts.value.isEmpty())
        assertEquals(setOf("queued"), cache.outgoingOperationIds(scope))
        assertEquals(listOf("delete-pending" to "remote-revision"), remote.deleted)
    }

    @Test
    fun composerClearRemovesComposerFilesAndKeepsOutbox() = runBlocking {
        saveComposerAttachment()
        cache.saveOutgoing(scope, outgoing("queued"))
        val composerDirectory = File(context.noBackupFilesDir, "composer")
        val store = ComposerAttachmentStore(context, cache)

        store.clearAll()

        assertFalse(composerDirectory.exists())
        assertEquals(setOf("queued"), cache.outgoingOperationIds(scope))
    }

    @Test
    fun clearOutgoingKeepReadsPreservesPendingReadsAndSent() = runBlocking {
        cache.saveOutgoing(scope, outgoing("queued"), notificationReadMessageId = 42L)
        cache.saveOutgoing(scope, outgoing("sent", state = OutgoingMessageState.SENT))

        cache.clearOutgoingKeepReads(scope)

        assertEquals(null, cache.outgoing(scope, "queued"))
        assertEquals(OutgoingMessageState.SENT, cache.outgoing(scope, "sent")?.state)
        assertEquals(listOf(42L), cache.readyPendingReads(scope, nowMillis = 0).map { it.messageId })
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
