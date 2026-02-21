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
import barkfluff.users.UsersApiGrpcKt
import barkfluff.users.UsersApiOuterClass
import barkfluff.files.FilesApiGrpcKt
import barkfluff.files.FilesApiOuterClass
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import io.grpc.*
import io.grpc.ManagedChannel
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
    var navigatorChannel: Channel? = null
        private set
    var beaconChannel: Channel? = null
        private set
    var identityChannel: Channel? = null
        private set
    var usersChannel: Channel? = null
        private set
    var filesChannel: Channel? = null
        private set

    // gRPC клиенты
    var navigatorClient: NavigatorApiGrpcKt.NavigatorApiCoroutineStub? = null
        private set
    var beaconClient: BeaconApiGrpcKt.BeaconApiCoroutineStub? = null
        private set
    var identityClient: IdentityApiGrpcKt.IdentityApiCoroutineStub? = null
        private set
    var usersClient: UsersApiGrpcKt.UsersApiCoroutineStub? = null
        private set
    var filesClient: FilesApiGrpcKt.FilesApiCoroutineStub? = null
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
            val channel = createChannel(address)
            beaconChannel = channel
            beaconClient = BeaconApiGrpcKt.BeaconApiCoroutineStub(channel)
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
            val channel = createChannel(address)
            navigatorChannel = channel
            navigatorClient = NavigatorApiGrpcKt.NavigatorApiCoroutineStub(channel)
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
    fun createIdentityClient(identityAddress: String, context: Context? = null): Result<Unit> {
        if (identityAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Identity сервера не указан"))
        }

        return try {
            val address = ensureHttpPrefix(identityAddress)
            val channel = createChannel(address)
            
            // Добавляем auth interceptor если передан контекст
            val interceptedChannel = if (context != null) {
                ClientInterceptors.intercept(channel, AuthInterceptor(context, this))
            } else {
                channel
            }
            
            identityChannel = interceptedChannel
            identityClient = IdentityApiGrpcKt.IdentityApiCoroutineStub(interceptedChannel)
            Log.d(TAG, "Identity клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Identity клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу авторизации: ${e.message}"))
        }
    }

    /**
     * Создает Users клиент для работы с пользователями
     */
    fun createUsersClient(usersAddress: String, context: Context? = null): Result<Unit> {
        if (usersAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Users сервера не указан"))
        }

        return try {
            val address = ensureHttpPrefix(usersAddress)
            val channel = createChannel(address)
            
            // Добавляем auth interceptor если передан контекст
            val interceptedChannel = if (context != null) {
                ClientInterceptors.intercept(channel, AuthInterceptor(context, this))
            } else {
                channel
            }
            
            usersChannel = interceptedChannel
            usersClient = UsersApiGrpcKt.UsersApiCoroutineStub(interceptedChannel)
            Log.d(TAG, "Users клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Users клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу пользователей: ${e.message}"))
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
     * Обновляет access токен используя refresh токен
     * Аналог TokenUpdate в WebApiTokenManager
     */
    suspend fun refreshAccessToken(refreshToken: String, currentRefreshTokenExpiration: Long = 0L): Result<RefreshTokenResult> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.CreateTokenRequest.newBuilder()
                .setRefreshToken(refreshToken)
                .build()

            val response = identityClient!!.createToken(request)

            // CreateTokenResponse возвращает только accessToken, refresh token не обновляется
            Result.success(
                RefreshTokenResult(
                    accessToken = response.accessToken.value,
                    accessTokenExpiration = response.accessToken.expirationDate.seconds * 1000,
                    refreshToken = refreshToken, // Используем тот же refresh token
                    refreshTokenExpiration = currentRefreshTokenExpiration // Используем существующее время истечения
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления токена", e)
            Result.failure(Exception("Ошибка обновления токена: ${e.message}"))
        }
    }

    /**
     * Получает данные текущего пользователя
     * Аналог GetUserData в WebApiUserManager
     */
    suspend fun getCurrentUserData(): Result<UserData> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.GetUserRequest.newBuilder()
                .setUserId(0) // 0 означает текущего пользователя
                .build()

            val response = usersClient!!.getUser(request)
            val user = response.user

            Result.success(
                UserData(
                    userId = user.id,
                    username = user.username,
                    firstName = user.firstName,
                    lastName = user.lastName,
                    bio = user.bio,
                    profilePictureUrl = user.profilePicture,
                    profilePicturePreviewUrl = user.profilePicturePreview,
                    registrationDate = user.registrationDate.seconds * 1000
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения данных пользователя", e)
            Result.failure(Exception("Ошибка получения данных пользователя: ${e.message}"))
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
        navigatorChannel?.let {
            if (it is ManagedChannel) {
                it.shutdown()
            }
        }
        beaconChannel?.let {
            if (it is ManagedChannel) {
                it.shutdown()
            }
        }
        identityChannel?.let {
            if (it is ManagedChannel) {
                it.shutdown()
            }
        }
        usersChannel?.let {
            if (it is ManagedChannel) {
                it.shutdown()
            }
        }
        filesChannel?.let {
            if (it is ManagedChannel) {
                it.shutdown()
            }
        }
    }

    /**
     * Создает Files клиент для работы с файлами
     */
    fun createFilesClient(filesAddress: String, context: Context? = null): Result<Unit> {
        if (filesAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Files сервера не указан"))
        }

        return try {
            val address = ensureHttpPrefix(filesAddress)
            val channel = createChannel(address)
            
            val interceptedChannel = if (context != null) {
                ClientInterceptors.intercept(channel, AuthInterceptor(context, this))
            } else {
                channel
            }
            
            filesChannel = interceptedChannel
            filesClient = FilesApiGrpcKt.FilesApiCoroutineStub(interceptedChannel)
            Log.d(TAG, "Files клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Files клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу файлов: ${e.message}"))
        }
    }

    /**
     * Проверяет существует ли email
     * Аналог CheckEmail в WebApiUserManager
     */
    suspend fun checkEmail(email: String): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.CheckExistEmailRequest.newBuilder()
                .setEmail(email.lowercase())
                .build()

            val response = usersClient!!.checkExistEmail(request)
            Result.success(response.exist)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка проверки email", e)
            Result.failure(Exception("Ошибка проверки email: ${e.message}"))
        }
    }

    /**
     * Проверяет существует ли username
     * Аналог CheckUsername в WebApiUserManager
     */
    suspend fun checkUsername(username: String): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.CheckExistUsernameRequest.newBuilder()
                .setUsername(username.lowercase())
                .build()

            val response = usersClient!!.checkExistUsername(request)
            Result.success(response.exist)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка проверки username", e)
            Result.failure(Exception("Ошибка проверки username: ${e.message}"))
        }
    }

    /**
     * Создает аккаунт (первый этап регистрации)
     * Аналог CreateAccount в WebApiRegistrationManager
     */
    suspend fun createAccount(firstName: String, lastName: String, email: String, login: String): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.CreateAccountRequest.newBuilder()
                .setFirstName(firstName)
                .setLastName(lastName)
                .setUsername(login.lowercase())
                .setEmail(email.lowercase())
                .build()

            val response = identityClient!!.createAccount(request)
            Result.success(response.codeId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания аккаунта", e)
            Result.failure(Exception("Ошибка создания аккаунта: ${e.message}"))
        }
    }

    /**
     * Подтверждает аккаунт кодом с почты
     * Аналог ConfirmAccount в WebApiRegistrationManager
     */
    suspend fun confirmAccount(codeId: String, verificationCode: String): Result<ConfirmAccountResult> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.ConfirmAccountRequest.newBuilder()
                .setCodeId(codeId)
                .setCodeValue(verificationCode)
                .build()

            val response = identityClient!!.confirmAccount(request)
            
            Result.success(
                ConfirmAccountResult(
                    refreshToken = response.refreshToken.value,
                    refreshTokenExpiration = response.refreshToken.expirationDate.seconds * 1000
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка подтверждения аккаунта", e)
            Result.failure(Exception("Ошибка подтверждения аккаунта: ${e.message}"))
        }
    }

    /**
     * Устанавливает аватар пользователя из file_id
     * Аналог SetProfilePicture в WebApiUserManager
     */
    suspend fun setProfilePicture(fileId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.SetProfilePictureRequest.newBuilder()
                .setFileId(fileId)
                .build()

            usersClient!!.setProfilePicture(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка установки аватара", e)
            Result.failure(Exception("Ошибка установки аватара: ${e.message}"))
        }
    }

    /**
     * Получает URL для загрузки файла
     */
    suspend fun getUploadUrl(fileType: FilesApiOuterClass.UploadFileType): Result<UploadUrlResult> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files клиент не создан"))
            }

            val request = FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                .setFileType(fileType)
                .build()

            val response = filesClient!!.getUploadUrl(request)
            
            Result.success(
                UploadUrlResult(
                    url = response.url,
                    fileId = response.fileId
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения URL загрузки", e)
            Result.failure(Exception("Ошибка получения URL загрузки: ${e.message}"))
        }
    }

    /**
     * Устанавливает пароль для пользователя
     * Аналог SetPassword в WebApiPasswordManager
     */
    suspend fun setPassword(password: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.SetPasswordRequest.newBuilder()
                .setPassword(password)
                .build()

            identityClient!!.setPassword(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка установки пароля", e)
            Result.failure(Exception("Ошибка установки пароля: ${e.message}"))
        }
    }

    /**
     * Запрашивает QR-код для настройки 2FA
     * Аналог OtpReceipt в WebApiAuthManager
     */
    suspend fun getOtpSetup(): Result<OtpSetupResult> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.EnableOtpVerificationRequest.newBuilder()
                .setOtpType(IdentityApiOuterClass.OtpTypeId.Authenticator)
                .build()

            val response = identityClient!!.enableOtpVerification(request)
            
            Result.success(
                OtpSetupResult(
                    qrBase64 = response.otpQr,
                    justCode = response.otpCode
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения 2FA setup", e)
            Result.failure(Exception("Ошибка получения 2FA setup: ${e.message}"))
        }
    }

    /**
     * Подтверждает настройку 2FA
     * Аналог OtpAccept в WebApiAuthManager
     */
    suspend fun confirmOtpSetup(code: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.ConfirmOtpVerificationRequest.newBuilder()
                .setOtpCode(code)
                .build()

            identityClient!!.confirmOtpVerification(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка подтверждения 2FA", e)
            Result.failure(Exception("Ошибка подтверждения 2FA: ${e.message}"))
        }
    }

    /**
     * Изменяет био пользователя
     * Аналог ChangeBio в WebApiUserManager
     */
    suspend fun changeBio(bio: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.ChangeBioRequest.newBuilder()
                .setBio(bio)
                .build()

            usersClient!!.changeBio(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка изменения био", e)
            Result.failure(Exception("Ошибка изменения био: ${e.message}"))
        }
    }

    data class RefreshTokenResult(
        val accessToken: String,
        val accessTokenExpiration: Long,
        val refreshToken: String,
        val refreshTokenExpiration: Long
    )

    data class ConfirmAccountResult(
        val refreshToken: String,
        val refreshTokenExpiration: Long
    )

    data class UploadUrlResult(
        val url: String,
        val fileId: String
    )

    data class OtpSetupResult(
        val qrBase64: String,
        val justCode: String
    )

    data class UserData(
        val userId: Long,
        val username: String,
        val firstName: String,
        val lastName: String,
        val bio: String,
        val profilePictureUrl: String,
        val profilePicturePreviewUrl: String,
        val registrationDate: Long
    )

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
