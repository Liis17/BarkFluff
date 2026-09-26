package com.barkfluff.client.domain.auth

import com.barkfluff.client.domain.model.AuthenticationCapabilities
import com.barkfluff.client.domain.model.AuthenticationFactor
import com.barkfluff.client.domain.model.AuthenticationLoginMode
import com.barkfluff.client.domain.model.SecuritySettings
import org.junit.Assert.assertEquals
import org.junit.Test

class AuthenticationUiPolicyTest {
    @Test
    fun `telegram capability gates Telegram registration and login choices`() {
        val emailOnly = AuthenticationCapabilities(emailAvailable = true, telegramAvailable = false, telegramBotUsername = "")

        assertEquals(listOf(AuthenticationFactor.EMAIL), AuthenticationUiPolicy.registrationConfirmationMethods(emailOnly))
        assertEquals(
            listOf(AuthenticationLoginMode.PASSWORD, AuthenticationLoginMode.PASSWORD_SECOND_FACTOR),
            AuthenticationUiPolicy.signInModes(emailOnly),
        )
    }

    @Test
    fun `telegram registration permits telegram only and password plus factor`() {
        assertEquals(
            listOf(AuthenticationLoginMode.TELEGRAM_LOGIN, AuthenticationLoginMode.PASSWORD_SECOND_FACTOR),
            AuthenticationUiPolicy.registrationLoginModes(AuthenticationFactor.TELEGRAM),
        )
    }

    @Test
    fun `security update always preserves desktop FastAuth`() {
        val settings = SecuritySettings(
            loginMode = AuthenticationLoginMode.PASSWORD,
            preferredFactor = AuthenticationFactor.EMAIL,
            authenticatorEnabled = false,
            emailEnabled = true,
            telegramLinked = true,
            telegramEnabled = true,
            telegramOtpEnabled = false,
            fastAuthTelegramEnabled = true,
            telegramUsername = "barkfluff",
            verifiedEmail = "user@example.com",
            remainingRecoveryCodes = 8,
        )

        val update = AuthenticationSecurityPolicy.update(settings, telegramOtpEnabled = true)

        assertEquals(true, update.fastAuthTelegramEnabled)
        assertEquals(true, update.telegramOtpEnabled)
    }
}
