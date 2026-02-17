package com.barkfluff.android.repository

import barkfluff.identity.AuthRequest
import barkfluff.identity.CreateTokenRequest
import barkfluff.identity.IdentityApiGrpcKt
import com.barkfluff.android.data.local.AppDataStore
import com.barkfluff.android.data.model.AuthResult
import com.barkfluff.android.data.remote.HeaderInterceptor
import io.grpc.StatusRuntimeException
import com.barkfluff.android.data.remote.GrpcChannelFactory
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class AuthRepository @Inject constructor(
    private val dataStore: AppDataStore,
    private val channelFactory: GrpcChannelFactory,
) {
    companion object {
        private const val OTP_NEEDED_CODE = "C1576884-12D8-4722-A7EE-9F9789AD1265"
        private const val OTP_WRONG_CODE = "803B632C-4457-4B05-9435-9C3DD0F41E00"
        private const val INVALID_LOGIN_CODE = "21BFB9B5-C377-45D1-9B15-6B7F3432B397"
    }

    private suspend fun getStub(): IdentityApiGrpcKt.IdentityApiCoroutineStub {
        val config = dataStore.getServerConfig()
            ?: error("Server not configured")
        val channel = channelFactory.getChannel(
            config.identityHost, config.identityPort, config.identityTls
        )
        return IdentityApiGrpcKt.IdentityApiCoroutineStub(channel)
    }

    suspend fun login(login: String, password: String, otpCode: String?): AuthResult {
        return try {
            val request = AuthRequest.newBuilder().apply {
                if (login.contains("@")) email = login else username = login
                this.password = password
                this.otpCode = otpCode ?: ""
            }.build()
            val response = getStub().auth(request)
            val accessExpiry = response.accessToken.expirationDate.seconds * 1000L
            val refreshToken = response.refreshToken.value
            val accessToken = response.accessToken.value
            dataStore.saveTokens(accessToken, accessExpiry, refreshToken)
            AuthResult.Success(accessToken, accessExpiry, refreshToken)
        } catch (e: Exception) {
            parseGrpcError(e)
        }
    }

    suspend fun ensureValidToken() {
        val expiry = dataStore.getAccessTokenExpiry()
        val now = System.currentTimeMillis()
        if (now >= expiry - 30_000L) {
            val refreshToken = dataStore.getRefreshToken()
            if (refreshToken.isEmpty()) return
            try {
                val request = CreateTokenRequest.newBuilder()
                    .setRefreshToken(refreshToken)
                    .build()
                val response = getStub().createToken(request)
                val newExpiry = response.accessToken.expirationDate.seconds * 1000L
                dataStore.saveAccessToken(response.accessToken.value, newExpiry)
            } catch (_: Exception) {
                // If refresh fails, let the next authenticated call fail
            }
        }
    }

    private fun parseGrpcError(e: Exception): AuthResult {
        val trailers = (e as? StatusRuntimeException)?.trailers
        val errorCode = trailers?.get(HeaderInterceptor.ERROR_CODE_KEY)
        return when (errorCode) {
            OTP_NEEDED_CODE -> AuthResult.NeedsOtp
            OTP_WRONG_CODE -> AuthResult.WrongOtp
            INVALID_LOGIN_CODE -> AuthResult.Error("Неверный логин или пароль")
            else -> AuthResult.Error(e.message ?: "Ошибка авторизации")
        }
    }
}
