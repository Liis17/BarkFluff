package com.barkfluff.client.chat

import barkfluff.shared.Shared

data class MessageTarget(val messageId: Long, val requestId: Long)
class MessageTargetMissingException : Exception()

/** Owns pending navigation until the adapter has committed the requested message. */
class MessageNavigator(private val load: suspend (Long, Int, Int) -> Result<List<Shared.Message>>) {
    private var nextRequestId = 0L
    var pending: MessageTarget? = null
        private set

    fun request(messageId: Long): MessageTarget {
        return MessageTarget(messageId, ++nextRequestId).also { pending = it }
    }

    suspend fun window(target: MessageTarget): Result<List<Shared.Message>>? {
        if (pending != target) return null
        val result = load(target.messageId, 20, 20)
        if (pending != target) return null
        return result.mapCatching { messages ->
            if (messages.none { it.id == target.messageId }) throw MessageTargetMissingException()
            messages.sortedWith(compareBy<Shared.Message> { it.sentAt.seconds }.thenBy { it.sentAt.nanos }.thenBy { it.id })
        }
    }

    fun position(target: MessageTarget, committedMessageIds: List<Long>): Int? =
        if (pending == target) committedMessageIds.indexOf(target.messageId).takeIf { it >= 0 } else null

    fun acknowledge(requestId: Long): Boolean {
        if (pending?.requestId != requestId) return false
        pending = null
        return true
    }
}
