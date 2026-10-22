package com.barkfluff.client

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SecretSendOutcomeTest {

    @Test
    fun historyFailureAfterServerAcceptedSendIsNotASendFailure() = runTest {
        var sends = 0
        val outcome = sendThenRefresh(send = { sends++; Result.success(Unit) }, refresh = { error("Room write failed") })
        assertTrue(outcome is SecretSendOutcome.HistoryFailed)
        assertEquals(1, sends)
    }

    @Test
    fun networkFailureSkipsHistoryRefresh() = runTest {
        var refreshed = false
        val outcome = sendThenRefresh(send = { Result.failure<Unit>(IllegalStateException("offline")) }, refresh = { refreshed = true })
        assertTrue(outcome is SecretSendOutcome.SendFailed)
        assertEquals(false, refreshed)
    }

    @Test
    fun successfulSendAndRefreshIsSent() = runTest {
        assertEquals(SecretSendOutcome.Sent, sendThenRefresh(send = { Result.success(Unit) }, refresh = {}))
    }
}
