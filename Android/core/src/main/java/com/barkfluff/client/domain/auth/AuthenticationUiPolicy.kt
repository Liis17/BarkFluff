package com.barkfluff.client.domain.auth

import com.barkfluff.client.domain.model.AuthenticationCapabilities
import com.barkfluff.client.domain.model.AuthenticationFactor
import com.barkfluff.client.domain.model.AuthenticationLoginMode

/** Server-capability rules kept independent from Android views. */
object AuthenticationUiPolicy {
    fun registrationConfirmationMethods(capabilities: AuthenticationCapabilities): List<AuthenticationFactor> = buildList {
        if (capabilities.emailAvailable) add(AuthenticationFactor.EMAIL)
        if (capabilities.telegramAvailable) add(AuthenticationFactor.TELEGRAM)
    }

    fun registrationLoginModes(method: AuthenticationFactor): List<AuthenticationLoginMode> = when (method) {
        AuthenticationFactor.EMAIL -> listOf(AuthenticationLoginMode.PASSWORD)
        AuthenticationFactor.TELEGRAM -> listOf(
            AuthenticationLoginMode.TELEGRAM_LOGIN,
            AuthenticationLoginMode.PASSWORD_SECOND_FACTOR,
        )
        else -> emptyList()
    }

    fun signInModes(capabilities: AuthenticationCapabilities): List<AuthenticationLoginMode> = buildList {
        add(AuthenticationLoginMode.PASSWORD)
        if (capabilities.telegramAvailable) add(AuthenticationLoginMode.TELEGRAM_LOGIN)
        add(AuthenticationLoginMode.PASSWORD_SECOND_FACTOR)
    }
}
