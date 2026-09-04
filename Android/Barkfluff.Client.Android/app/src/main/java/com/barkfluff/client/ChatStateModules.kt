package com.barkfluff.client

import com.barkfluff.client.cache.ComposerAttachment
import com.barkfluff.client.domain.gateway.RealtimeGateway
import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

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

    fun stagedAttachment(state: ComposerState, path: String, kind: String = "DOCUMENT"): ComposerState {
        if (path.isBlank() || path in state.attachmentPaths) return state
        val alignedKinds = state.attachmentKinds
            .take(state.attachmentPaths.size)
            .let { kinds ->
                if (kinds.size == state.attachmentPaths.size) kinds
                else kinds + List(state.attachmentPaths.size - kinds.size) { "DOCUMENT" }
            }
        return state.copy(
            attachmentPaths = state.attachmentPaths + path,
            attachmentKinds = alignedKinds + kind,
        )
    }

    fun removeAttachment(state: ComposerState, path: String): ComposerState {
        val index = state.attachmentPaths.indexOf(path)
        if (index < 0) return state
        return state.copy(
            attachmentPaths = state.attachmentPaths.filterIndexed { itemIndex, _ -> itemIndex != index },
            attachmentKinds = state.attachmentKinds.filterIndexed { itemIndex, _ -> itemIndex != index },
        )
    }

    fun clearAfterDurableEnqueue(state: ComposerState, generation: Long?): ComposerState = state.copy(
        text = "",
        pendingReply = null,
        pendingEdit = null,
        attachmentPaths = emptyList(),
        attachmentKinds = emptyList(),
        draftGeneration = generation,
    )

    /**
     * Completes one send while retaining previews staged after the handoff started. The outbox
     * and [ComposerAttachmentStore] use generations to distinguish those newer records.
     */
    fun clearAfterDurableEnqueue(
        state: ComposerState,
        generation: Long?,
        remaining: List<ComposerAttachment>,
    ): ComposerState {
        val ordered = remaining.sortedBy { it.attachmentIndex }
        return state.copy(
            text = "",
            pendingReply = null,
            pendingEdit = null,
            attachmentPaths = ordered.map { it.path },
            attachmentKinds = ordered.map { it.kind },
            draftGeneration = ordered.maxOfOrNull { it.generation } ?: generation,
        )
    }
}

/**
 * Presence reducer with expiry timestamps. The ViewModel owns the subscription jobs; this helper
 * makes cross-chat/self-user filtering deterministic and easy to unit-test.
 */
class ChatPresence(private val currentUserId: Long) {
    private val typingExpiry = ConcurrentHashMap<String, Long>()

    fun online(
        state: PresenceState,
        userId: Long,
        isOnline: Boolean,
        lastSeenEpochMillis: Long = 0L,
    ): PresenceState {
        if (userId <= 0L || userId == currentUserId) return state
        val ids = state.onlineUserIds.toMutableSet()
        if (isOnline) ids.add(userId) else ids.remove(userId)
        val lastSeen = state.lastSeenEpochMillisByUser.toMutableMap()
        if (lastSeenEpochMillis > 0L) lastSeen[userId] = lastSeenEpochMillis
        return state.copy(
            onlineUserIds = ids,
            lastSeenEpochMillisByUser = lastSeen,
        )
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

/**
 * Owns the side effects around a regular-chat presence subscription. The reducer above remains
 * pure; this small coordinator is the only place that starts typing heartbeats or changes the
 * server subscription, so an Activity recreation cannot leave duplicate loops behind.
 */
class ChatPresenceSession(
    private val realtime: RealtimeGateway,
    private val scope: CoroutineScope,
    private val heartbeatIntervalMillis: Long = 4_000L,
    private val idleTimeoutMillis: Long = 5_000L,
    private val nowMillis: () -> Long = { System.currentTimeMillis() },
) {
    private var chatId: String = ""
    private var lastTypingInputAt = 0L
    private var heartbeatJob: Job? = null
    private var typingAnnounced = false

    fun start(chatId: String, onlineUserIds: List<Long>) {
        if (this.chatId != chatId) stopTyping(sendCancel = true)
        this.chatId = chatId
        realtime.changeTypingSubscription(chatId.takeIf { it.isNotBlank() }?.let(::listOf).orEmpty())
        realtime.changeOnlineSubscription(onlineUserIds.filter { it > 0L }.distinct())
    }

    fun textChanged(text: String) {
        if (chatId.isBlank()) return
        if (text.isBlank()) {
            stopTyping(sendCancel = true)
            return
        }
        lastTypingInputAt = nowMillis()
        if (heartbeatJob?.isActive == true) return
        heartbeatJob = scope.launch {
            typingAnnounced = true
            while (isActive && chatId.isNotBlank()) {
                realtime.sendTypingStatus(chatId, typing = true)
                delay(heartbeatIntervalMillis)
                if (nowMillis() - lastTypingInputAt >= idleTimeoutMillis) break
            }
            if (isActive && chatId.isNotBlank() && typingAnnounced) {
                realtime.sendTypingStatus(chatId, typing = false)
                typingAnnounced = false
            }
            heartbeatJob = null
        }
    }

    fun stopTyping(sendCancel: Boolean) {
        heartbeatJob?.cancel()
        heartbeatJob = null
        if (sendCancel && chatId.isNotBlank() && typingAnnounced) {
            realtime.sendTypingStatus(chatId, typing = false)
        }
        typingAnnounced = false
    }

    fun stop() {
        stopTyping(sendCancel = true)
        realtime.changeTypingSubscription(emptyList())
        realtime.changeOnlineSubscription(emptyList())
        chatId = ""
    }
}
