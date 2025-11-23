package com.barkfluff.messenger.data.repository

import com.barkfluff.messenger.data.local.SessionManager
import com.barkfluff.messenger.domain.model.AuthToken
import com.barkfluff.messenger.domain.model.User
import com.google.protobuf.timestamp
import io.grpc.ManagedChannel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import javax.inject.Inject
import javax.inject.Named
import kotlinx.coroutines.flow.first

class AuthRepository @Inject constructor(
    @Named("identityChannel") private val identityChannel: ManagedChannel,
    @Named("usersChannel") private val usersChannel: ManagedChannel,
    private val sessionManager: SessionManager
) {

    suspend fun login(
        emailOrUsername: String,
        password: String,
        otpCode: String? = null
    ): Result<AuthToken> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.AuthRequest.newBuilder()
                .setEmailOrUsername(emailOrUsername)
                .setPassword(password)
                .apply {
                    otpCode?.let { setOtpCode(it) }
                }
                .build()

            val response = stub.auth(request)

            val token = AuthToken(
                accessToken = response.accessToken.value,
                refreshToken = response.refreshToken.value,
                accessTokenExpiration = Instant.ofEpochSecond(
                    response.accessToken.expirationDate.seconds,
                    response.accessToken.expirationDate.nanos.toLong()
                ),
                refreshTokenExpiration = Instant.ofEpochSecond(
                    response.refreshToken.expirationDate.seconds,
                    response.refreshToken.expirationDate.nanos.toLong()
                )
            )

            sessionManager.saveToken(token)
            sessionManager.saveUserId(response.userId)

            Result.success(token)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun register(
        email: String,
        username: String,
        password: String,
        firstName: String,
        lastName: String
    ): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.CreateAccountRequest.newBuilder()
                .setEmail(email)
                .setUsername(username)
                .setPassword(password)
                .setFirstName(firstName)
                .setLastName(lastName)
                .build()

            stub.createAccount(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun confirmAccount(
        email: String,
        code: String
    ): Result<AuthToken> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.ConfirmAccountRequest.newBuilder()
                .setEmail(email)
                .setCode(code)
                .build()

            val response = stub.confirmAccount(request)

            val token = AuthToken(
                accessToken = response.accessToken.value,
                refreshToken = response.refreshToken.value,
                accessTokenExpiration = Instant.ofEpochSecond(
                    response.accessToken.expirationDate.seconds,
                    response.accessToken.expirationDate.nanos.toLong()
                ),
                refreshTokenExpiration = Instant.ofEpochSecond(
                    response.refreshToken.expirationDate.seconds,
                    response.refreshToken.expirationDate.nanos.toLong()
                )
            )

            sessionManager.saveToken(token)
            sessionManager.saveUserId(response.userId)

            Result.success(token)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun refreshToken(): Result<AuthToken> = withContext(Dispatchers.IO) {
        try {
            val currentToken = sessionManager.authToken.kotlinx.coroutines.flow.first()
                ?: return@withContext Result.failure(Exception("No token found"))

            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.CreateTokenRequest.newBuilder()
                .setRefreshToken(currentToken.refreshToken)
                .build()

            val response = stub.createToken(request)

            val newToken = AuthToken(
                accessToken = response.accessToken.value,
                refreshToken = currentToken.refreshToken,
                accessTokenExpiration = Instant.ofEpochSecond(
                    response.accessToken.expirationDate.seconds,
                    response.accessToken.expirationDate.nanos.toLong()
                ),
                refreshTokenExpiration = currentToken.refreshTokenExpiration
            )

            sessionManager.saveToken(newToken)

            Result.success(newToken)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun resetPassword(email: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.ResetPasswordRequest.newBuilder()
                .setEmail(email)
                .build()

            stub.resetPassword(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun confirmResetPassword(email: String, code: String, newPassword: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.ConfirmResetPasswordRequest.newBuilder()
                .setEmail(email)
                .setOtpCode(code)
                .setNewPassword(newPassword)
                .build()

            stub.confirmResetPassword(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun setPassword(oldPassword: String, newPassword: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.SetPasswordRequest.newBuilder()
                .setOldPassword(oldPassword)
                .setNewPassword(newPassword)
                .build()

            stub.setPassword(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun getActiveSessions(): Result<List<ActiveSession>> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.GetActiveSessionsRequest.newBuilder().build()
            val response = stub.getActiveSessions(request)

            val sessions = response.sessionsList.map { session ->
                ActiveSession(
                    id = session.id,
                    deviceName = session.deviceName,
                    ip = session.ip,
                    createdAt = Instant.ofEpochSecond(
                        session.createdAt.seconds,
                        session.createdAt.nanos.toLong()
                    ),
                    isCurrent = session.isCurrent
                )
            }

            Result.success(sessions)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun removeActiveSession(sessionId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.RemoveActiveSessionRequest.newBuilder()
                .setSessionId(sessionId)
                .build()

            stub.removeActiveSession(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun enableOtp(otpType: OtpType): Result<OtpSetupInfo> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val typeProto = when (otpType) {
                OtpType.AUTHENTICATOR -> identity_api.IdentityApi.OtpTypeId.AUTHENTICATOR
                OtpType.EMAIL -> identity_api.IdentityApi.OtpTypeId.EMAIL
            }

            val request = identity_api.IdentityApi.EnableOtpVerificationRequest.newBuilder()
                .setType(typeProto)
                .build()

            val response = stub.enableOtpVerification(request)

            Result.success(
                OtpSetupInfo(
                    secret = response.secret,
                    qrCodeUrl = response.qrCode.takeIf { it.isNotBlank() }
                )
            )
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun confirmOtp(otpType: OtpType, code: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val typeProto = when (otpType) {
                OtpType.AUTHENTICATOR -> identity_api.IdentityApi.OtpTypeId.AUTHENTICATOR
                OtpType.EMAIL -> identity_api.IdentityApi.OtpTypeId.EMAIL
            }

            val request = identity_api.IdentityApi.ConfirmOtpVerificationRequest.newBuilder()
                .setType(typeProto)
                .setOtpCode(code)
                .build()

            stub.confirmOtpVerification(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun disableOtp(otpType: OtpType): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val typeProto = when (otpType) {
                OtpType.AUTHENTICATOR -> identity_api.IdentityApi.OtpTypeId.AUTHENTICATOR
                OtpType.EMAIL -> identity_api.IdentityApi.OtpTypeId.EMAIL
            }

            val request = identity_api.IdentityApi.DisableOtpVerificationRequest.newBuilder()
                .setType(typeProto)
                .build()

            stub.disableOtpVerification(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun listOtpVerification(): Result<List<OtpMethod>> = withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)
            val request = identity_api.IdentityApi.ListOtpVerificationRequest.newBuilder().build()
            val response = stub.listOtpVerification(request)

            val methods = response.methodsList.map { methodProto ->
                OtpMethod(
                    type = when (methodProto.type) {
                        identity_api.IdentityApi.OtpTypeId.AUTHENTICATOR -> OtpType.AUTHENTICATOR
                        identity_api.IdentityApi.OtpTypeId.EMAIL -> OtpType.EMAIL
                        else -> OtpType.EMAIL
                    },
                    isEnabled = methodProto.isEnabled
                )
            }

            Result.success(methods)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun logout() {
        sessionManager.clearSession()
    }
}

data class ActiveSession(
    val id: String,
    val deviceName: String,
    val ip: String,
    val createdAt: Instant,
    val isCurrent: Boolean
)

enum class OtpType {
    AUTHENTICATOR,
    EMAIL
}

data class OtpSetupInfo(
    val secret: String,
    val qrCodeUrl: String?
)

data class OtpMethod(
    val type: OtpType,
    val isEnabled: Boolean
)
