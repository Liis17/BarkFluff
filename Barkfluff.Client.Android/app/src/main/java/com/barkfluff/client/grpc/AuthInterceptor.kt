package com.barkfluff.client.grpc

import android.content.Context
import android.util.Base64
import android.util.Log
import com.barkfluff.client.data.GlobalParam
import io.grpc.*
import io.grpc.Status.Code
import kotlinx.coroutines.runBlocking

/**
 * Interceptor для добавления access токена к gRPC запросам
 * и автоматического обновления токена при ошибках авторизации
 */
class AuthInterceptor(
    private val context: Context,
    private val grpcManager: GrpcManager
) : ClientInterceptor {

    companion object {
        private const val TAG = "AuthInterceptor"
        
        private val AUTHORIZATION_KEY: Metadata.Key<String> =
            Metadata.Key.of("authorization", Metadata.ASCII_STRING_MARSHALLER)
        
        // Error codes from x-error-code trailer
        private const val ERROR_UNAUTHORIZED = "UNAUTHORIZED"
        private const val ERROR_TOKEN_EXPIRED = "TOKEN_EXPIRED"
        private const val ERROR_INVALID_TOKEN = "INVALID_TOKEN"
        
        private val ERROR_CODE_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
    }

    private val globalParam = GlobalParam(context)

    override fun <ReqT, RespT> interceptCall(
        method: MethodDescriptor<ReqT, RespT>,
        callOptions: CallOptions,
        next: Channel
    ): ClientCall<ReqT, RespT> {
        return object : ForwardingClientCall.SimpleForwardingClientCall<ReqT, RespT>(
            next.newCall(method, callOptions)
        ) {
            private var responseListener: Listener<RespT>? = null

            override fun start(responseListener: Listener<RespT>, headers: Metadata) {
                this.responseListener = responseListener

                // Добавляем access токен к запросу
                val accessToken = globalParam.accessToken
                if (!accessToken.isNullOrBlank()) {
                    headers.put(AUTHORIZATION_KEY, "Bearer $accessToken")
                }

                // Добавляем метаданные устройства
                headers.put(key("x-device-id"), toBase64(globalParam.deviceId))
                headers.put(key("x-device-name"), toBase64(GlobalParam.getDeviceName()))
                headers.put(key("x-os-name"), toBase64(GlobalParam.getOsVersion()))
                headers.put(key("x-app-name"), toBase64(GlobalParam.getAppName()))
                headers.put(key("x-app-version"), toBase64(GlobalParam.getAppVersion(context)))
                headers.put(key("x-ip-address"), toBase64(globalParam.ipAddress))

                // Создаем listener для обработки ошибок
                val wrappedListener = object : io.grpc.ClientCall.Listener<RespT>() {
                    override fun onMessage(message: RespT) {
                        responseListener?.onMessage(message)
                    }

                    override fun onHeaders(headers: Metadata) {
                        responseListener?.onHeaders(headers)
                    }

                    override fun onClose(status: Status, trailers: Metadata) {
                        // Проверяем, является ли ошибкой авторизации
                        if (isAuthError(status, trailers)) {
                            Log.d(TAG, "Обнаружена ошибка авторизации, пытаемся обновить токен")
                            
                            // Пытаемся обновить токен синхронно (в рамках onClose это допустимо)
                            val refreshResult = runBlocking {
                                refreshAccessToken(globalParam.refreshToken ?: "", globalParam.refreshTokenExpiration)
                            }

                            if (refreshResult) {
                                Log.d(TAG, "Токен успешно обновлен, операция будет повторена")
                                // Токен обновлен, но мы не можем автоматически повторить вызов здесь
                                // Приложение должно обработать это на более высоком уровне
                            } else {
                                Log.e(TAG, "Не удалось обновить токен")
                            }
                        }
                        responseListener?.onClose(status, trailers)
                    }
                }

                super.start(wrappedListener, headers)
            }
        }
    }

    /**
     * Проверяет, является ли ошибка связанной с авторизацией
     */
    private fun isAuthError(status: Status, trailers: Metadata?): Boolean {
        if (status.code == Code.UNAUTHENTICATED || 
            status.code == Code.PERMISSION_DENIED) {
            return true
        }

        val errorCode = trailers?.get(ERROR_CODE_KEY)?.uppercase()
        return when (errorCode) {
            ERROR_UNAUTHORIZED, ERROR_TOKEN_EXPIRED, ERROR_INVALID_TOKEN -> true
            else -> false
        }
    }

    /**
     * Обновляет access токен используя refresh токен
     */
    private suspend fun refreshAccessToken(refreshToken: String, refreshTokenExpiration: Long): Boolean {
        return try {
            val identityAddress = globalParam.socketIdentity
            if (identityAddress.isBlank()) {
                return false
            }

            // Создаем Identity клиент если нужно
            if (grpcManager.identityClient == null) {
                val createResult = grpcManager.createIdentityClient(identityAddress)
                if (createResult.isFailure) {
                    return false
                }
            }

            // Обновляем токен
            val refreshResult = grpcManager.refreshAccessToken(refreshToken, refreshTokenExpiration)
            if (refreshResult.isSuccess) {
                val (newAccessToken, newAccessTokenExpiration, newRefreshToken, newRefreshTokenExpiration) = refreshResult.getOrNull()!!

                // Сохраняем новые токены
                globalParam.accessToken = newAccessToken
                globalParam.accessTokenExpiration = newAccessTokenExpiration
                globalParam.refreshToken = newRefreshToken
                globalParam.refreshTokenExpiration = newRefreshTokenExpiration

                true
            } else {
                false
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления токена", e)
            false
        }
    }

    private fun toBase64(value: String): String {
        return Base64.encodeToString(value.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)
    }

    private fun key(name: String): Metadata.Key<String> {
        return Metadata.Key.of(name, Metadata.ASCII_STRING_MARSHALLER)
    }
}
