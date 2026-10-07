package com.barkfluff.client.domain.auth

import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import com.barkfluff.client.domain.model.AuthenticationCapabilities
import com.barkfluff.client.domain.model.AuthenticationChallenge
import com.barkfluff.client.domain.model.AuthenticationChallengeReference
import com.barkfluff.client.domain.model.AuthenticationChallengeState
import com.barkfluff.client.domain.model.AuthenticationCompletion
import com.barkfluff.client.domain.model.AuthenticationFactor
import com.barkfluff.client.domain.model.AuthenticationLoginMode
import com.barkfluff.client.domain.model.OtpEnrollment
import com.barkfluff.client.domain.model.RegistrationRequest
import com.barkfluff.client.domain.model.SecuritySettings
import com.barkfluff.client.domain.model.SecuritySettingsUpdate
import com.barkfluff.client.domain.model.SignInRequest
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AuthenticationChallengeControllerTest {

    @Test
    fun `cancel revokes the active opaque challenge and prevents completion`() = runBlocking {
        val gateway = FakeGateway()
        val controller = AuthenticationChallengeController(gateway)

        controller.begin { beginSignIn(SignInRequest("alice", "password", AuthenticationLoginMode.PASSWORD)) }
        controller.cancel()

        assertEquals(listOf(gateway.reference), gateway.cancelled)
        assertTrue(controller.complete("123456").isFailure)
        assertEquals(0, gateway.completeCalls)
    }

    @Test
    fun `terminal completion clears the active challenge`() = runBlocking {
        val gateway = FakeGateway().apply {
            completion = AuthenticationCompletion(
                state = AuthenticationChallengeState.COMPLETED,
                session = null,
                securityProof = reference,
                recoveryCodes = listOf("code-1"),
                errorCode = "",
            )
        }
        val controller = AuthenticationChallengeController(gateway)

        controller.begin { beginPasswordRecovery("alice") }
        val completion = controller.complete("123456").getOrThrow()

        assertEquals(listOf("code-1"), completion.recoveryCodes)
        assertTrue(controller.resend().isFailure)
    }

    @Test
    fun `resume reuses an in-memory challenge instead of beginning another one`() = runBlocking {
        val gateway = FakeGateway()
        val controller = AuthenticationChallengeController(gateway)

        controller.begin { beginSignIn(SignInRequest("alice", "password", AuthenticationLoginMode.PASSWORD)) }
        val resumed = controller.resumeOrBegin {
            error("A retained challenge must not start another request")
        }

        assertTrue(resumed.isSuccess)
        assertEquals(1, gateway.beginSignInCalls)
        assertEquals(1, gateway.challengeCalls)
    }

    @Test
    fun `terminal poll result clears the active challenge even when UI stops polling`() = runBlocking {
        val gateway = FakeGateway().apply {
            currentChallenge = challenge.copy(state = AuthenticationChallengeState.EXPIRED)
        }
        val controller = AuthenticationChallengeController(gateway, pollIntervalMillis = 0)

        controller.begin { beginSignIn(SignInRequest("alice", "password", AuthenticationLoginMode.PASSWORD)) }
        controller.poll { false }

        assertTrue(controller.resend().isFailure)
    }

    private class FakeGateway : AuthenticationChallengeGateway {
        val reference = AuthenticationChallengeReference("challenge", "secret")
        val cancelled = mutableListOf<AuthenticationChallengeReference>()
        var completeCalls = 0
        var beginSignInCalls = 0
        var challengeCalls = 0
        var completion = AuthenticationCompletion(
            state = AuthenticationChallengeState.WAITING,
            session = null,
            securityProof = null,
            recoveryCodes = emptyList(),
            errorCode = "",
        )

        val challenge = AuthenticationChallenge(
            reference = reference,
            state = AuthenticationChallengeState.WAITING,
            factor = AuthenticationFactor.NONE,
            telegramUrl = "",
            expiresAtMillis = 0,
            availableFactors = emptyList(),
            needsCode = true,
            errorCode = "",
        )
        var currentChallenge = challenge

        override suspend fun capabilities() = Result.success(AuthenticationCapabilities(false, false, ""))
        override suspend fun beginRegistration(request: RegistrationRequest) = Result.success(currentChallenge)
        override suspend fun beginSignIn(request: SignInRequest): Result<AuthenticationChallenge> {
            beginSignInCalls++
            return Result.success(currentChallenge)
        }
        override suspend fun challenge(reference: AuthenticationChallengeReference): Result<AuthenticationChallenge> {
            challengeCalls++
            return Result.success(currentChallenge)
        }
        override suspend fun completeChallenge(reference: AuthenticationChallengeReference, code: String, useRecoveryCode: Boolean): Result<AuthenticationCompletion> {
            completeCalls++
            return Result.success(completion)
        }
        override suspend fun cancelChallenge(reference: AuthenticationChallengeReference): Result<AuthenticationChallenge> {
            cancelled += reference
            return Result.success(currentChallenge)
        }
        override suspend fun resendChallenge(reference: AuthenticationChallengeReference) = Result.success(currentChallenge)
        override suspend fun securitySettings(): Result<SecuritySettings> = unsupported()
        override suspend fun beginReauthentication(password: String, factor: AuthenticationFactor, useRecoveryCode: Boolean) = Result.success(currentChallenge)
        override suspend fun updateSecuritySettings(securityProof: AuthenticationChallengeReference, update: SecuritySettingsUpdate): Result<SecuritySettings> = unsupported()
        override suspend fun beginTelegramBinding(securityProof: AuthenticationChallengeReference) = Result.success(currentChallenge)
        override suspend fun unlinkTelegram(securityProof: AuthenticationChallengeReference, update: SecuritySettingsUpdate): Result<SecuritySettings> = unsupported()
        override suspend fun beginEmailBinding(securityProof: AuthenticationChallengeReference, email: String) = Result.success(currentChallenge)
        override suspend fun generateRecoveryCodes(securityProof: AuthenticationChallengeReference) = Result.success(emptyList<String>())
        override suspend fun beginPasswordRecovery(login: String) = Result.success(challenge)
        override suspend fun setRecoveredPassword(securityProof: AuthenticationChallengeReference, password: String) = Result.success(Unit)
        override suspend fun enableOtpVerification(factor: AuthenticationFactor, securityProof: AuthenticationChallengeReference): Result<OtpEnrollment> = unsupported()
        override suspend fun confirmOtpVerification(code: String, securityProof: AuthenticationChallengeReference) = Result.success(emptyList<String>())
        override suspend fun disableOtpVerification(factor: AuthenticationFactor, securityProof: AuthenticationChallengeReference) = Result.success(Unit)

        private fun <T> unsupported(): Result<T> = Result.failure(AssertionError("Unexpected gateway call"))
    }
}
