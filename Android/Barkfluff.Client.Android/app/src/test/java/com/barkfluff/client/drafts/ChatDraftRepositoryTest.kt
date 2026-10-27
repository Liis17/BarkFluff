package com.barkfluff.client.drafts

import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDraft
import com.barkfluff.client.domain.gateway.ChatDraftGateway
import com.barkfluff.client.domain.model.ChatDraftData
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ChatDraftRepositoryTest {

    @Test
    fun `delete conflict retries when server still has the sent text`() = runTest {
        val cache = FakeCacheStore()
        val remote = FakeGateway(
            deleteResults = listOf(false, true),
            getResults = listOf(draft("отправлено", "r2"), null),
        )
        val repository = repository(cache, remote)
        val sent = repository.edit(CHAT_ID, "отправлено", 0L)!!

        repository.clearAfterSent(CHAT_ID, sent.generation)

        assertEquals(listOf("r1", "r2"), remote.deletedRevisions)
        assertNull(repository(cache, remote).restore(CHAT_ID))
    }

    @Test
    fun `delete conflict preserves a genuinely newer server draft across restart`() = runTest {
        val cache = FakeCacheStore()
        val newer = draft("новый текст", "r2")
        val remote = FakeGateway(
            deleteResults = listOf(false),
            getResults = listOf(newer, newer),
        )
        val repository = repository(cache, remote)
        val sent = repository.edit(CHAT_ID, "отправлено", 0L)!!

        repository.clearAfterSent(CHAT_ID, sent.generation)

        val restored = repository(cache, remote).restore(CHAT_ID)
        assertEquals("новый текст", restored?.text)
        assertEquals("r2", restored?.revision)
        assertEquals(listOf("r1"), remote.deletedRevisions)
    }

    private fun repository(cache: FakeCacheStore, remote: FakeGateway) =
        ChatDraftRepository({ SCOPE }, cache, remote)

    private class FakeCacheStore : ChatDraftCacheStore {
        private val drafts = linkedMapOf<String, CachedChatDraft>()

        override suspend fun readChatDrafts(scope: CacheScope): List<CachedChatDraft> = drafts.values.toList()

        override suspend fun saveChatDraft(scope: CacheScope, draft: CachedChatDraft) {
            drafts[draft.chatId] = draft
        }

        override suspend fun deleteChatDraft(scope: CacheScope, chatId: String) {
            drafts.remove(chatId)
        }
    }

    private class FakeGateway(
        deleteResults: List<Boolean>,
        getResults: List<ChatDraftData?>,
    ) : ChatDraftGateway {
        private val deleteResults = ArrayDeque(deleteResults)
        private val getResults = ArrayDeque(getResults)
        val deletedRevisions = mutableListOf<String>()

        override suspend fun get(chatId: String): Result<ChatDraftData?> =
            Result.success(getResults.removeFirst())

        override suspend fun upsert(
            chatId: String,
            text: String,
            replyToMessageId: Long,
        ): Result<ChatDraftData> = Result.success(ChatDraftData(text, replyToMessageId, "r1", 1L))

        override suspend fun delete(chatId: String, expectedRevision: String): Result<Boolean> {
            deletedRevisions += expectedRevision
            return Result.success(deleteResults.removeFirst())
        }
    }

    companion object {
        private const val CHAT_ID = "chat-1"
        private val SCOPE = CacheScope("server|1")

        private fun draft(text: String, revision: String) = ChatDraftData(text, 0L, revision, 2L)
    }
}
