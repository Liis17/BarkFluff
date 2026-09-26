package com.barkfluff.client.domain.model

/** The account policy selected by the user and enforced by Identity. */
enum class AuthenticationLoginMode {
    PASSWORD,
    TELEGRAM_LOGIN,
    PASSWORD_SECOND_FACTOR,
}

enum class AuthenticationFactor {
    NONE,
    AUTHENTICATOR,
    EMAIL,
    TELEGRAM,
}

enum class AuthenticationChallengeState {
    WAITING,
    APPROVED,
    REJECTED,
    EXPIRED,
    COMPLETED,
    CANCELLED,
}

/** Opaque, short-lived reference. It must stay in memory only. */
data class AuthenticationChallengeReference(
    val id: String,
    val secret: String,
)

data class AuthenticationCapabilities(
    val emailAvailable: Boolean,
    val telegramAvailable: Boolean,
    val telegramBotUsername: String,
)

data class AuthenticationChallenge(
    val reference: AuthenticationChallengeReference,
    val state: AuthenticationChallengeState,
    val factor: AuthenticationFactor,
    val telegramUrl: String,
    val expiresAtMillis: Long,
    val availableFactors: List<AuthenticationFactor>,
    val needsCode: Boolean,
    val errorCode: String,
)

data class AuthenticationCompletion(
    val state: AuthenticationChallengeState,
    val session: AuthSession?,
    val securityProof: AuthenticationChallengeReference?,
    val recoveryCodes: List<String>,
    val errorCode: String,
)

data class RegistrationRequest(
    val username: String,
    val password: String,
    val firstName: String,
    val lastName: String,
    val email: String,
    val confirmationMethod: AuthenticationFactor,
    val loginMode: AuthenticationLoginMode,
)

data class SignInRequest(
    val login: String,
    val password: String,
    val loginMode: AuthenticationLoginMode,
    val factor: AuthenticationFactor = AuthenticationFactor.NONE,
    val useRecoveryCode: Boolean = false,
)

data class SecuritySettings(
    val loginMode: AuthenticationLoginMode,
    val preferredFactor: AuthenticationFactor,
    val authenticatorEnabled: Boolean,
    val emailEnabled: Boolean,
    val telegramLinked: Boolean,
    val telegramEnabled: Boolean,
    val telegramOtpEnabled: Boolean,
    /** Desktop-only policy. Android always preserves this value when it saves settings. */
    val fastAuthTelegramEnabled: Boolean,
    val telegramUsername: String,
    val verifiedEmail: String,
    val remainingRecoveryCodes: Int,
)

data class SecuritySettingsUpdate(
    val loginMode: AuthenticationLoginMode,
    val preferredFactor: AuthenticationFactor,
    val telegramEnabled: Boolean,
    val telegramOtpEnabled: Boolean,
    val fastAuthTelegramEnabled: Boolean,
)

data class OtpEnrollment(
    val qrBase64: String,
    val manualCode: String,
)
