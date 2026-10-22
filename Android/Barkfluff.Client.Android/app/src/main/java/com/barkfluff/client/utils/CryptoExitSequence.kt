package com.barkfluff.client.utils

import com.barkfluff.client.grpc.TokenValidity
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

enum class SessionGate { CONTINUE, LOGIN, CLEANUP_FAILED }

/** Only a rejected Identity session is destructive; an unavailable Identity keeps local state. */
suspend fun gateTokenValidity(validity: TokenValidity, exitRejectedSession: suspend () -> Boolean): SessionGate =
    when (validity) {
        TokenValidity.VALID, TokenValidity.UNAVAILABLE -> SessionGate.CONTINUE
        TokenValidity.REJECTED -> if (exitRejectedSession()) SessionGate.LOGIN else SessionGate.CLEANUP_FAILED
    }

class BoundedNetworkStep(val timeoutMillis: Long, val call: suspend () -> Unit)

/**
 * Once crypto invalidation starts, the exit must reach credential finalization even when the
 * caller is cancelled; otherwise wiped keys would remain paired with live credentials. Network
 * steps are best effort and bounded so the non-cancellable section cannot hang forever.
 * Returns false only when crypto cleanup could not be journaled and credentials were retained.
 */
suspend fun runCryptoExit(
    cryptoExit: suspend () -> Boolean,
    localCleanup: suspend () -> Unit,
    network: List<BoundedNetworkStep> = emptyList(),
    finalizeCredentials: suspend () -> Unit,
    onStepError: (Throwable) -> Unit = {},
): Boolean = withContext(NonCancellable) {
    if (!cryptoExit()) return@withContext false
    try {
        localCleanup()
    } catch (e: Exception) {
        onStepError(e)
    }
    for (step in network) {
        try {
            withTimeoutOrNull(step.timeoutMillis) { step.call() }
        } catch (e: Exception) {
            onStepError(e)
        }
    }
    finalizeCredentials()
    true
}
