package com.barkfluff.client

import java.util.concurrent.ConcurrentHashMap

/** Pure reducer for message selection. No RecyclerView or Activity state is required. */
class SelectionReducer {
    fun toggle(state: SelectionState, messageId: Long): SelectionState {
        if (messageId <= 0L) return state
        val selected = state.selectedMessageIds.toMutableSet()
        if (!selected.add(messageId)) selected.remove(messageId)
        return SelectionState(selectedMessageIds = selected, isActive = selected.isNotEmpty())
    }

    fun selectAll(messageIds: Iterable<Long>): SelectionState {
        val ids = messageIds.filter { it > 0L }.toSet()
        return SelectionState(
            selectedMessageIds = ids,
            isActive = ids.isNotEmpty(),
        )
    }

    fun clear(): SelectionState = SelectionState()

    fun removeMissing(state: SelectionState, availableIds: Set<Long>): SelectionState {
        val selected = state.selectedMessageIds.intersect(availableIds)
        return SelectionState(selectedMessageIds = selected, isActive = selected.isNotEmpty())
    }
}

/**
 * Stateless composer transition helper. Persistence and enqueue are injected by the ViewModel;
 * this class only enforces the reply/edit invariant and accepted-preview semantics.
 */
class ChatComposer {
    fun textChanged(state: ComposerState, text: String): ComposerState = state.copy(text = text)

    fun beginReply(state: ComposerState, reply: PendingReply): ComposerState = state.copy(
        pendingReply = reply,
        pendingEdit = null,
    )

    fun beginEdit(state: ComposerState, edit: PendingEdit): ComposerState = state.copy(
        pendingReply = null,
        pendingEdit = edit,
        text = edit.text,
    )

    fun clearReply(state: ComposerState): ComposerState = state.copy(pendingReply = null)

    fun clearEdit(state: ComposerState): ComposerState = state.copy(pendingEdit = null)

    fun stagedAttachment(state: ComposerState, path: String): ComposerState {
        if (path.isBlank() || path in state.attachmentPaths) return state
        return state.copy(attachmentPaths = state.attachmentPaths + path)
    }

    fun removeAttachment(state: ComposerState, path: String): ComposerState =
        state.copy(attachmentPaths = state.attachmentPaths - path)

    fun clearAfterDurableEnqueue(state: ComposerState, generation: Long?): ComposerState = state.copy(
        text = "",
        pendingReply = null,
        pendingEdit = null,
        attachmentPaths = emptyList(),
        draftGeneration = generation,
    )
}

/**
 * Presence reducer with expiry timestamps. The ViewModel owns the subscription jobs; this helper
 * makes cross-chat/self-user filtering deterministic and easy to unit-test.
 */
class ChatPresence(private val currentUserId: Long) {
    private val typingExpiry = ConcurrentHashMap<String, Long>()

    fun online(state: PresenceState, userId: Long, isOnline: Boolean): PresenceState {
        if (userId <= 0L || userId == currentUserId) return state
        val ids = state.onlineUserIds.toMutableSet()
        if (isOnline) ids.add(userId) else ids.remove(userId)
        return state.copy(onlineUserIds = ids)
    }

    fun typing(
        state: PresenceState,
        chatId: String,
        eventChatId: String,
        userId: Long,
        isTyping: Boolean,
        nowMillis: Long,
        expiryMillis: Long = 5_000L,
    ): PresenceState {
        if (chatId.isBlank() || !chatId.equals(eventChatId, ignoreCase = true) || userId <= 0L || userId == currentUserId) {
            return expire(state, nowMillis)
        }
        val ids = state.typingUserIds.toMutableSet()
        val key = "$eventChatId:$userId"
        if (isTyping) {
            ids.add(userId)
            typingExpiry[key] = nowMillis + expiryMillis
        } else {
            ids.remove(userId)
            typingExpiry.remove(key)
        }
        return state.copy(typingUserIds = ids)
    }

    fun expire(state: PresenceState, nowMillis: Long): PresenceState {
        val expired = typingExpiry.filterValues { it <= nowMillis }.keys
        if (expired.isEmpty()) return state
        expired.forEach(typingExpiry::remove)
        val ids = state.typingUserIds.toMutableSet()
        // A user may have typing events for multiple chats; remove only IDs no longer present.
        val activeIds = typingExpiry.keys.mapNotNull { it.substringAfter(':', "").toLongOrNull() }.toSet()
        ids.retainAll(activeIds)
        return state.copy(typingUserIds = ids)
    }
}
