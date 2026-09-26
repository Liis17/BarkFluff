package com.barkfluff.client.domain.auth

import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import com.barkfluff.client.domain.model.AuthenticationChallenge
import com.barkfluff.client.domain.model.AuthenticationChallengeReference
import com.barkfluff.client.domain.model.AuthenticationChallengeState
import com.barkfluff.client.domain.model.AuthenticationCompletion
import kotlinx.coroutines.delay

/**
 * Keeps exactly one Identity challenge in memory and ignores responses from a cancelled/replaced
 * request. UI owns its polling Job, which is paused while the Activity is not foregrounded.
 */
class AuthenticationChallengeController(
    private val gateway: AuthenticationChallengeGateway,
    private val pollIntervalMillis: Long = POLL_INTERVAL_MILLIS,
) {
    private var generation = 0L
    private var activeReference: AuthenticationChallengeReference? = null

    val hasActiveChallenge: Boolean
        get() = activeReference != null

    suspend fun begin(start: suspend AuthenticationChallengeGateway.() -> Result<AuthenticationChallenge>): Result<AuthenticationChallenge> {
        cancelSilently()
        val requestGeneration = ++generation
        return gateway.start().onSuccess { challenge ->
            if (requestGeneration == generation) activeReference = challenge.reference
        }
    }

    /** Rejoins an in-memory challenge after a configuration change instead of issuing a duplicate one. */
    suspend fun resumeOrBegin(
        start: suspend AuthenticationChallengeGateway.() -> Result<AuthenticationChallenge>,
    ): Result<AuthenticationChallenge> = if (hasActiveChallenge) refresh() else begin(start)

    suspend fun refresh(): Result<AuthenticationChallenge> = withActive { reference ->
        gateway.challenge(reference)
    }

    suspend fun complete(code: String = "", useRecoveryCode: Boolean = false): Result<AuthenticationCompletion> =
        withActive { reference ->
            gateway.completeChallenge(reference, code, useRecoveryCode)
        }.also { result ->
            result.getOrNull()?.let { completion ->
                if (completion.state != AuthenticationChallengeState.WAITING &&
                    completion.state != AuthenticationChallengeState.APPROVED
                ) {
                    activeReference = null
                }
            }
        }

    suspend fun resend(): Result<AuthenticationChallenge> = withActive { reference ->
        gateway.resendChallenge(reference)
    }

    suspend fun poll(onUpdate: suspend (AuthenticationChallenge) -> Boolean) {
        val pollGeneration = generation
        while (activeReference != null && pollGeneration == generation) {
            delay(pollIntervalMillis)
            val state = refresh().getOrNull() ?: continue
            if (pollGeneration != generation || !onUpdate(state)) return
            if (state.state != AuthenticationChallengeState.WAITING && state.state != AuthenticationChallengeState.APPROVED) {
                activeReference = null
                return
            }
        }
    }

    suspend fun cancel() {
        cancelSilently()
    }

    private suspend fun cancelSilently() {
        generation++
        activeReference?.let { gateway.cancelChallenge(it) }
        activeReference = null
    }

    private suspend fun <T> withActive(block: suspend (AuthenticationChallengeReference) -> Result<T>): Result<T> {
        val reference = activeReference ?: return Result.failure(IllegalStateException("Authentication challenge is not active"))
        val requestGeneration = generation
        return block(reference).let { result ->
            if (requestGeneration == generation) result
            else Result.failure(IllegalStateException("Authentication challenge is no longer active"))
        }
    }

    private companion object {
        const val POLL_INTERVAL_MILLIS = 2_000L
    }
}
