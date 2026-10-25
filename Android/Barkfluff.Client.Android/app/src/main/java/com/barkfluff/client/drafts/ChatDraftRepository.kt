package com.barkfluff.client.drafts

import android.content.Context
import android.util.Log
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDraft
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.ChatDraftGateway
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

internal interface ChatDraftCacheStore {
    suspend fun readChatDrafts(scope: CacheScope): List<CachedChatDraft>
    suspend fun saveChatDraft(scope: CacheScope, draft: CachedChatDraft)
    suspend fun deleteChatDraft(scope: CacheScope, chatId: String)
}

private class RepositoryChatDraftCacheStore(
    private val repository: ChatCacheRepository,
) : ChatDraftCacheStore {
    override suspend fun readChatDrafts(scope: CacheScope): List<CachedChatDraft> =
        repository.readChatDrafts(scope)

    override suspend fun saveChatDraft(scope: CacheScope, draft: CachedChatDraft) =
        repository.saveChatDraft(scope, draft)

    override suspend fun deleteChatDraft(scope: CacheScope, chatId: String) =
        repository.deleteChatDraft(scope, chatId)
}

/** Durable, per-node/per-user journal for regular-chat drafts. */
class ChatDraftRepository internal constructor(
    private val currentScope: () -> CacheScope?,
    private val cache: ChatDraftCacheStore,
    private val remote: ChatDraftGateway,
) {
    constructor(
        context: Context,
        cache: ChatCacheRepository,
        remote: ChatDraftGateway,
    ) : this(
        currentScope = { CacheScope.from(GlobalParam(context)) },
        cache = RepositoryChatDraftCacheStore(cache),
        remote = remote,
    )

    private val mutex = Mutex()
    // Cover snapshot -> RPC -> persistence, so wipe cannot race an already captured upsert.
    private val networkMutex = Mutex()
    private var loadedScope: CacheScope? = null
    private val entries = linkedMapOf<String, LocalChatDraft>()
    private val _drafts = MutableStateFlow<Map<String, LocalChatDraft>>(emptyMap())
    val drafts: StateFlow<Map<String, LocalChatDraft>> = _drafts.asStateFlow()

    suspend fun restore(chatId: String): LocalChatDraft? = networkMutex.withLock { restoreLocked(chatId) }

    private suspend fun restoreLocked(chatId: String): LocalChatDraft? {
        val known = mutex.withLock {
            ensureLoaded() ?: return null
            entries[chatId]?.takeIf { it.syncState != ChatDraftSyncState.SYNCED }?.let {
                return it.takeIf(LocalChatDraft::isActive)
            }
            entries[chatId]
        }

        val remoteResult = remote.get(chatId)
        return mutex.withLock {
            val scope = ensureLoaded() ?: return@withLock null
            val current = entries[chatId]
            if (current != known) {
                return@withLock current?.takeIf(LocalChatDraft::isActive)
            }
            if (remoteResult.isFailure) return@withLock entries[chatId]?.takeIf(LocalChatDraft::isActive)
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
    }

    suspend fun edit(chatId: String, text: String, replyToMessageId: Long): LocalChatDraft? = mutex.withLock {
        val scope = ensureLoaded() ?: return@withLock null
        val current = entries[chatId]
        val edited = ChatDraftJournal.edit(current, text, replyToMessageId)
        val next = if (edited.syncState == ChatDraftSyncState.DELETE_PENDING) {
            edited.copy(text = current?.text.orEmpty())
        } else edited
        entries[chatId] = next
        persist(scope, chatId, next)
        publish()
        next
    }

    suspend fun flush(chatId: String) = networkMutex.withLock { flushLocked(chatId) }

    private suspend fun flushLocked(chatId: String) {
        val pending = mutex.withLock {
            val scope = ensureLoaded() ?: return@withLock null
            entries[chatId]?.let { scope to it }
        } ?: return
        val (scope, snapshot) = pending
        when (snapshot.syncState) {
            ChatDraftSyncState.SYNCED -> Unit
            ChatDraftSyncState.DIRTY -> {
                val saved = remote.upsert(chatId, snapshot.text, snapshot.replyToMessageId).getOrNull()
                    ?: return
                mutex.withLock {
                    val current = entries[chatId] ?: return@withLock
                    val synced = ChatDraftJournal.markSynced(current, snapshot.generation, saved.revision)
                    entries[chatId] = synced
                    persist(scope, chatId, synced)
                    publish()
                }
            }
            ChatDraftSyncState.DELETE_PENDING -> {
                if (snapshot.revision.isBlank()) {
                    mutex.withLock {
                        if (entries[chatId]?.generation == snapshot.generation) {
                            entries.remove(chatId)
                            cache.deleteChatDraft(scope, chatId)
                            publish()
                        }
                    }
                    return
                }
                val deleted = remote.delete(chatId, snapshot.revision)
                if (deleted.getOrNull() == true) {
                    removeIfGenerationMatches(scope, chatId, snapshot.generation)
                } else if (deleted.getOrNull() == false) {
                    resolveDeleteConflict(scope, chatId, snapshot)
                }
            }
        }
    }

    private suspend fun resolveDeleteConflict(scope: CacheScope, chatId: String, tombstone: LocalChatDraft) {
        val latestResult = remote.get(chatId)
        if (latestResult.isFailure) return
        val latest = latestResult.getOrNull()
        if (latest == null) {
            removeIfGenerationMatches(scope, chatId, tombstone.generation)
            return
        }

        if (latest.text.isBlank() || latest.text == tombstone.text) {
            val currentTombstone = tombstone.copy(revision = latest.revision)
            val shouldRetry = mutex.withLock {
                val current = entries[chatId] ?: return@withLock false
                if (current.generation != tombstone.generation) return@withLock false
                entries[chatId] = currentTombstone
                persist(scope, chatId, currentTombstone)
                publish()
                true
            }
            if (shouldRetry && remote.delete(chatId, latest.revision).getOrNull() == true) {
                removeIfGenerationMatches(scope, chatId, tombstone.generation)
            }
            return
        }

        mutex.withLock {
            val current = entries[chatId] ?: return@withLock
            if (current.generation != tombstone.generation) return@withLock
            val preserved = LocalChatDraft(
                text = latest.text,
                replyToMessageId = latest.replyToMessageId,
                revision = latest.revision,
                generation = tombstone.generation + 1,
                syncState = ChatDraftSyncState.SYNCED,
            )
            entries[chatId] = preserved
            persist(scope, chatId, preserved)
            publish()
        }
    }

    private suspend fun removeIfGenerationMatches(scope: CacheScope, chatId: String, generation: Long) {
        mutex.withLock {
            if (entries[chatId]?.generation == generation) {
                entries.remove(chatId)
                cache.deleteChatDraft(scope, chatId)
                publish()
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

    /** Best-effort remote deletion and local cleanup for the selected account/server only. */
    suspend fun wipeAll(scope: CacheScope) = networkMutex.withLock {
        val snapshot = mutex.withLock {
            check(ensureLoaded() == scope) { "Draft scope changed before cleanup" }
            entries.toMap()
        }
        snapshot.forEach { (chatId, draft) ->
            if (draft.revision.isBlank()) return@forEach
            try {
                val result = remote.delete(chatId, draft.revision)
                val error = result.exceptionOrNull()
                if (error is CancellationException) throw error
                if (error != null) {
                    Log.w("ChatDraftRepository", "Could not delete a remote draft during wipe", error)
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Throwable) {
                Log.w("ChatDraftRepository", "Could not delete a remote draft during wipe", error)
            }
        }
        mutex.withLock {
            snapshot.forEach { (chatId, draft) ->
                if (loadedScope == scope && entries[chatId] == draft) {
                    cache.deleteChatDraft(scope, chatId)
                    entries.remove(chatId)
                }
            }
            publish()
        }
    }

    suspend fun clearAfterSent(chatId: String, sentGeneration: Long) = networkMutex.withLock {
        clearAfterSentLocked(chatId, sentGeneration)
    }

    private suspend fun clearAfterSentLocked(chatId: String, sentGeneration: Long) {
        val pending = mutex.withLock {
            val scope = ensureLoaded() ?: return@withLock null
            val current = entries[chatId] ?: return@withLock null
            if (current.generation != sentGeneration) return@withLock null
            scope to current
        } ?: return
        val (scope, sent) = pending
        val synced = if (sent.syncState == ChatDraftSyncState.DIRTY) {
            val saved = remote.upsert(chatId, sent.text, sent.replyToMessageId).getOrNull() ?: return
            ChatDraftJournal.markSynced(sent, sent.generation, saved.revision)
        } else sent
        mutex.withLock {
            val current = entries[chatId] ?: return@withLock
            if (current.generation != sentGeneration) {
                val updated = ChatDraftJournal.markSynced(current, sentGeneration, synced.revision)
                entries[chatId] = updated
                persist(scope, chatId, updated)
                publish()
                return@withLock
            }
            val tombstone = ChatDraftJournal.edit(synced, "", 0L).copy(text = sent.text)
            entries[chatId] = tombstone
            persist(scope, chatId, tombstone)
            publish()
        }
        flushLocked(chatId)
    }

    private suspend fun ensureLoaded(): CacheScope? {
        val scope = currentScope() ?: return null
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
