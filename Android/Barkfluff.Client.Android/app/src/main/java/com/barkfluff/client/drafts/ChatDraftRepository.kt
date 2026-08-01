package com.barkfluff.client.drafts

import android.content.Context
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDraft
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** Durable, per-node/per-user journal for regular-chat drafts. */
class ChatDraftRepository(
    context: Context,
    grpcManager: GrpcManager,
    private val cache: ChatCacheRepository
) {
    private val globalParam = GlobalParam(context)
    private val remote = ChatRepository(context.applicationContext, grpcManager)
    private val mutex = Mutex()
    private var loadedScope: CacheScope? = null
    private val entries = linkedMapOf<String, LocalChatDraft>()
    private val _drafts = MutableStateFlow<Map<String, LocalChatDraft>>(emptyMap())
    val drafts: StateFlow<Map<String, LocalChatDraft>> = _drafts.asStateFlow()

    suspend fun restore(chatId: String): LocalChatDraft? = mutex.withLock {
        val scope = ensureLoaded() ?: return@withLock null
        entries[chatId]?.takeIf { it.syncState != ChatDraftSyncState.SYNCED }?.let {
            return@withLock it.takeIf(LocalChatDraft::isActive)
        }

        val remoteResult = remote.getChatDraft(chatId)
        if (remoteResult.isFailure) return@withLock null
        val remoteDraft = remoteResult.getOrNull()
        if (remoteDraft == null) {
            entries.remove(chatId)
            cache.deleteChatDraft(scope, chatId)
            publish()
            return@withLock null
        }
        val restored = LocalChatDraft(
            text = remoteDraft.text,
            replyToMessageId = remoteDraft.replyToMessageId,
            revision = remoteDraft.revision,
            generation = 1L,
            syncState = ChatDraftSyncState.SYNCED
        )
        entries[chatId] = restored
        persist(scope, chatId, restored)
        publish()
        restored
    }

    suspend fun edit(chatId: String, text: String, replyToMessageId: Long): LocalChatDraft? = mutex.withLock {
        val scope = ensureLoaded() ?: return@withLock null
        val next = ChatDraftJournal.edit(entries[chatId], text, replyToMessageId)
        entries[chatId] = next
        persist(scope, chatId, next)
        publish()
        next
    }

    suspend fun flush(chatId: String) = mutex.withLock {
        val scope = ensureLoaded() ?: return@withLock
        val snapshot = entries[chatId] ?: return@withLock
        when (snapshot.syncState) {
            ChatDraftSyncState.SYNCED -> Unit
            ChatDraftSyncState.DIRTY -> {
                val saved = remote.upsertChatDraft(chatId, snapshot.text, snapshot.replyToMessageId).getOrNull()
                    ?: return@withLock
                val current = entries[chatId] ?: return@withLock
                val synced = ChatDraftJournal.markSynced(current, snapshot.generation, saved.revision)
                entries[chatId] = synced
                persist(scope, chatId, synced)
                publish()
            }
            ChatDraftSyncState.DELETE_PENDING -> {
                if (snapshot.revision.isBlank()) {
                    entries.remove(chatId)
                    cache.deleteChatDraft(scope, chatId)
                    publish()
                    return@withLock
                }
                val deleted = remote.deleteChatDraft(chatId, snapshot.revision).getOrNull() ?: return@withLock
                if (deleted && entries[chatId]?.generation == snapshot.generation) {
                    entries.remove(chatId)
                    cache.deleteChatDraft(scope, chatId)
                    publish()
                }
            }
        }
    }

    suspend fun flushAll() {
        val ids = mutex.withLock {
            ensureLoaded()
            entries.keys.toList()
        }
        for (chatId in ids) flush(chatId)
    }

    suspend fun loadLocal() = mutex.withLock { ensureLoaded() }

    suspend fun clearAfterSent(chatId: String, sentGeneration: Long) {
        mutex.withLock {
            val scope = ensureLoaded() ?: return@withLock
            val current = entries[chatId] ?: return@withLock
            if (current.generation != sentGeneration) return@withLock
            val tombstone = ChatDraftJournal.edit(current, "", 0L)
            entries[chatId] = tombstone
            persist(scope, chatId, tombstone)
            publish()
        }
        flush(chatId)
    }

    private suspend fun ensureLoaded(): CacheScope? {
        val scope = CacheScope.from(globalParam) ?: return null
        if (loadedScope?.id == scope.id) return scope
        entries.clear()
        cache.readChatDrafts(scope).forEach { cached -> entries[cached.chatId] = cached.toLocal() }
        loadedScope = scope
        publish()
        return scope
    }

    private suspend fun persist(scope: CacheScope, chatId: String, draft: LocalChatDraft) {
        cache.saveChatDraft(scope, CachedChatDraft(
            chatId, draft.text, draft.replyToMessageId, draft.revision, draft.generation, draft.syncState.ordinal
        ))
    }

    private fun CachedChatDraft.toLocal() = LocalChatDraft(
        text, replyToMessageId, revision, generation,
        ChatDraftSyncState.entries.getOrElse(syncState) { ChatDraftSyncState.DIRTY }
    )

    private fun publish() {
        _drafts.value = entries.toMap()
    }
}
