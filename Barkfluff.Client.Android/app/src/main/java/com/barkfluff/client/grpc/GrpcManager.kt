package com.barkfluff.client.grpc

import android.content.Context
import android.util.Base64
import android.util.Log
import barkfluff.beacon.BeaconApiGrpcKt
import barkfluff.beacon.BeaconApiOuterClass
import barkfluff.identity.IdentityApiGrpcKt
import barkfluff.identity.IdentityApiOuterClass
import barkfluff.navigator.NavigatorApiGrpcKt
import barkfluff.navigator.NavigatorApiOuterClass
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import io.grpc.*
import io.grpc.okhttp.OkHttpChannelBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

/**
 * Менеджер для работы с gRPC
 * Аналог WebApiClientManager из WPF клиента
 */
class GrpcManager {

    companion object {
        private const val TAG = "GrpcManager"
        const val DEFAULT_NAVIGATOR_URL = "navigator.barkfluff.com:64646"

        // Error codes from x-error-code trailer
        const val ERROR_OTP_CODE_NEEDED = "C1576884-12D8-4722-A7EE-9F9789AD1265"
        const val ERROR_NOT_VALID_OTP_CODE = "803B632C-4457-4B05-9435-9C3DD0F41E00"
        const val ERROR_INVALID_LOGIN_OR_PASSWORD = "21BFB9B5-C377-45D1-9B15-6B7F3432B397"

        private val ERROR_CODE_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
    }

    // gRPC каналы
    var navigatorChannel: ManagedChannel? = null
        private set
    var beaconChannel: ManagedChannel? = null
        private set
    var identityChannel: ManagedChannel? = null
        private set

    // gRPC клиенты
    var navigatorClient: NavigatorApiGrpcKt.NavigatorApiCoroutineStub? = null
        private set
    var beaconClient: BeaconApiGrpcKt.BeaconApiCoroutineStub? = null
        private set
    var identityClient: IdentityApiGrpcKt.IdentityApiCoroutineStub? = null
        private set

