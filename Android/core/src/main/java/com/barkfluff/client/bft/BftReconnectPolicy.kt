package com.barkfluff.client.bft

/** Pure retry policy; delays are scheduled only while the app has an active network demand. */
internal class BftReconnectPolicy(
    private val randomUnit: () -> Double = { Math.random() },
    private val baseDelayMillis: Long = 1_000,
    private val maxDelayMillis: Long = 60_000,
    private val rejectedKeyDelayMillis: Long = 300_000,
    private val rejectedAfterSilentFailures: Int = 3
) {
    data class FailureDecision(val delayMillis: Long, val keyRejected: Boolean)

    private var failures = 0
    private var silentFailures = 0

    fun recordFailure(authenticationFailed: Boolean = false, closedWithoutResponse: Boolean = false): FailureDecision {
        failures++
        silentFailures = if (closedWithoutResponse) silentFailures + 1 else 0
        val rejected = authenticationFailed || silentFailures >= rejectedAfterSilentFailures
        if (rejected) return FailureDecision(rejectedKeyDelayMillis, keyRejected = true)

        val exponential = (baseDelayMillis * (1L shl (failures - 1).coerceAtMost(20)))
            .coerceAtMost(maxDelayMillis)
        val jitter = randomUnit().coerceIn(0.0, 1.0)
        val factor = 0.75 + jitter * 0.5
        return FailureDecision((exponential * factor).toLong().coerceAtLeast(1), keyRejected = false)
    }

    fun connected() {
        failures = 0
        silentFailures = 0
    }

    fun networkChanged() {
        connected()
    }
}
