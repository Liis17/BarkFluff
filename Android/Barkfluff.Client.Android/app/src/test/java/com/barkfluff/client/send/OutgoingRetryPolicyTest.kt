package com.barkfluff.client.send

import org.junit.Assert.assertEquals
import org.junit.Test

class OutgoingRetryPolicyTest {

    @Test
    fun `retry delay follows the documented capped schedule`() {
        assertEquals(10_000L, OutgoingRetryPolicy.delayForAttempt(1))
        assertEquals(30_000L, OutgoingRetryPolicy.delayForAttempt(2))
        assertEquals(120_000L, OutgoingRetryPolicy.delayForAttempt(3))
        assertEquals(300_000L, OutgoingRetryPolicy.delayForAttempt(4))
        assertEquals(900_000L, OutgoingRetryPolicy.delayForAttempt(5))
        assertEquals(1_800_000L, OutgoingRetryPolicy.delayForAttempt(6))
        assertEquals(1_800_000L, OutgoingRetryPolicy.delayForAttempt(100))
    }

    @Test
    fun `first retry is used for zero or negative attempt counts`() {
        assertEquals(10_000L, OutgoingRetryPolicy.delayForAttempt(0))
        assertEquals(10_000L, OutgoingRetryPolicy.delayForAttempt(-1))
    }
}
