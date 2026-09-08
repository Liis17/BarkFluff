package com.barkfluff.client.send

/** Retry schedule for temporary outbox failures. */
object OutgoingRetryPolicy {
    private val delaysMillis = longArrayOf(
        10_000L,
        30_000L,
        120_000L,
        300_000L,
        900_000L,
        1_800_000L
    )

    fun delayForAttempt(attempt: Int): Long =
        delaysMillis[(attempt.coerceAtLeast(1) - 1).coerceAtMost(delaysMillis.lastIndex)]
}
