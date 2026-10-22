package com.barkfluff.client.utils

import com.barkfluff.client.grpc.TokenValidity
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CryptoExitSequenceTest {

    @Test
    fun unavailableIdentityKeepsStateAndOnlyRejectedRunsCleanup() = runTest {
        var cleanups = 0
        assertEquals(SessionGate.CONTINUE, gateTokenValidity(TokenValidity.UNAVAILABLE) { cleanups++; true })
        assertEquals(SessionGate.CONTINUE, gateTokenValidity(TokenValidity.VALID) { cleanups++; true })
        assertEquals(0, cleanups)
        assertEquals(SessionGate.LOGIN, gateTokenValidity(TokenValidity.REJECTED) { cleanups++; true })
        assertEquals(1, cleanups)
    }

    @Test
    fun rejectedCleanupJournalFailureIsReportedAndRetryCanSucceed() = runTest {
        val results = ArrayDeque(listOf(false, true))
        assertEquals(SessionGate.CLEANUP_FAILED, gateTokenValidity(TokenValidity.REJECTED) { results.removeFirst() })
        assertEquals(SessionGate.LOGIN, gateTokenValidity(TokenValidity.REJECTED) { results.removeFirst() })
    }

    @Test
    fun cancellationAfterCryptoExitStillFinalizesCredentials() = runTest {
        val networkStarted = CompletableDeferred<Unit>()
        var finalized = false
        val logout = launch {
            runCryptoExit(
                cryptoExit = { true },
                localCleanup = {},
                network = listOf(BoundedNetworkStep(1_000) { networkStarted.complete(Unit); awaitCancellation() }),
                finalizeCredentials = { finalized = true },
            )
        }
        networkStarted.await()
        logout.cancelAndJoin()
        assertTrue(finalized)
    }

    @Test
    fun failedCleanupJournalRetainsCredentials() = runTest {
        var finalized = false
        var cleaned = false
        assertFalse(runCryptoExit(
            cryptoExit = { false },
            localCleanup = { cleaned = true },
            finalizeCredentials = { finalized = true },
        ))
        assertFalse(cleaned)
        assertFalse(finalized)
    }

    @Test
    fun failingNetworkAndLocalStepsDoNotPreventFinalization() = runTest {
        var finalized = false
        assertTrue(runCryptoExit(
            cryptoExit = { true },
            localCleanup = { error("cache unavailable") },
            network = listOf(BoundedNetworkStep(1_000) { error("offline") }),
            finalizeCredentials = { finalized = true },
        ))
        assertTrue(finalized)
    }
}
