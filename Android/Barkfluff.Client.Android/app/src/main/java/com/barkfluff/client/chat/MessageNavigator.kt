package com.barkfluff.client.chat

import barkfluff.shared.Shared

data class MessageTarget(val messageId: Long, val requestId: Long)
class MessageTargetMissingException : Exception()
internal val messageChronologicalOrder = compareBy<Shared.Message> { it.sentAt.seconds }.thenBy { it.sentAt.nanos }.thenBy { it.id }

/** Owns pending navigation until the adapter has committed the requested message. */
class MessageNavigator(private val load: suspend (Long, Int, Int) -> Result<List<Shared.Message>>) {
    private var nextRequestId = 0L
    var pending: MessageTarget? = null
        private set
    var liveTailVersion = 0L
        private set

    fun deferLiveMessage() { liveTailVersion++ }
    fun hasUnloadedTailSince(version: Long): Boolean = liveTailVersion != version

    fun request(messageId: Long): MessageTarget {
        return MessageTarget(messageId, ++nextRequestId).also { pending = it }
    }

    suspend fun window(target: MessageTarget): Result<RegularChatSession.Page>? {
        if (pending != target) return null
        val tailVersion = liveTailVersion
        val result = load(target.messageId, 20, 20)
        if (pending != target) return null
        return result.mapCatching { messages ->
            if (messages.none { it.id == target.messageId }) throw MessageTargetMissingException()
            val sorted = messages.sortedWith(messageChronologicalOrder)
            val index = sorted.indexOfFirst { it.id == target.messageId }
            RegularChatSession.Page(sorted, hasMoreBefore = index >= 20,
                hasMoreAfter = sorted.size - index - 1 >= 20 || hasUnloadedTailSince(tailVersion))
        }
    }

    fun position(target: MessageTarget, committedMessageIds: List<Long>, listIsCommitted: Boolean): Int? =
        if (pending == target && listIsCommitted) committedMessageIds.indexOf(target.messageId).takeIf { it >= 0 } else null

    fun acknowledge(requestId: Long): Boolean {
        if (pending?.requestId != requestId) return false
        pending = null
        return true
    }
}
