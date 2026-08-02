package com.barkfluff.client.drafts

enum class ChatDraftSyncState { DIRTY, SYNCED, DELETE_PENDING }

data class LocalChatDraft(
    val text: String,
    val replyToMessageId: Long,
    val revision: String = "",
    val generation: Long = 0L,
    val syncState: ChatDraftSyncState = ChatDraftSyncState.DIRTY
) {
    val isActive: Boolean
        get() = syncState != ChatDraftSyncState.DELETE_PENDING
}

/** Pure state transitions for the persisted chat-draft journal. */
object ChatDraftJournal {

    fun edit(current: LocalChatDraft?, text: String, replyToMessageId: Long): LocalChatDraft {
        val nextGeneration = (current?.generation ?: 0L) + 1L
        return if (text.isEmpty() && replyToMessageId == 0L) {
            LocalChatDraft(
                text = "",
                replyToMessageId = 0L,
                revision = current?.revision.orEmpty(),
                generation = nextGeneration,
                syncState = ChatDraftSyncState.DELETE_PENDING
            )
        } else {
            LocalChatDraft(
                text = text,
                replyToMessageId = replyToMessageId,
                revision = current?.revision.orEmpty(),
                generation = nextGeneration,
                syncState = ChatDraftSyncState.DIRTY
            )
        }
    }

    fun markSynced(current: LocalChatDraft, generation: Long, revision: String): LocalChatDraft =
        if (current.generation != generation || current.syncState == ChatDraftSyncState.DELETE_PENDING) current
        else current.copy(revision = revision, syncState = ChatDraftSyncState.SYNCED)

    fun removeIfGenerationMatches(current: LocalChatDraft?, generation: Long): LocalChatDraft? =
        if (current?.generation == generation) null else current
}
