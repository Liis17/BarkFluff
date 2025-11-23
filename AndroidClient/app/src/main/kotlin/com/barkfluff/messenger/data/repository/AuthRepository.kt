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

    suspend fun logout() {
        sessionManager.clearSession()
    }
}