    /**
     * Создает только Beacon клиент для работы с Beacon API на сервере
     * Аналог CreateOnlyBeaconAC в WPF
     */
    fun createOnlyBeaconClient(beaconAddress: String): Result<Unit> {
        if (beaconAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Beacon сервера не указан"))
        }

        return try {
            val address = ensureHttpPrefix(beaconAddress)
            beaconChannel = createChannel(address)
            beaconClient = BeaconApiGrpcKt.BeaconApiCoroutineStub(beaconChannel!!)
            Log.d(TAG, "Beacon клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Beacon клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу: ${e.message}"))
        }
    }

    /**
     * Создает Navigator клиент для работы с навигатором
     * Аналог CreateNavigatorAC в WPF
     */
    fun createNavigatorClient(navigatorUrl: String = DEFAULT_NAVIGATOR_URL): Result<Unit> {
        if (navigatorUrl.isBlank()) {
            return Result.failure(IllegalArgumentException("URL навигатора не может быть пустым"))
        }

        return try {
            val address = ensureHttpPrefix(navigatorUrl)
            navigatorChannel = createChannel(address)
            navigatorClient = NavigatorApiGrpcKt.NavigatorApiCoroutineStub(navigatorChannel!!)
            Log.d(TAG, "Navigator клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Navigator клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу: ${e.message}"))
        }
    }

    /**
     * Создает Identity клиент для авторизации
     */
    fun createIdentityClient(identityAddress: String): Result<Unit> {
        if (identityAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Identity сервера не указан"))
        }

        return try {
            val address = ensureHttpPrefix(identityAddress)
            identityChannel = createChannel(address)
            identityClient = IdentityApiGrpcKt.IdentityApiCoroutineStub(identityChannel!!)
            Log.d(TAG, "Identity клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Identity клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу авторизации: ${e.message}"))
        }
    }

    /**
     * Авторизация пользователя
     * @param email email (nullable, if username is used)
     * @param username username (nullable, if email is used)
     * @param password пароль
     * @param otpCode код 2FA (nullable)
     * @param context контекст для получения метаданных устройства
     */
    suspend fun auth(
        email: String?,
        username: String?,
        password: String,
        otpCode: String?,
        context: Context
    ): AuthResult = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext AuthResult.Error("Identity клиент не создан")
            }

            val requestBuilder = IdentityApiOuterClass.AuthRequest.newBuilder()
                .setPassword(password)

            if (!email.isNullOrBlank()) {
                requestBuilder.email = email
            } else if (!username.isNullOrBlank()) {
                requestBuilder.username = username
            }

            if (!otpCode.isNullOrBlank()) {
                requestBuilder.otpCode = otpCode
            }

            val request = requestBuilder.build()

            // Add device metadata headers via interceptor
            val globalParam = GlobalParam(context)
            val headerInterceptor = object : ClientInterceptor {
                override fun <ReqT, RespT> interceptCall(
                    method: MethodDescriptor<ReqT, RespT>,
                    callOptions: CallOptions,
                    next: Channel
                ): ClientCall<ReqT, RespT> {
                    return object : ForwardingClientCall.SimpleForwardingClientCall<ReqT, RespT>(
                        next.newCall(method, callOptions)
                    ) {
                        override fun start(responseListener: Listener<RespT>, headers: Metadata) {
                            headers.put(key("x-device-id"), toBase64(globalParam.deviceId))
                            headers.put(key("x-device-name"), toBase64(GlobalParam.getDeviceName()))
                            headers.put(key("x-os-name"), toBase64(GlobalParam.getOsVersion()))
                            headers.put(key("x-app-name"), toBase64(GlobalParam.getAppName()))
                            headers.put(key("x-app-version"), toBase64(GlobalParam.getAppVersion(context)))
                            headers.put(key("x-ip-address"), toBase64(globalParam.ipAddress))
                            super.start(responseListener, headers)
                        }
                    }
                }
            }

            val interceptedChannel = ClientInterceptors.intercept(identityChannel!!, headerInterceptor)
            val stub = IdentityApiGrpcKt.IdentityApiCoroutineStub(interceptedChannel)
            val response = stub.auth(request)

            AuthResult.Success(
                accessToken = response.accessToken.value,
                accessTokenExpiration = response.accessToken.expirationDate.seconds * 1000,
                refreshToken = response.refreshToken.value,
                refreshTokenExpiration = response.refreshToken.expirationDate.seconds * 1000
            )
        } catch (e: StatusException) {
            handleAuthError(e)
        } catch (e: StatusRuntimeException) {
            handleAuthRuntimeError(e)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка авторизации", e)
            AuthResult.Error("Ошибка подключения: ${e.message}")
        }
    }

    private fun handleAuthError(e: StatusException): AuthResult {
        val errorCode = e.trailers?.get(ERROR_CODE_KEY)?.uppercase()
        Log.d(TAG, "Auth error: status=${e.status}, errorCode=$errorCode")

        return when (errorCode) {
            ERROR_OTP_CODE_NEEDED -> AuthResult.OtpRequired
            ERROR_NOT_VALID_OTP_CODE -> AuthResult.Error("Неверный код 2FA")
            ERROR_INVALID_LOGIN_OR_PASSWORD -> AuthResult.Error("Неверный логин или пароль")
            else -> AuthResult.Error(e.status.description ?: "Ошибка авторизации")
        }
    }

    private fun handleAuthRuntimeError(e: StatusRuntimeException): AuthResult {
        val errorCode = e.trailers?.get(ERROR_CODE_KEY)?.uppercase()
        Log.d(TAG, "Auth runtime error: status=${e.status}, errorCode=$errorCode")

        return when (errorCode) {
            ERROR_OTP_CODE_NEEDED -> AuthResult.OtpRequired
            ERROR_NOT_VALID_OTP_CODE -> AuthResult.Error("Неверный код 2FA")
            ERROR_INVALID_LOGIN_OR_PASSWORD -> AuthResult.Error("Неверный логин или пароль")
            else -> AuthResult.Error(e.status.description ?: "Ошибка авторизации")
        }
    }

    /**
     * Получает список серверов из навигатора
     * Аналог GetServerList в WebApiServerManager
     */
    suspend fun getServerList(): Result<List<ServerDataElement>> = withContext(Dispatchers.IO) {
        try {
            if (navigatorClient == null) {
                return@withContext Result.failure(IllegalStateException("Navigator клиент не создан"))
            }

            val request = NavigatorApiOuterClass.ListServersRequest.newBuilder().build()
            val response = navigatorClient!!.listServers(request)

            val serverList = response.serversList.map { server ->
                ServerDataElement(
                    ip = "${server.beaconUri.host}:${server.beaconUri.port}",
                    title = server.name,
                    description = server.description,
                    userCount = server.accountsCount.toString(),
                    publicName = server.serverPublicName,
                    location = server.location,
                    hexColor = server.color.mainHex
                )
            }

            Log.d(TAG, "Получено ${serverList.size} серверов")
            Result.success(serverList)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения списка серверов", e)
            Result.failure(Exception("Ошибка получения списка серверов: ${e.message}"))
        }
    }

    /**
     * Получает информацию о сервере через Beacon API
     * Аналог GetServerInfo в WebApiServerManager
     */
    suspend fun getServerInfo(): Result<ServerInfo> = withContext(Dispatchers.IO) {
        try {
            if (beaconClient == null) {
                return@withContext Result.failure(IllegalStateException("Beacon клиент не создан"))
            }

            val request = BeaconApiOuterClass.GetServerInfoRequest.newBuilder().build()
            val response = beaconClient!!.getServerInfo(request)

            val serverInfo = ServerInfo(
                name = response.name,
                description = response.description,
                color = ClientColors(
                    response.color.liteHex,
                    response.color.mainHex,
                    response.color.hardHex
                ),
                identityEndpoint = "${response.identity.endpoint.host}:${response.identity.endpoint.port}",
                usersEndpoint = "${response.users.endpoint.host}:${response.users.endpoint.port}",
                filesEndpoint = "${response.files.endpoint.host}:${response.files.endpoint.port}",
                messagesEndpoint = "${response.messages.endpoint.host}:${response.messages.endpoint.port}",
                updatesEndpoint = "${response.updates.endpoint.host}:${response.updates.endpoint.port}",
                onlinerEndpoint = "${response.onliner.endpoint.host}:${response.onliner.endpoint.port}"
            )

            Log.d(TAG, "Получена информация о сервере: ${serverInfo.name}")
            Result.success(serverInfo)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения информации о сервере: ${e.message}", e)
            var cause = e.cause
            while (cause != null) {
                Log.e(TAG, "  Caused by: ${cause.javaClass.simpleName}: ${cause.message}")
                cause = cause.cause
            }
            Result.failure(Exception("Ошибка получения информации о сервере: ${e.message}"))
        }
    }

    private fun createChannel(address: String): ManagedChannel {
        val url = ensureHttpPrefix(address)
        val useTls = url.startsWith("https://")
        val hostPort = url.removePrefix("http://").removePrefix("https://")
        val parts = hostPort.split(":")
        val host = parts[0]
        val port = parts[1].toInt()

        Log.d(TAG, "Создание gRPC канала: host=$host, port=$port, tls=$useTls")

        val builder = OkHttpChannelBuilder.forAddress(host, port)
        if (useTls) {
            // Доверяем всем сертификатам (сервер использует самоподписанный сертификат)
            val trustManager = object : X509TrustManager {
                override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
                override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
                override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
            }
            val sslContext = SSLContext.getInstance("TLS")
            sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
            builder.sslSocketFactory(sslContext.socketFactory)
        } else {
            builder.usePlaintext()
        }
        return builder.build()
    }

    private fun ensureHttpPrefix(url: String): String {
        return if (!url.startsWith("http://") && !url.startsWith("https://")) {
            "http://$url"
        } else {
            url
        }
    }

    private fun toBase64(value: String): String {
        return Base64.encodeToString(value.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)
    }

    private fun key(name: String): Metadata.Key<String> {
        return Metadata.Key.of(name, Metadata.ASCII_STRING_MARSHALLER)
    }

    /**
     * Закрывает все gRPC каналы
     * Аналог Dispose в WPF
     */
    fun shutdown() {
        navigatorChannel?.shutdown()
        beaconChannel?.shutdown()
        identityChannel?.shutdown()
    }

    data class ServerInfo(
        val name: String,
        val description: String,
        val color: ClientColors,
        val identityEndpoint: String,
        val usersEndpoint: String,
        val filesEndpoint: String,
        val messagesEndpoint: String,
        val updatesEndpoint: String,
        val onlinerEndpoint: String
    )

    sealed class AuthResult {
        data class Success(
            val accessToken: String,
            val accessTokenExpiration: Long,
            val refreshToken: String,
            val refreshTokenExpiration: Long
        ) : AuthResult()

        data object OtpRequired : AuthResult()
        data class Error(val message: String) : AuthResult()
    }
}
