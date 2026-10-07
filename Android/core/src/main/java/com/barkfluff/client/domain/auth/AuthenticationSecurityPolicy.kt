package com.barkfluff.client.domain.auth

import com.barkfluff.client.domain.model.AuthenticationFactor
import com.barkfluff.client.domain.model.AuthenticationLoginMode
import com.barkfluff.client.domain.model.SecuritySettings
import com.barkfluff.client.domain.model.SecuritySettingsUpdate

/** Pure settings mapper: Android never exposes or changes the desktop FastAuth preference. */
object AuthenticationSecurityPolicy {
    fun update(
        settings: SecuritySettings,
        loginMode: AuthenticationLoginMode = settings.loginMode,
        preferredFactor: AuthenticationFactor = settings.preferredFactor,
        telegramEnabled: Boolean = settings.telegramEnabled,
        telegramOtpEnabled: Boolean = settings.telegramOtpEnabled,
    ): SecuritySettingsUpdate = SecuritySettingsUpdate(
        loginMode = loginMode,
        preferredFactor = preferredFactor,
        telegramEnabled = telegramEnabled,
        telegramOtpEnabled = telegramOtpEnabled,
        fastAuthTelegramEnabled = settings.fastAuthTelegramEnabled,
    )
}
