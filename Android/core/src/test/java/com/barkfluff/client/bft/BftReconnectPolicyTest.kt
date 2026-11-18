package com.barkfluff.client.bft

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BftReconnectPolicyTest {
    @Test
    fun `backoff doubles with bounded jitter and a network change resets it`() {
        val policy = BftReconnectPolicy(
            randomUnit = { 0.5 },
            baseDelayMillis = 100,
            maxDelayMillis = 800,
            rejectedKeyDelayMillis = 5_000
        )

        assertEquals(100, policy.recordFailure().delayMillis)
        assertEquals(200, policy.recordFailure().delayMillis)
        assertEquals(400, policy.recordFailure().delayMillis)
        policy.networkChanged()
        assertEquals(100, policy.recordFailure().delayMillis)
    }

    @Test
    fun `authentication failure and repeated silent closes show key rejection`() {
        val policy = BftReconnectPolicy(
            randomUnit = { 0.5 },
            baseDelayMillis = 100,
            maxDelayMillis = 800,
            rejectedKeyDelayMillis = 5_000,
            rejectedAfterSilentFailures = 3
        )

        assertFalse(policy.recordFailure(closedWithoutResponse = true).keyRejected)
        assertFalse(policy.recordFailure(closedWithoutResponse = true).keyRejected)
        val revoked = policy.recordFailure(closedWithoutResponse = true)
        assertTrue(revoked.keyRejected)
        assertEquals(5_000, revoked.delayMillis)

        policy.connected()
        val auth = policy.recordFailure(authenticationFailed = true)
        assertTrue(auth.keyRejected)
        assertEquals(5_000, auth.delayMillis)
    }
}
