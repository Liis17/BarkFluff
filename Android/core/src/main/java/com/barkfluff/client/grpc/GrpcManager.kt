package com.barkfluff.client.grpc

import android.content.Context
import android.util.Base64
import android.util.Log
import barkfluff.beacon.BeaconApiGrpcKt
import barkfluff.beacon.BeaconApiOuterClass
import barkfluff.calls.CallsApiGrpcKt
import barkfluff.fast.auth.FastAuthApiGrpcKt
import barkfluff.fast.auth.FastAuthApiOuterClass
import barkfluff.fast.auth.FastAuthApiOuterClass.ScanFastAuthResponse
import barkfluff.identity.IdentityApiGrpcKt
import barkfluff.identity.IdentityApiOuterClass
import barkfluff.navigator.NavigatorApiGrpcKt
import barkfluff.navigator.NavigatorApiOuterClass
import barkfluff.users.UsersApiGrpcKt
import barkfluff.users.UsersApiOuterClass
import barkfluff.files.FilesApiGrpcKt
import barkfluff.files.FilesApiOuterClass
import barkfluff.messages.MessagesApiGrpcKt
import barkfluff.messages.MessagesApiOuterClass
import barkfluff.updates.UpdatesApiGrpcKt
import barkfluff.onliner.OnlinerApiGrpcKt
import barkfluff.shared.Shared
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import com.barkfluff.client.security.TlsTransportFactory
import io.grpc.*
import io.grpc.ManagedChannel
import io.grpc.okhttp.OkHttpChannelBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.net.HttpURLConnection
import java.net.URL

/**
 * Менеджер для работы с gRPC
 * Аналог WebApiClientManager из WPF клиента
 */
class GrpcManager(context: Context) {

    companion object {
        private const val TAG = "GrpcManager"
        private const val TOKEN_BUFFER_MINUTES = 5
        const val DEFAULT_NAVIGATOR_URL = "https://navigator.barkfluff.com:443"

        // Error codes from x-error-code trailer
        const val ERROR_OTP_CODE_NEEDED = "C1576884-12D8-4722-A7EE-9F9789AD1265"
        const val ERROR_NOT_VALID_OTP_CODE = "803B632C-4457-4B05-9435-9C3DD0F41E00"
        const val ERROR_INVALID_LOGIN_OR_PASSWORD = "21BFB9B5-C377-45D1-9B15-6B7F3432B397"
        const val ERROR_INVALID_OLD_PASSWORD = "A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04"
        const val ERROR_USERNAME_INVALID_FORMAT = "E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47"

        private val ERROR_CODE_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
    }

    // Единый мьютекс на обновление токена — все стримы и операции (RealtimeService,
    // CallEventsService, виджет, 401-retry) рефрешат через него, чтобы параллельные
    // обновления не аннулировали refresh-токен друг друга (сервер ротирует refresh-токен).
    private val tokenRefreshMutex = Mutex()
    private val tlsTransport = TlsTransportFactory(context.applicationContext)

    // Адреса, использованные при создании каналов (для идемпотентности)
    private var navigatorAddress: String? = null
    private var beaconAddress: String? = null
    private var identityAddress: String? = null
    private var usersAddress: String? = null
    private var filesAddress: String? = null
    private var messagesAddress: String? = null
    private var updatesAddress: String? = null
    private var onlinerAddress: String? = null
    private var fastAuthAddress: String? = null
    private var callsAddress: String? = null

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
    var messagesChannel: Channel? = null
        private set
    var updatesChannel: Channel? = null
        private set
    var onlinerChannel: Channel? = null
        private set
    var fastAuthChannel: Channel? = null
        private set
    var callsChannel: Channel? = null
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
    var messagesClient: MessagesApiGrpcKt.MessagesApiCoroutineStub? = null
        private set
    var updatesClient: UpdatesApiGrpcKt.UpdatesApiCoroutineStub? = null
        private set
    var onlinerClient: OnlinerApiGrpcKt.OnlinerApiCoroutineStub? = null
        private set
    var fastAuthClient: FastAuthApiGrpcKt.FastAuthApiCoroutineStub? = null
        private set
    var callsClient: CallsApiGrpcKt.CallsApiCoroutineStub? = null
        private set

    /**
     * Проверяет, инициализированы ли основные клиенты (identity, users, files, messages).
     */
    fun isInitialized(): Boolean {
        return identityClient != null && usersClient != null && filesClient != null && messagesClient != null
    }

    fun normalizeEndpointAddress(address: String): String = tlsTransport.normalizeGrpcAddress(address)

    /**
     * Инициализирует все клиенты по адресам из GlobalParam.
     * Идемпотентно — если клиент уже создан с тем же адресом, пропускает пересоздание.
     */
    fun initAllClients(context: Context, globalParam: GlobalParam) {
        val identity = globalParam.socketIdentity
        val users = globalParam.socketUsers
        val files = globalParam.socketFiles
        val messages = globalParam.socketMessages
        val onliner = globalParam.socketOnliner
        val updates = globalParam.socketUpdates
        val calls = globalParam.socketCalls

        if (identity.isNotBlank()) createIdentityClient(identity, context, includeDeviceInfo = true)
        if (users.isNotBlank()) createUsersClient(users, context, includeDeviceInfo = true)
        if (files.isNotBlank()) createFilesClient(files, context, includeDeviceInfo = true)
        if (messages.isNotBlank()) createMessagesClient(messages, context, includeDeviceInfo = true)
        if (onliner.isNotBlank()) createOnlinerClient(onliner, context, includeDeviceInfo = true)
        if (updates.isNotBlank()) createUpdatesClient(updates, context, includeDeviceInfo = true)
        val fastAuth = globalParam.socketFastAuth
        if (fastAuth.isNotBlank()) createFastAuthClient(fastAuth, context, includeDeviceInfo = true)
        if (calls.isNotBlank()) createCallsClient(calls, context, includeDeviceInfo = true)
    }

    /**
     * Создает только Beacon клиент для работы с Beacon API на сервере
     * Аналог CreateOnlyBeaconAC в WPF
     */
    fun createOnlyBeaconClient(beaconAddress: String): Result<Unit> {
        if (beaconAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Beacon сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(beaconAddress)
        if (this.beaconAddress == normalized && beaconClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)
            this.beaconChannel = channel
            this.beaconClient = BeaconApiGrpcKt.BeaconApiCoroutineStub(channel)
            this.beaconAddress = normalized
            Log.d(TAG, "Beacon клиент создан: $normalized")
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

        val normalized = tlsTransport.normalizeGrpcAddress(navigatorUrl)
        if (this.navigatorAddress == normalized && navigatorClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)
            navigatorChannel = channel
            navigatorClient = NavigatorApiGrpcKt.NavigatorApiCoroutineStub(channel)
            this.navigatorAddress = normalized
            Log.d(TAG, "Navigator клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Navigator клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу: ${e.message}"))
        }
    }

    /**
     * Создает Identity клиент для авторизации
     */
    fun createIdentityClient(identityAddress: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (identityAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Identity сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(identityAddress)
        if (this.identityAddress == normalized && identityClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            // Добавляем interceptors
            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            identityChannel = interceptedChannel
            identityClient = IdentityApiGrpcKt.IdentityApiCoroutineStub(interceptedChannel)
            this.identityAddress = normalized
            Log.d(TAG, "Identity клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Identity клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу авторизации: ${e.message}"))
        }
    }

    /**
     * Создает Users клиент для работы с пользователями
     */
    fun createUsersClient(usersAddress: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (usersAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Users сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(usersAddress)
        if (this.usersAddress == normalized && usersClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            // Добавляем interceptors
            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.usersChannel = interceptedChannel
            usersClient = UsersApiGrpcKt.UsersApiCoroutineStub(interceptedChannel)
            this.usersAddress = normalized
            Log.d(TAG, "Users клиент создан: $normalized")
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
     * Проверяет валидность access токена и обновляет его при необходимости.
     * Вызывается перед критическими операциями (загрузка чатов, сообщений).
     *
     * @param context Контекст приложения
     * @return true если токен валиден или успешно обновлён, false если обновление не удалось
     */
    suspend fun ensureTokenValid(context: Context): Boolean {
        val globalParam = GlobalParam(context)
        val expiration = globalParam.accessTokenExpiration
        val now = System.currentTimeMillis()
        val bufferMs = TOKEN_BUFFER_MINUTES * 60 * 1000L

        // Токен ещё валиден
        if (expiration > 0 && now + bufferMs < expiration) {
            return true
        }

        Log.d(TAG, "ensureTokenValid: Token expiring soon, refreshing")
        return refreshTokenInternal(globalParam, context, globalParam.accessToken)
    }

    /**
     * Принудительно обновляет токен, игнорируя время истечения.
     * Используется при получении UNAUTHENTICATED ошибки.
     *
     * @param context Контекст приложения
     * @return true если токен успешно обновлён
     */
    suspend fun forceRefreshToken(context: Context): Boolean {
        val globalParam = GlobalParam(context)
        Log.d(TAG, "forceRefreshToken: Force refreshing token")
        return refreshTokenInternal(globalParam, context, globalParam.accessToken)
    }

    private suspend fun refreshTokenInternal(
        globalParam: GlobalParam,
        context: Context,
        tokenBeforeRefresh: String?
    ): Boolean = tokenRefreshMutex.withLock {
        // Double-check после захвата лока: если access-токен уже сменился пока ждали лок —
        // другой вызов успел обновить. Не рефрешим повторно (иначе аннулируем свежий refresh-токен).
        if (!tokenBeforeRefresh.isNullOrBlank() && globalParam.accessToken != tokenBeforeRefresh) {
            return@withLock true
        }

        val refreshToken = globalParam.refreshToken
        if (refreshToken.isNullOrBlank()) {
            Log.e(TAG, "refreshTokenInternal: No refresh token available")
            return@withLock false
        }

        val identityAddress = globalParam.socketIdentity
        if (identityAddress.isBlank()) {
            Log.e(TAG, "refreshTokenInternal: No identity address available")
            return@withLock false
        }

        try {
            // Убеждаемся что identity клиент инициализирован
            createIdentityClient(identityAddress, context, includeDeviceInfo = true)
            val result = refreshAccessToken(refreshToken, globalParam.refreshTokenExpiration)

            if (result.isSuccess) {
                val tokenResult = result.getOrNull()!!
                globalParam.accessToken = tokenResult.accessToken
                globalParam.accessTokenExpiration = tokenResult.accessTokenExpiration
                globalParam.refreshToken = tokenResult.refreshToken
                globalParam.refreshTokenExpiration = tokenResult.refreshTokenExpiration
                Log.i(TAG, "refreshTokenInternal: Token refreshed successfully")
                true
            } else {
                Log.e(TAG, "refreshTokenInternal: Token refresh failed: ${result.exceptionOrNull()?.message}")
                false
            }
        } catch (e: Exception) {
            Log.e(TAG, "refreshTokenInternal: Error refreshing token", e)
            false
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

            Log.d(TAG, "getCurrentUserData: userId=${user.id}")

            val profilePictureFileId = extractGuidFromUrl(user.profilePicture)
            val profilePicturePreviewFileId = extractGuidFromUrl(user.profilePicturePreview)
            
            Log.d(TAG, "getCurrentUserData: profilePictureFileId='$profilePictureFileId', profilePicturePreviewFileId='$profilePicturePreviewFileId'")

            Result.success(
                UserData(
                    userId = user.id,
                    username = user.username,
                    firstName = user.firstName,
                    lastName = user.lastName,
                    bio = user.bio,
                    profilePictureUrl = user.profilePicture,
                    profilePicturePreviewUrl = user.profilePicturePreview,
                    profilePictureFileId = profilePictureFileId,
                    profilePicturePreviewFileId = profilePicturePreviewFileId,
                    profilePosterFileId = user.profilePosterFileId,
                    registrationDate = user.registrationDate.seconds * 1000
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения данных пользователя", e)
            Result.failure(Exception("Ошибка получения данных пользователя: ${e.message}"))
        }
    }

    /**
     * Отправляет Firebase токен на сервер
     */
    suspend fun setFirebaseToken(token: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.SetFirebaseTokenRequest.newBuilder()
                .setFirebaseToken(token)
                .build()

            usersClient!!.setFirebaseToken(request)
            Log.d(TAG, "Firebase токен успешно отправлен на сервер")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отправки Firebase токена", e)
            Result.failure(Exception("Ошибка отправки Firebase токена: ${e.message}"))
        }
    }

    /**
     * Переименовывает устройство
     */
    suspend fun renameDevice(deviceId: String, customName: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.RenameDeviceRequest.newBuilder()
                .setDeviceId(deviceId)
                .setCustomName(customName)
                .build()

            usersClient!!.renameDevice(request)
            Log.d(TAG, "Устройство успешно переименовано")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка переименования устройства", e)
            Result.failure(Exception("Ошибка переименования устройства: ${e.message}"))
        }
    }

    /**
     * Получает статус уведомлений для текущего устройства
     */
    suspend fun getNotificationsEnabled(): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.GetCurrentDeviceRequest.newBuilder().build()
            val response = usersClient!!.getCurrentDevice(request)

            if (response.hasDevice()) {
                Result.success(response.device.notificationsEnabled)
            } else {
                Result.success(true)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения статуса уведомлений", e)
            Result.failure(Exception("Ошибка получения статуса уведомлений: ${e.message}"))
        }
    }

    /**
     * Получает настройки приватности текущего пользователя
     */
    suspend fun getPrivacySettings(): Result<UsersApiOuterClass.PrivacySettings> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            val response = usersClient!!.getPrivacySettings(
                UsersApiOuterClass.GetPrivacySettingsRequest.newBuilder().build()
            )
            Result.success(response.settings)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения настроек приватности", e)
            Result.failure(Exception("Ошибка получения настроек приватности: ${e.message}"))
        }
    }

    /**
     * Обновляет настройки приватности текущего пользователя
     */
    suspend fun updatePrivacySettings(settings: UsersApiOuterClass.PrivacySettings): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            usersClient!!.updatePrivacySettings(
                UsersApiOuterClass.UpdatePrivacySettingsRequest.newBuilder()
                    .setSettings(settings)
                    .build()
            )
            Log.d(TAG, "Настройки приватности успешно обновлены")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления настроек приватности", e)
            Result.failure(Exception("Ошибка обновления настроек приватности: ${e.message}"))
        }
    }

    /**
     * Включает или выключает push-уведомления для текущего устройства
     */
    suspend fun setNotificationsEnabled(enabled: Boolean): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.SetNotificationsEnabledRequest.newBuilder()
                .setEnabled(enabled)
                .build()

            usersClient!!.setNotificationsEnabled(request)
            Log.d(TAG, "Статус уведомлений успешно обновлён: $enabled")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления статуса уведомлений", e)
            Result.failure(Exception("Ошибка обновления статуса уведомлений: ${e.message}"))
        }
    }

    /**
     * Заглушить/включить уведомления для конкретного чата (per-chat mute).
     * mutedUntilEpochSeconds == null при muted=true => замьютить навсегда.
     */
    suspend fun setChatMuted(
        chatId: String,
        muted: Boolean,
        mutedUntilEpochSeconds: Long? = null
    ): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val builder = UsersApiOuterClass.SetChatMutedRequest.newBuilder()
                .setChatId(chatId)
                .setMuted(muted)

            if (muted && mutedUntilEpochSeconds != null) {
                builder.mutedUntil = com.google.protobuf.Timestamp.newBuilder()
                    .setSeconds(mutedUntilEpochSeconds)
                    .build()
            }

            usersClient!!.setChatMuted(builder.build())
            Log.d(TAG, "Mute чата $chatId обновлён: muted=$muted")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления mute чата", e)
            Result.failure(Exception("Ошибка обновления mute чата: ${e.message}"))
        }
    }

    /**
     * Возвращает набор идентификаторов замьюченных чатов текущего пользователя.
     */
    suspend fun getMutedChats(): Result<Set<String>> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val response = usersClient!!.getMutedChats(
                UsersApiOuterClass.GetMutedChatsRequest.getDefaultInstance()
            )
            Result.success(response.chatsList.map { it.chatId }.toSet())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения списка замьюченных чатов", e)
            Result.failure(Exception("Ошибка получения списка замьюченных чатов: ${e.message}"))
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
            Log.d(
                TAG,
                "Beacon calls: has=${response.hasCalls()}, " +
                    "host=${if (response.hasCalls()) response.calls.endpoint.host else ""}, " +
                    "port=${if (response.hasCalls()) response.calls.endpoint.port else 0}, " +
                    "tls=${response.hasCalls() && response.calls.tlsEnabled}, " +
                    "livekit=${response.livekitUrl.isNotBlank()}"
            )

            val serverInfo = ServerInfo(
                name = response.name,
                description = response.description,
                color = ClientColors(
                    response.color.liteHex,
                    response.color.mainHex,
                    response.color.hardHex
                ),
                identityEndpoint = buildEndpointUrl(response.identity.endpoint.host, response.identity.endpoint.port, response.identity.tlsEnabled),
                usersEndpoint = buildEndpointUrl(response.users.endpoint.host, response.users.endpoint.port, response.users.tlsEnabled),
                filesEndpoint = buildEndpointUrl(response.files.endpoint.host, response.files.endpoint.port, response.files.tlsEnabled),
                messagesEndpoint = buildEndpointUrl(response.messages.endpoint.host, response.messages.endpoint.port, response.messages.tlsEnabled),
                updatesEndpoint = buildEndpointUrl(response.updates.endpoint.host, response.updates.endpoint.port, response.updates.tlsEnabled),
                onlinerEndpoint = buildEndpointUrl(response.onliner.endpoint.host, response.onliner.endpoint.port, response.onliner.tlsEnabled),
                fastAuthEndpoint = buildEndpointUrl(response.fastAuth.endpoint.host, response.fastAuth.endpoint.port, response.fastAuth.tlsEnabled),
                callsEndpoint = if (response.hasCalls()) buildEndpointUrl(response.calls.endpoint.host, response.calls.endpoint.port, response.calls.tlsEnabled) else "",
                livekitUrl = normalizeLivekitUrl(response.livekitUrl)
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

    /**
     * Собирает URL вида {scheme}://{host}:{port}, где scheme выбирается
     * по флагу tlsEnabled. Если в host уже есть схема — она срезается,
     * чтобы избежать двойного префикса.
     */
    private fun buildEndpointUrl(host: String, port: Int, tlsEnabled: Boolean): String {
        if (host.isBlank() || port <= 0) {
            return ""
        }

        var cleanHost = host
        if (cleanHost.startsWith("https://", ignoreCase = true)) {
            cleanHost = cleanHost.substring(8)
        } else if (cleanHost.startsWith("http://", ignoreCase = true)) {
            cleanHost = cleanHost.substring(7)
        }
        cleanHost = cleanHost.trimEnd('/')
        val scheme = if (tlsEnabled) "https" else "http"
        return tlsTransport.normalizeGrpcAddress("$scheme://$cleanHost:$port")
    }

    private fun normalizeLivekitUrl(url: String): String {
        if (url.isBlank()) {
            return ""
        }
        return tlsTransport.normalizeUrl(url)
    }

    private fun createChannel(address: String): ManagedChannel = tlsTransport.createGrpcChannel(address)

    /** Applies the same host-scoped TLS policy to presigned HTTP(S) connections. */
    fun configureHttpConnection(connection: HttpURLConnection) {
        tlsTransport.configure(connection)
    }

    private fun toBase64(value: String): String {
        return Base64.encodeToString(value.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)
    }

    private fun key(name: String): Metadata.Key<String> {
        return Metadata.Key.of(name, Metadata.ASCII_STRING_MARSHALLER)
    }

    /**
     * Пересоздаёт все каналы принудительно.
     * Вызывается при возврате из фона, когда старые каналы могли сломаться (DNS resolution failure).
     *
     * Использует swap-then-close: сначала создаёт новые каналы, потом закрывает старые.
     * Это исключает окно, в котором клиенты == null и IO-корутины падают с NPE.
     */
    fun recreateAllClients(context: Context, globalParam: GlobalParam) {
        Log.d(TAG, "Force-recreating all gRPC channels")

        // 1. Сохраняем старые каналы для последующего закрытия
        val oldChannels = listOfNotNull(
            identityChannel, usersChannel, filesChannel,
            messagesChannel, updatesChannel, onlinerChannel, callsChannel
        )

        // 2. Сбрасываем адреса, чтобы initAllClients не пропустил пересоздание (idempotency check)
        identityAddress = null
        usersAddress = null
        filesAddress = null
        messagesAddress = null
        updatesAddress = null
        onlinerAddress = null
        fastAuthAddress = null
        callsAddress = null

        // 3. Создаём новые каналы и клиенты — ссылки обновляются атомарно для каждого сервиса
        initAllClients(context, globalParam)

        // 4. Закрываем старые каналы (in-flight RPC на старых каналах завершатся с ошибкой,
        //    но новые запросы уже идут через новые каналы)
        oldChannels.forEach { shutdownChannel(it) }
    }

    /**
     * Закрывает все gRPC каналы
     * Аналог Dispose в WPF
     */
    fun shutdown() {
        shutdownChannel(navigatorChannel)
        shutdownChannel(beaconChannel)
        shutdownChannel(identityChannel)
        shutdownChannel(usersChannel)
        shutdownChannel(filesChannel)
        shutdownChannel(messagesChannel)
        shutdownChannel(updatesChannel)
        shutdownChannel(onlinerChannel)
        shutdownChannel(fastAuthChannel)
        shutdownChannel(callsChannel)

        navigatorChannel = null
        beaconChannel = null
        identityChannel = null
        usersChannel = null
        filesChannel = null
        messagesChannel = null
        updatesChannel = null
        onlinerChannel = null
        fastAuthChannel = null
        callsChannel = null

        navigatorClient = null
        beaconClient = null
        identityClient = null
        usersClient = null
        filesClient = null
        messagesClient = null
        updatesClient = null
        onlinerClient = null
        fastAuthClient = null
        callsClient = null

        navigatorAddress = null
        beaconAddress = null
        identityAddress = null
        usersAddress = null
        filesAddress = null
        messagesAddress = null
        updatesAddress = null
        onlinerAddress = null
        fastAuthAddress = null
        callsAddress = null
    }

    /**
     * Корректно закрывает gRPC канал
     */
    private fun shutdownChannel(channel: Channel?) {
        if (channel is ManagedChannel) {
            channel.shutdown()
            try {
                // Ждем завершения не более 3 секунд
                if (!channel.awaitTermination(3, java.util.concurrent.TimeUnit.SECONDS)) {
                    Log.w(TAG, "Канал не был закрыт в течение 3 секунд, вызываем shutdownNow()")
                    channel.shutdownNow()
                }
            } catch (e: InterruptedException) {
                Log.w(TAG, "Прерывание при ожидании закрытия канала", e)
                channel.shutdownNow()
                Thread.currentThread().interrupt()
            }
        }
    }

    /**
     * Создает Files клиент для работы с файлами
     */
    fun createFilesClient(filesAddress: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (filesAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Files сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(filesAddress)
        if (this.filesAddress == normalized && filesClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.filesChannel = interceptedChannel
            filesClient = FilesApiGrpcKt.FilesApiCoroutineStub(interceptedChannel)
            this.filesAddress = normalized
            Log.d(TAG, "Files клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Files клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу файлов: ${e.message}"))
        }
    }

    /**
     * Создает Messages клиент для работы с чатами и сообщениями
     */
    fun createMessagesClient(messagesAddress: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (messagesAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Messages сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(messagesAddress)
        if (this.messagesAddress == normalized && messagesClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.messagesChannel = interceptedChannel
            messagesClient = MessagesApiGrpcKt.MessagesApiCoroutineStub(interceptedChannel)
            this.messagesAddress = normalized
            Log.d(TAG, "Messages клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Messages клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу сообщений: ${e.message}"))
        }
    }

    /**
     * Создает Updates клиент для подписки на обновления сообщений
     */
    fun createUpdatesClient(updatesAddress: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (updatesAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Updates сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(updatesAddress)
        if (this.updatesAddress == normalized && updatesClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.updatesChannel = interceptedChannel
            updatesClient = UpdatesApiGrpcKt.UpdatesApiCoroutineStub(interceptedChannel)
            this.updatesAddress = normalized
            Log.d(TAG, "Updates клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Updates клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу обновлений: ${e.message}"))
        }
    }

    /**
     * Создает Onliner клиент для отслеживания онлайн-статусов
     */
    fun createOnlinerClient(onlinerAddress: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (onlinerAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Onliner сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(onlinerAddress)
        if (this.onlinerAddress == normalized && onlinerClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.onlinerChannel = interceptedChannel
            onlinerClient = OnlinerApiGrpcKt.OnlinerApiCoroutineStub(interceptedChannel)
            this.onlinerAddress = normalized
            Log.d(TAG, "Onliner клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Onliner клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу онлайн-статусов: ${e.message}"))
        }
    }

    fun createFastAuthClient(address: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (address.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес FastAuth сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(address)
        if (this.fastAuthAddress == normalized && fastAuthClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.fastAuthChannel = interceptedChannel
            fastAuthClient = FastAuthApiGrpcKt.FastAuthApiCoroutineStub(interceptedChannel)
            this.fastAuthAddress = normalized
            Log.d(TAG, "FastAuth клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания FastAuth клиента", e)
            Result.failure(Exception("Ошибка подключения к FastAuth серверу: ${e.message}"))
        }
    }

    fun createCallsClient(address: String, context: Context? = null, includeDeviceInfo: Boolean = false): Result<Unit> {
        if (address.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Calls сервера не указан"))
        }

        val normalized = tlsTransport.normalizeGrpcAddress(address)
        if (this.callsAddress == normalized && callsClient != null) return Result.success(Unit)

        return try {
            val channel = createChannel(normalized)

            val interceptors = mutableListOf<ClientInterceptor>()
            if (context != null) {
                interceptors.add(AuthInterceptor(context))
            }
            if (includeDeviceInfo && context != null) {
                interceptors.add(DeviceInfoInterceptor(context))
            }

            val interceptedChannel = if (interceptors.isNotEmpty()) {
                ClientInterceptors.intercept(channel, *interceptors.toTypedArray())
            } else {
                channel
            }

            this.callsChannel = interceptedChannel
            callsClient = CallsApiGrpcKt.CallsApiCoroutineStub(interceptedChannel)
            this.callsAddress = normalized
            Log.d(TAG, "Calls клиент создан: $normalized")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Calls клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу звонков: ${e.message}"))
        }
    }
    suspend fun scanFastAuth(fastAuthId: String): Result<ScanFastAuthResponse> = withContext(Dispatchers.IO) {
        try {
            if (fastAuthClient == null) {
                return@withContext Result.failure(IllegalStateException("FastAuth клиент не создан"))
            }
            val request = FastAuthApiOuterClass.ScanFastAuthRequest.newBuilder()
                .setFastAuthId(fastAuthId)
                .build()
            val response = fastAuthClient!!.scanFastAuth(request)
            Result.success(response)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка scanFastAuth", e)
            Result.failure(e)
        }
    }

    suspend fun acceptFastAuth(fastAuthId: String, confirmationCode: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (fastAuthClient == null) {
                return@withContext Result.failure(IllegalStateException("FastAuth клиент не создан"))
            }
            val request = FastAuthApiOuterClass.AcceptFastAuthRequest.newBuilder()
                .setFastAuthId(fastAuthId)
                .setConfirmationCode(confirmationCode)
                .build()
            fastAuthClient!!.acceptFastAuth(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка acceptFastAuth", e)
            Result.failure(e)
        }
    }

    suspend fun rejectFastAuth(fastAuthId: String, confirmationCode: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (fastAuthClient == null) {
                return@withContext Result.failure(IllegalStateException("FastAuth клиент не создан"))
            }
            val request = FastAuthApiOuterClass.RejectFastAuthRequest.newBuilder()
                .setFastAuthId(fastAuthId)
                .setConfirmationCode(confirmationCode)
                .build()
            fastAuthClient!!.rejectFastAuth(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка rejectFastAuth", e)
            Result.failure(e)
        }
    }

    /**
     * Получает список чатов
     * Аналог GetChats в WebApiMessageManager
     */
    suspend fun getChats(offset: Int = 0, size: Int = 50): Result<List<ChatData>> =
        getChatsPage(offset, size).map { it.chats }

    /** Страница списка с totalCount для последовательной подгрузки в ChatsFragment. */
    suspend fun getChatsPage(offset: Int = 0, size: Int = 50): Result<ChatPage> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val pagination = Shared.PageRequest.newBuilder()
                .setOffset(offset)
                .setSize(size)
                .build()

            val request = MessagesApiOuterClass.ListChatsRequest.newBuilder()
                .setPagination(pagination)
                .build()

            val response = messagesClient!!.listChats(request)

            val chats = response.chatsList.map(::toChatData)
            Log.d(TAG, "Получено ${chats.size} чатов из ${response.totalCount}")
            Result.success(ChatPage(chats, response.totalCount))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения чатов", e)
            Result.failure(Exception("Ошибка получения чатов: ${e.message}"))
        }
    }

    private fun toChatData(chat: MessagesApiOuterClass.Chat): ChatData {
        val lastMsg = if (chat.hasLastMessage()) {
            val msg = chat.lastMessage
            LastMessageData(
                id = msg.id,
                senderId = msg.senderId,
                text = msg.content?.text ?: "",
                sentAt = msg.sentAt.seconds * 1000,
                readBy = msg.readByList
            )
        } else null

        return ChatData(
            id = chat.id,
            title = chat.title,
            picture = chat.picture,
            pictureFileId = extractGuidFromUrl(chat.picture),
            picturePreviewFileId = extractGuidFromUrl(chat.picture),
            isGroupChat = chat.isGroupChat,
            lastMessage = lastMsg,
            memberIds = chat.membersList.map { it.userId },
            countUnread = chat.countUnread,
            firstUnreadMessageId = chat.firstUnreadMessageId,
            chatType = chat.chatType,
            lastActivityAt = if (chat.hasLastActivityAt()) chat.lastActivityAt.seconds * 1000 else lastMsg?.sentAt ?: 0L,
            privateInviteState = chat.privateInviteState,
            privateInviterUserId = chat.privateInviterUserId,
            hasDraft = chat.hasDraft
        )
    }

    /**
     * Получает список участников группового чата
     */
    suspend fun listChatMembers(chatId: String, offset: Int = 0, size: Int = 50): Result<List<ChatMemberInfo>> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val pagination = Shared.PageRequest.newBuilder()
                .setOffset(offset)
                .setSize(size)
                .build()

            val request = MessagesApiOuterClass.ListChatMembersRequest.newBuilder()
                .setChatId(chatId)
                .setPagination(pagination)
                .build()

            val response = messagesClient!!.listChatMembers(request)

            val members = response.chatMembersList.map { m ->
                ChatMemberInfo(
                    userId = m.generalInfo.userId,
                    firstName = m.firstName,
                    lastName = m.lastName
                )
            }

            Result.success(members)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения участников чата $chatId", e)
            Result.failure(Exception("Ошибка получения участников чата: ${e.message}"))
        }
    }

    /**
     * Исключает пользователя из группового чата
     */
    suspend fun kickUser(chatId: String, userId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val request = MessagesApiOuterClass.KickUserRequest.newBuilder()
                .setChatId(chatId)
                .setUserId(userId)
                .build()

            messagesClient!!.kickUser(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка исключения пользователя $userId из чата $chatId", e)
            Result.failure(Exception("Ошибка исключения пользователя: ${e.message}"))
        }
    }

    /**
     * Добавляет пользователя в групповой чат
     */
    suspend fun addUser(chatId: String, userId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val request = MessagesApiOuterClass.AddUserRequest.newBuilder()
                .setChatId(chatId)
                .setUserId(userId)
                .build()

            messagesClient!!.addUser(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка добавления пользователя $userId в чат $chatId", e)
            Result.failure(Exception("Ошибка добавления пользователя: ${e.message}"))
        }
    }

    /**
     * Изменяет групповой чат (название и/или аватар).
     * Пустые параметры (null) — не менять соответствующее поле.
     */
    suspend fun updateGroupChat(chatId: String, title: String? = null, pictureFileId: String? = null): Result<ChatData> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val request = MessagesApiOuterClass.UpdateGroupChatRequest.newBuilder()
                .setChatId(chatId)
                .setTitle(title ?: "")
                .setPictureFileId(pictureFileId ?: "")
                .build()

            val response = messagesClient!!.updateGroupChat(request)
            Result.success(toChatData(response.chat))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка изменения группового чата $chatId", e)
            Result.failure(Exception("Ошибка изменения чата: ${e.message}"))
        }
    }

    suspend fun createGroupChat(userIds: List<Long>, title: String, pictureFileId: String? = null): Result<ChatData> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.CreateGroupChatRequest.newBuilder()
                .addAllUserIds(userIds)
                .setTitle(title)
                .setPictureFileId(pictureFileId.orEmpty())
                .build()
            Result.success(toChatData(client.createGroupChat(request).createdChat))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания группового чата", e)
            Result.failure(e)
        }
    }

    /**
     * Получает данные пользователя по ID
     */
    suspend fun getUserData(userId: Long): Result<UserData> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.GetUserRequest.newBuilder()
                .setUserId(userId)
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
                    profilePictureFileId = extractGuidFromUrl(user.profilePicture),
                    profilePicturePreviewFileId = extractGuidFromUrl(user.profilePicturePreview),
                    profilePosterFileId = user.profilePosterFileId,
                    registrationDate = user.registrationDate.seconds * 1000
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения данных пользователя $userId", e)
            Result.failure(Exception("Ошибка получения данных пользователя: ${e.message}"))
        }
    }

    /**
     * Поиск пользователей по запросу
     * Аналог SearchUser в WebApiSearchManager
     */
    suspend fun searchUsers(query: String, offset: Int = 0, size: Int = 50): Result<List<UserData>> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val pagination = Shared.PageRequest.newBuilder()
                .setOffset(offset)
                .setSize(size)
                .build()

            val request = UsersApiOuterClass.SearchUsersRequest.newBuilder()
                .setQuery(query)
                .setPagination(pagination)
                .build()

            val response = usersClient!!.searchUsers(request)

            val users = response.usersList.map { user ->
                UserData(
                    userId = user.id,
                    username = user.username,
                    firstName = user.firstName,
                    lastName = user.lastName,
                    bio = user.bio,
                    profilePictureUrl = user.profilePicture,
                    profilePicturePreviewUrl = user.profilePicturePreview,
                    profilePictureFileId = extractGuidFromUrl(user.profilePicture),
                    profilePicturePreviewFileId = extractGuidFromUrl(user.profilePicturePreview),
                    registrationDate = user.registrationDate.seconds * 1000
                )
            }

            Log.d(TAG, "Поиск: найдено ${users.size} пользователей")
            Result.success(users)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка поиска пользователей", e)
            Result.failure(Exception("Ошибка поиска пользователей: ${e.message}"))
        }
    }

    /**
     * Получает ID личного чата с пользователем
     */
    suspend fun getPersonChatId(userId: Long): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val request = MessagesApiOuterClass.GetPersonChatIdRequest.newBuilder()
                .setUserId(userId)
                .build()

            val response = messagesClient!!.getPersonChatId(request)
            Result.success(response.chatId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения ID чата с пользователем $userId", e)
            Result.failure(Exception("Ошибка получения ID чата: ${e.message}"))
        }
    }

    /**
     * Получает временные URL для скачивания файлов
     * Аналог GetFile/GetFiles в WebApiFileManager
     */
    suspend fun getFileDownloadUrls(fileIds: List<String>): Result<Map<String, String>> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files клиент не создан"))
            }

            val request = FilesApiOuterClass.GetTempDownloadUrlRequest.newBuilder()
                .addAllFileIds(fileIds)
                .build()

            val response = filesClient!!.getTempDownloadUrl(request)
            val urlMap = response.fileUrlsList.associate { it.fileId to it.url }

            Result.success(urlMap)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения URL файлов", e)
            Result.failure(Exception("Ошибка получения URL файлов: ${e.message}"))
        }
    }

    /**
     * Получает временный URL для скачивания файла по fileId.
     * Аналог GetFile в WebApiFileManager.
     */
    suspend fun getFileDownloadUrl(fileId: String): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                Log.e(TAG, "getFileDownloadUrl: Files клиент не создан")
                return@withContext Result.failure(IllegalStateException("Files клиент не создан"))
            }

            Log.d(TAG, "getFileDownloadUrl: Запрос URL для fileId=$fileId")

            val request = FilesApiOuterClass.GetTempDownloadUrlRequest.newBuilder()
                .addFileIds(fileId)
                .build()

            val response = filesClient!!.getTempDownloadUrl(request)

            Log.d(TAG, "getFileDownloadUrl: Получен ответ, fileUrlsList.size=${response.fileUrlsList.size}")

            val fileUrl = response.fileUrlsList.firstOrNull()
            if (fileUrl == null) {
                Log.e(TAG, "getFileDownloadUrl: Пустой список fileUrlsList для fileId=$fileId")
                return@withContext Result.failure(Exception("URL не получен: пустой ответ"))
            }

            val url = fileUrl.url

            if (url.isNullOrBlank()) {
                Log.e(TAG, "getFileDownloadUrl: Пустой URL для fileId=$fileId")
                return@withContext Result.failure(Exception("URL не получен"))
            }

            Log.d(TAG, "getFileDownloadUrl: Успешно получен URL для fileId=$fileId")
            Result.success(url)
        } catch (e: Exception) {
            Log.e(TAG, "getFileDownloadUrl: Исключение", e)
            Result.failure(Exception("Ошибка получения URL: ${e.message}"))
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
        } catch (e: StatusRuntimeException) {
            val errorCode = e.trailers?.get(ERROR_CODE_KEY)
            if (errorCode == ERROR_USERNAME_INVALID_FORMAT) {
                Log.w(TAG, "Недопустимый формат имени пользователя")
                Result.failure(Exception("Имя пользователя имеет недопустимый формат: латинские буквы, цифры и подчёркивание, 3–32 символа"))
            } else {
                Log.e(TAG, "Ошибка создания аккаунта", e)
                Result.failure(Exception("Ошибка создания аккаунта: ${e.message}"))
            }
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
     * Загружает аватар пользователя через Files API
     * Сначала получает URL для загрузки, затем загружает файл
     */
    suspend fun uploadUserAvatar(jpegImageBytes: ByteArray): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files клиент не создан"))
            }

            // Получаем URL для загрузки
            val uploadUrlResult = getUploadUrl(FilesApiOuterClass.UploadFileType.USER_AVATAR)
            if (uploadUrlResult.isFailure) {
                return@withContext Result.failure(uploadUrlResult.exceptionOrNull()!!)
            }

            val uploadData = uploadUrlResult.getOrNull()!!
            val fileId = uploadData.fileId

            // Выполняем HTTP POST multipart/form-data для загрузки файла
            Log.d(TAG, "Avatar upload URL получен, fileId: $fileId")

            val boundary = "----BarkFluff${System.currentTimeMillis()}"
            val url = URL(uploadData.url)
            val connection = url.openConnection() as HttpURLConnection

            configureHttpConnection(connection)

            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "multipart/form-data; boundary=$boundary")
            connection.doOutput = true

            connection.outputStream.use { out ->
                val writer = out.bufferedWriter()
                // Часть с файлом
                writer.write("--$boundary\r\n")
                writer.write("Content-Disposition: form-data; name=\"file\"; filename=\"avatar.jpg\"\r\n")
                writer.write("Content-Type: image/jpeg\r\n")
                writer.write("\r\n")
                writer.flush()
                out.write(jpegImageBytes)
                out.flush()
                writer.write("\r\n")
                writer.write("--$boundary--\r\n")
                writer.flush()
            }

            val responseCode = connection.responseCode
            connection.disconnect()

            if (responseCode !in 200..299) {
                return@withContext Result.failure(Exception("Upload failed: HTTP $responseCode"))
            }

            Log.d(TAG, "Avatar uploaded successfully, fileId: $fileId")
            Result.success(fileId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка загрузки аватара", e)
            Result.failure(Exception("Ошибка загрузки аватара: ${e.message}"))
        }
    }

    /**
     * Проверяет, существует ли на сервере файл с таким SHA-256 хешем (lowercase hex, 64 символа).
     * Возвращает существующий fileId или пустую строку, если файла нет — для дедупликации до загрузки.
     */
    suspend fun checkFileHash(fileHash: String): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files клиент не создан"))
            }

            val request = FilesApiOuterClass.CheckFileHashRequest.newBuilder()
                .setFileHash(fileHash)
                .build()

            val response = filesClient!!.checkFileHash(request)
            Result.success(response.fileId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка проверки хеша файла", e)
            Result.failure(e)
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

    /**
     * Изменяет имя и фамилию пользователя
     */
    suspend fun changeName(firstName: String, lastName: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.ChangeNameRequest.newBuilder()
                .setFirstName(firstName)
                .setLastName(lastName)
                .build()

            usersClient!!.changeName(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка изменения имени", e)
            Result.failure(Exception("Ошибка изменения имени: ${e.message}"))
        }
    }

    /**
     * Изменяет имя пользователя (username)
     */
    suspend fun changeUsername(username: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }

            val request = UsersApiOuterClass.ChangeUsernameRequest.newBuilder()
                .setUsername(username)
                .build()

            usersClient!!.changeUsername(request)
            Result.success(Unit)
        } catch (e: StatusRuntimeException) {
            val errorCode = e.trailers?.get(ERROR_CODE_KEY)
            if (errorCode == ERROR_USERNAME_INVALID_FORMAT) {
                Log.w(TAG, "Недопустимый формат имени пользователя")
                Result.failure(Exception("Имя пользователя имеет недопустимый формат: латинские буквы, цифры и подчёркивание, 3–32 символа"))
            } else {
                Log.e(TAG, "Ошибка изменения username", e)
                Result.failure(Exception("Ошибка изменения username: ${e.message}"))
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка изменения username", e)
            Result.failure(Exception("Ошибка изменения username: ${e.message}"))
        }
    }

    /**
     * Получает список активных сессий
     */
    suspend fun getActiveSessions(context: Context): Result<List<SessionData>> = withContext(Dispatchers.IO) {
        suspend fun doCall(): List<SessionData> {
            if (identityClient == null) throw IllegalStateException("Identity клиент не создан")
            val request = IdentityApiOuterClass.GetActiveSessionsRequest.newBuilder().build()
            val response = identityClient!!.getActiveSessions(request)
            return response.sessionsList.map { session ->
                SessionData(
                    id = session.id,
                    createdAt = session.createdAt.seconds * 1000,
                    expirationAt = session.expirationAt.seconds * 1000,
                    deviceId = session.deviceId,
                    originalName = session.originalName,
                    customName = session.customName,
                    appName = session.appName,
                    os = session.operationSystem,
                    location = session.location
                )
            }
        }

        try {
            Result.success(doCall())
        } catch (e: StatusException) {
            if (e.status.code == Status.Code.UNAUTHENTICATED && forceRefreshToken(context)) {
                try {
                    Result.success(doCall())
                } catch (e2: Exception) {
                    Log.e(TAG, "Ошибка получения сессий после обновления токена", e2)
                    Result.failure(Exception("Ошибка получения сессий: ${e2.message}"))
                }
            } else {
                Log.e(TAG, "Ошибка получения сессий", e)
                Result.failure(Exception("Ошибка получения сессий: ${e.message}"))
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения сессий", e)
            Result.failure(Exception("Ошибка получения сессий: ${e.message}"))
        }
    }

    /**
     * Удаляет активную сессию
     */
    suspend fun removeActiveSession(deviceId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.RemoveActiveSessionRequest.newBuilder()
                .setDeviceId(deviceId)
                .build()

            identityClient!!.removeActiveSession(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка удаления сессии", e)
            Result.failure(Exception("Ошибка удаления сессии: ${e.message}"))
        }
    }

    /**
     * Разлогинивает текущее устройство на сервере (удаляет refresh-токен).
     */
    suspend fun logout(): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.LogoutRequest.newBuilder().build()
            identityClient!!.logout(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка logout", e)
            Result.failure(Exception("Ошибка logout: ${e.message}"))
        }
    }

    /**
     * Получает статус 2FA
     */
    suspend fun listOtpVerification(): Result<OtpStatus> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.ListOtpVerificationRequest.newBuilder().build()
            val response = identityClient!!.listOtpVerification(request)

            Result.success(
                OtpStatus(
                    authenticatorEnabled = response.authenticatorEnabled,
                    emailEnabled = response.emailEnabled
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения статуса 2FA", e)
            Result.failure(Exception("Ошибка получения статуса 2FA: ${e.message}"))
        }
    }

    /**
     * Включает 2FA по email
     */
    suspend fun enableOtpEmail(): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.EnableOtpVerificationRequest.newBuilder()
                .setOtpType(IdentityApiOuterClass.OtpTypeId.Email)
                .build()

            identityClient!!.enableOtpVerification(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка включения 2FA по email", e)
            Result.failure(Exception("Ошибка включения 2FA по email: ${e.message}"))
        }
    }

    /**
     * Отключает 2FA
     */
    suspend fun disableOtpVerification(type: IdentityApiOuterClass.OtpTypeId, code: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.DisableOtpVerificationRequest.newBuilder()
                .setOtpType(type)
                .setOtpCode(code)
                .build()

            identityClient!!.disableOtpVerification(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отключения 2FA", e)
            Result.failure(Exception("Ошибка отключения 2FA: ${e.message}"))
        }
    }

    /**
     * Изменяет пароль с проверкой старого
     */
    suspend fun changePassword(oldPassword: String, newPassword: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.SetPasswordRequest.newBuilder()
                .setPassword(newPassword)
                .setOldPassword(oldPassword)
                .build()

            identityClient!!.setPassword(request)
            Result.success(Unit)
        } catch (e: StatusRuntimeException) {
            val errorCode = e.trailers?.get(ERROR_CODE_KEY)
            if (errorCode == ERROR_INVALID_OLD_PASSWORD) {
                Log.w(TAG, "Неверный старый пароль")
                Result.failure(InvalidOldPasswordException())
            } else {
                Log.e(TAG, "Ошибка смены пароля", e)
                Result.failure(Exception("Ошибка смены пароля: ${e.message}"))
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка смены пароля", e)
            Result.failure(Exception("Ошибка смены пароля: ${e.message}"))
        }
    }

    /**
     * Получает информацию о хранилище пользователя
     */
    suspend fun getUserStorageInfo(): Result<StorageInfo> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files клиент не создан"))
            }

            val request = FilesApiOuterClass.GetUserStorageInfoRequest.newBuilder().build()
            val response = filesClient!!.getUserStorageInfo(request)

            val byType = response.storageByTypesList.associate { it.fileType.name to it.usedStorage }

            Result.success(
                StorageInfo(
                    totalUsed = response.totalUsedStorage,
                    limit = response.storageLimit,
                    byType = byType
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения информации о хранилище", e)
            Result.failure(Exception("Ошибка получения информации о хранилище: ${e.message}"))
        }
    }

    /**
     * Отмечает сообщения как прочитанные
     */
    suspend fun markAsRead(messageIds: List<Long>): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            }

            val request = MessagesApiOuterClass.MarkAsReadRequest.newBuilder()
                .addAllMessageIds(messageIds)
                .build()

            messagesClient!!.markAsRead(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отметки сообщений как прочитанных", e)
            Result.failure(Exception("Ошибка отметки как прочитано: ${e.message}"))
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
        val profilePictureFileId: String = "",
        val profilePicturePreviewFileId: String = "",
        val profilePosterFileId: String = "",
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
        val onlinerEndpoint: String,
        val fastAuthEndpoint: String,
        val callsEndpoint: String,
        val livekitUrl: String
    )

    data class ChatData(
        val id: String,
        val title: String,
        val picture: String,
        val pictureFileId: String = "",
        val picturePreviewFileId: String = "",
        val isGroupChat: Boolean,
        val lastMessage: LastMessageData?,
        val memberIds: List<Long>,
        val countUnread: Long,
        val firstUnreadMessageId: Long,
        val chatType: Shared.ChatType = Shared.ChatType.CHAT_TYPE_REGULAR,
        val lastActivityAt: Long = lastMessage?.sentAt ?: 0L,
        val privateInviteState: Shared.PrivateChatInviteState = Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_ACCEPTED,
        val privateInviterUserId: Long = 0L,
        val hasDraft: Boolean = false
    )

    data class ChatPage(val chats: List<ChatData>, val totalCount: Int)

    data class PrivateChatCreateResult(val chat: MessagesApiOuterClass.Chat, val created: Boolean)

    data class LastMessageData(
        val id: Long,
        val senderId: Long,
        val text: String,
        val sentAt: Long,
        val readBy: List<Long>
    )

    data class ChatMemberInfo(
        val userId: Long,
        val firstName: String,
        val lastName: String
    )

    data class ChatFolder(
        val folderId: String,
        val folderName: String,
        val folderIcon: String,
        val chatIds: List<String>,
        val sortOrder: Int
    )

    /**
     * Извлекает GUID (fileId) из URL файла.
     * Сервер файлов возвращает URL вида: https://files.barkfluff.com/web/download/{fileId}
     */
    private fun extractGuidFromUrl(urlOrGuid: String): String {
        if (urlOrGuid.isBlank()) return urlOrGuid

        // Если это URL (начинается с http), извлекаем GUID из конца
        if (urlOrGuid.startsWith("http://") || urlOrGuid.startsWith("https://")) {
            val lastSegment = urlOrGuid.substringAfterLast("/")
            // Проверяем что это GUID (36 символов с дефисами)
            if (lastSegment.matches(Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOption.IGNORE_CASE))) {
                return lastSegment
            }
            // Если не GUID, возвращаем URL как есть
            return urlOrGuid
        }

        // Это уже GUID
        return urlOrGuid
    }

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

    data class SessionData(
        val id: Long,
        val createdAt: Long,
        val expirationAt: Long,
        val deviceId: String,
        val originalName: String,
        val customName: String,
        val appName: String,
        val os: String,
        val location: String
    )

    data class OtpStatus(
        val authenticatorEnabled: Boolean,
        val emailEnabled: Boolean
    )

    data class StorageInfo(
        val totalUsed: Long,
        val limit: Long,
        val byType: Map<String, Long>
    )

    data class ConfirmResetPasswordResult(
        val accessToken: String,
        val accessTokenExpiration: Long,
        val refreshToken: String,
        val refreshTokenExpiration: Long
    )

    class InvalidOldPasswordException : Exception("Неверный старый пароль")

    /**
     * Запрашивает сброс пароля через код на email.
     * @param email Email пользователя (если username null)
     * @param username Username пользователя (если email null)
     * @return resetId для подтверждения сброса
     */
    suspend fun resetPassword(email: String? = null, username: String? = null): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            if (email.isNullOrBlank() && username.isNullOrBlank()) {
                return@withContext Result.failure(IllegalArgumentException("Email или username должны быть указаны"))
            }

            val requestBuilder = IdentityApiOuterClass.ResetPasswordRequest.newBuilder()
                .setOtpType(IdentityApiOuterClass.OtpTypeId.Email)

            if (!email.isNullOrBlank()) {
                requestBuilder.email = email
            } else if (!username.isNullOrBlank()) {
                requestBuilder.username = username
            }

            val request = requestBuilder.build()
            val response = identityClient!!.resetPassword(request)

            Log.d(TAG, "resetPassword: resetId=${response.resetId}")
            Result.success(response.resetId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка запроса сброса пароля", e)
            Result.failure(Exception("Ошибка запроса сброса пароля: ${e.message}"))
        }
    }

    /**
     * Подтверждает сброс пароля кодом с почты.
     * После успешного подтверждения хеш пароля очищается и возвращаются новые токены.
     * @param resetId ID запроса сброса пароля
     * @param otpCode 6-значный код подтверждения
     * @return Новые токены (access и refresh)
     */
    suspend fun confirmResetPassword(resetId: String, otpCode: String): Result<ConfirmResetPasswordResult> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            val request = IdentityApiOuterClass.ConfirmResetPasswordRequest.newBuilder()
                .setResetId(resetId)
                .setOtpCode(otpCode)
                .build()

            val response = identityClient!!.confirmResetPassword(request)

            Log.d(TAG, "confirmResetPassword: Токены получены успешно")
            Result.success(
                ConfirmResetPasswordResult(
                    accessToken = response.accessToken.value,
                    accessTokenExpiration = response.accessToken.expirationDate.seconds * 1000,
                    refreshToken = response.refreshToken.value,
                    refreshTokenExpiration = response.refreshToken.expirationDate.seconds * 1000
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка подтверждения сброса пароля", e)
            Result.failure(Exception("Ошибка подтверждения сброса пароля: ${e.message}"))
        }
    }

    /**
     * Устанавливает новый пароль после сброса (без требования старого пароля).
     * Используется после успешного подтверждения кода сброса.
     * @param newPassword Новый пароль
     */
    suspend fun setPasswordAfterReset(newPassword: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (identityClient == null) {
                return@withContext Result.failure(IllegalStateException("Identity клиент не создан"))
            }

            // После сброса пароля хеш очищен, поэтому old_password не требуется
            val request = IdentityApiOuterClass.SetPasswordRequest.newBuilder()
                .setPassword(newPassword)
                .build()

            identityClient!!.setPassword(request)
            Log.d(TAG, "setPasswordAfterReset: Пароль успешно установлен")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка установки пароля после сброса", e)
            Result.failure(Exception("Ошибка установки пароля: ${e.message}"))
        }
    }

    // --- Стикеры ---

    /**
     * Получает список стикерпаков с сервера.
     */
    suspend fun listStickerPacks(offset: Int = 0, size: Int = 50): List<FilesApiOuterClass.StickerPackInfo>? = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                Log.e(TAG, "listStickerPacks: Files клиент не создан")
                return@withContext null
            }

            val pagination = barkfluff.shared.Shared.PageRequest.newBuilder()
                .setOffset(offset)
                .setSize(size)
                .build()

            val request = FilesApiOuterClass.ListStickerPacksRequest.newBuilder()
                .setPagination(pagination)
                .build()

            val response = filesClient!!.listStickerPacks(request)
            response.packsList
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка загрузки стикерпаков", e)
            null
        }
    }

    /**
     * Получает стикерпак со всеми стикерами.
     */
    suspend fun getStickerPack(packId: String): List<FilesApiOuterClass.StickerInfo>? = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                Log.e(TAG, "getStickerPack: Files клиент не создан")
                return@withContext null
            }

            val request = FilesApiOuterClass.GetStickerPackRequest.newBuilder()
                .setPackId(packId)
                .build()

            val response = filesClient!!.getStickerPack(request)
            response.stickersList
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка загрузки стикеров пака $packId", e)
            null
        }
    }

    // -------------------------------------------------------------------------
    // Персонализация
    // -------------------------------------------------------------------------

    /**
     * Получает персонализацию текущего пользователя с сервера.
     * Возвращает список fileId фоновых изображений.
     */
    suspend fun getPersonalization(): Result<List<String>> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            val request = UsersApiOuterClass.GetPersonalizationRequest.newBuilder().build()
            val response = usersClient!!.getPersonalization(request)
            Result.success(response.personalization.chatBackgroundFileIdsList.toList())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения персонализации", e)
            Result.failure(e)
        }
    }

    /**
     * Обновляет список fileId фоновых изображений на сервере.
     */
    suspend fun updatePersonalizationBackgrounds(fileIds: List<String>): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            val personalization = UsersApiOuterClass.UserPersonalizationData.newBuilder()
                .addAllChatBackgroundFileIds(fileIds)
                .build()
            val request = UsersApiOuterClass.UpdatePersonalizationRequest.newBuilder()
                .setPersonalization(personalization)
                .build()
            usersClient!!.updatePersonalization(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления персонализации", e)
            Result.failure(e)
        }
    }

    data class SyncedChatBackgroundSettings(
        val globalChatBackgroundFileId: String,
        val chatBackgroundFileIds: Map<String, String>
    )

    suspend fun getUserSettings(): Result<SyncedChatBackgroundSettings> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            val response = usersClient!!.getUserSettings(
                UsersApiOuterClass.GetUserSettingsRequest.newBuilder().build()
            )
            Result.success(
                SyncedChatBackgroundSettings(
                    globalChatBackgroundFileId = response.settings.globalChatBackgroundFileId,
                    chatBackgroundFileIds = response.settings.chatBackgroundsList
                        .filter { it.chatId.isNotBlank() && it.chatBackgroundFileId.isNotBlank() }
                        .associate { it.chatId to it.chatBackgroundFileId }
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения настроек фонов", e)
            Result.failure(e)
        }
    }

    suspend fun setGlobalChatBackground(fileId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            usersClient!!.setGlobalChatBackground(
                UsersApiOuterClass.SetGlobalChatBackgroundRequest.newBuilder()
                    .setChatBackgroundFileId(fileId)
                    .build()
            )
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка установки глобального фона", e)
            Result.failure(e)
        }
    }

    suspend fun setChatBackground(chatId: String, fileId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            }
            usersClient!!.setChatBackground(
                UsersApiOuterClass.SetChatBackgroundRequest.newBuilder()
                    .setChatId(chatId)
                    .setChatBackgroundFileId(fileId)
                    .build()
            )
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка установки фона чата", e)
            Result.failure(e)
        }
    }

    /**
     * Получает FileId постера профиля текущего пользователя
     */
    suspend fun getProfilePoster(): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Клиент Users не создан"))
            }
            val request = UsersApiOuterClass.GetProfilePosterRequest.newBuilder().build()
            val response = usersClient!!.getProfilePoster(request)
            Result.success(response.profilePosterFileId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения постера профиля", e)
            Result.failure(e)
        }
    }

    /**
     * Устанавливает постер профиля. Пустой fileId — удаляет постер.
     */
    suspend fun setProfilePoster(fileId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (usersClient == null) {
                return@withContext Result.failure(IllegalStateException("Клиент Users не создан"))
            }
            val request = UsersApiOuterClass.SetProfilePosterRequest.newBuilder()
                .setProfilePosterFileId(fileId)
                .build()
            usersClient!!.setProfilePoster(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка установки постера профиля", e)
            Result.failure(e)
        }
    }

    /**
     * Загружает постер профиля через Files API (JPEG 85%, MESSAGE_ATTACHMENT_IMAGE)
     * @return fileId загруженного файла
     */
    suspend fun uploadProfilePoster(jpegImageBytes: ByteArray): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Клиент Files не создан"))
            }
            val uploadUrlResult = getUploadUrl(FilesApiOuterClass.UploadFileType.USER_PROFILE_POSTER)
            if (uploadUrlResult.isFailure) {
                return@withContext Result.failure(uploadUrlResult.exceptionOrNull()!!)
            }
            val uploadData = uploadUrlResult.getOrNull()!!
            val fileId = uploadData.fileId
            Log.d(TAG, "Profile poster upload URL получен, fileId: $fileId")

            val boundary = "----BarkFluff${System.currentTimeMillis()}"
            val url = java.net.URL(uploadData.url)
            val connection = url.openConnection() as java.net.HttpURLConnection
            configureHttpConnection(connection)
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "multipart/form-data; boundary=$boundary")
            connection.doOutput = true
            connection.outputStream.use { out ->
                val writer = out.bufferedWriter()
                writer.write("--$boundary\r\n")
                writer.write("Content-Disposition: form-data; name=\"file\"; filename=\"poster.jpg\"\r\n")
                writer.write("Content-Type: image/jpeg\r\n")
                writer.write("\r\n")
                writer.flush()
                out.write(jpegImageBytes)
                out.flush()
                writer.write("\r\n")
                writer.write("--$boundary--\r\n")
                writer.flush()
            }
            val responseCode = connection.responseCode
            connection.disconnect()
            if (responseCode !in 200..299) {
                return@withContext Result.failure(Exception("Upload failed: HTTP $responseCode"))
            }
            Log.d(TAG, "Постер профиля загружен, fileId: $fileId")
            Result.success(fileId)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка загрузки постера профиля", e)
            Result.failure(Exception("Ошибка загрузки постера: ${e.message}"))
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Папки чатов (UsersApi)
    // ═══════════════════════════════════════════════════════════════

    private fun UsersApiOuterClass.ChatFolderData.toDomain(): ChatFolder = ChatFolder(
        folderId = folderId,
        folderName = folderName,
        folderIcon = folderIcon,
        chatIds = chatListList.toList(),
        sortOrder = sortOrder
    )

    suspend fun getChatFolders(): Result<List<ChatFolder>> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.GetChatFoldersRequest.getDefaultInstance()
            val response = client.getChatFolders(request)
            Result.success(response.foldersList.map { it.toDomain() })
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения папок чатов", e)
            Result.failure(e)
        }
    }

    suspend fun createChatFolder(name: String, icon: String): Result<ChatFolder> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.CreateChatFolderRequest.newBuilder()
                .setFolderName(name)
                .setFolderIcon(icon)
                .build()
            val response = client.createChatFolder(request)
            Result.success(response.folder.toDomain())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания папки", e)
            Result.failure(e)
        }
    }

    suspend fun updateChatFolder(
        folderId: String,
        name: String? = null,
        icon: String? = null,
        chatList: List<String>? = null
    ): Result<ChatFolder> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val builder = UsersApiOuterClass.UpdateChatFolderRequest.newBuilder()
                .setFolderId(folderId)
            if (name != null) builder.folderName = name
            if (icon != null) builder.folderIcon = icon
            if (chatList != null) {
                builder.hasChatListUpdate = true
                builder.addAllChatList(chatList)
            }
            val response = client.updateChatFolder(builder.build())
            Result.success(response.folder.toDomain())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления папки $folderId", e)
            Result.failure(e)
        }
    }

    suspend fun deleteChatFolder(folderId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.DeleteChatFolderRequest.newBuilder()
                .setFolderId(folderId)
                .build()
            client.deleteChatFolder(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка удаления папки $folderId", e)
            Result.failure(e)
        }
    }

    suspend fun addChatToFolder(folderId: String, chatId: String): Result<ChatFolder> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.AddChatToFolderRequest.newBuilder()
                .setFolderId(folderId)
                .setChatId(chatId)
                .build()
            val response = client.addChatToFolder(request)
            Result.success(response.folder.toDomain())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка добавления чата $chatId в папку $folderId", e)
            Result.failure(e)
        }
    }

    suspend fun removeChatFromFolder(folderId: String, chatId: String): Result<ChatFolder> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.RemoveChatFromFolderRequest.newBuilder()
                .setFolderId(folderId)
                .setChatId(chatId)
                .build()
            val response = client.removeChatFromFolder(request)
            Result.success(response.folder.toDomain())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка удаления чата $chatId из папки $folderId", e)
            Result.failure(e)
        }
    }

    suspend fun reorderChatFolders(orders: List<Pair<String, Int>>): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val builder = UsersApiOuterClass.ReorderChatFoldersRequest.newBuilder()
            for ((folderId, sortOrder) in orders) {
                builder.addOrders(
                    UsersApiOuterClass.ChatFolderOrder.newBuilder()
                        .setFolderId(folderId)
                        .setSortOrder(sortOrder)
                        .build()
                )
            }
            client.reorderChatFolders(builder.build())
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка изменения порядка папок", e)
            Result.failure(e)
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Закреплённые сообщения (MessagesApi)
    // ═══════════════════════════════════════════════════════════════

    suspend fun pinMessage(chatId: String, messageId: Long): Result<Shared.PinnedMessageInfo> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.PinMessageRequest.newBuilder()
                .setChatId(chatId)
                .setMessageId(messageId)
                .build()
            val response = client.pinMessage(request)
            Result.success(response.pinned)
        } catch (e: StatusException) {
            val errorCode = e.trailers?.get(ERROR_CODE_KEY)
            Log.e(TAG, "Ошибка закрепления сообщения $messageId, errorCode=$errorCode", e)
            Result.failure(PinErrorException(errorCode, e))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка закрепления сообщения $messageId", e)
            Result.failure(e)
        }
    }

    suspend fun unpinMessage(chatId: String, messageId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.UnpinMessageRequest.newBuilder()
                .setChatId(chatId)
                .setMessageId(messageId)
                .build()
            client.unpinMessage(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка открепления сообщения $messageId", e)
            Result.failure(e)
        }
    }

    suspend fun listPinnedMessages(
        chatId: String,
        offset: Int = 0,
        size: Int = 50
    ): Result<Pair<List<Shared.PinnedMessageInfo>, Int>> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.ListPinnedMessagesRequest.newBuilder()
                .setChatId(chatId)
                .setPagination(
                    Shared.PageRequest.newBuilder()
                        .setOffset(offset)
                        .setSize(size)
                        .build()
                )
                .build()
            val response = client.listPinnedMessages(request)
            Result.success(response.pinnedList.toList() to response.totalCount)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения закреплённых сообщений чата $chatId", e)
            Result.failure(e)
        }
    }

    suspend fun unpinAllMessages(chatId: String): Result<Int> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.UnpinAllRequest.newBuilder()
                .setChatId(chatId)
                .build()
            val response = client.unpinAll(request)
            Result.success(response.unpinnedCount)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка открепления всех сообщений чата $chatId", e)
            Result.failure(e)
        }
    }

    class PinErrorException(val errorCode: String?, cause: Throwable) : Exception(cause) {
        companion object {
            const val ERROR_TOO_MANY_PINNED = "F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23"
        }
        val isTooManyPinned: Boolean get() = errorCode.equals(ERROR_TOO_MANY_PINNED, ignoreCase = true)
    }

    /**
     * Найти Chat по ID через ListChats. Используется приватным чатом для получения kdf_salt+verifier
     * после повторного открытия (когда локальный ключ не сохранён).
     */
    suspend fun getChat(chatId: String): Result<MessagesApiOuterClass.Chat> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            var offset = 0
            while (true) {
                val response = client.listChats(MessagesApiOuterClass.ListChatsRequest.newBuilder()
                    .setPagination(Shared.PageRequest.newBuilder().setOffset(offset).setSize(50).build())
                    .build())
                response.chatsList.firstOrNull { it.id == chatId }?.let { return@withContext Result.success(it) }
                offset += response.chatsCount
                if (response.chatsCount == 0 || offset >= response.totalCount) break
            }
            Result.failure(NoSuchElementException("Чат $chatId не найден"))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения чата $chatId", e)
            Result.failure(e)
        }
    }

    // ==================== Приватные чаты (E2E через passphrase) ====================

    suspend fun createPrivateChat(
        peerUserId: Long,
        kdfSalt: ByteArray,
        passphraseVerifier: ByteArray
    ): Result<PrivateChatCreateResult> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.CreatePrivateChatRequest.newBuilder()
                .setPeerUserId(peerUserId)
                .setKdfSalt(com.google.protobuf.ByteString.copyFrom(kdfSalt))
                .setPassphraseVerifier(com.google.protobuf.ByteString.copyFrom(passphraseVerifier))
                .build()
            val response = client.createPrivateChat(request)
            Result.success(PrivateChatCreateResult(response.chat, response.created))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания приватного чата с $peerUserId", e)
            Result.failure(e)
        }
    }

    suspend fun acceptPrivateChat(chatId: String): Result<MessagesApiOuterClass.Chat> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.AcceptPrivateChatRequest.newBuilder().setChatId(chatId).build()
            Result.success(client.acceptPrivateChat(request).chat)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка принятия приватного чата $chatId", e)
            Result.failure(e)
        }
    }

    suspend fun rejectPrivateChat(chatId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.RejectPrivateChatRequest.newBuilder().setChatId(chatId).build()
            client.rejectPrivateChat(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отклонения приватного чата $chatId", e)
            Result.failure(e)
        }
    }

    suspend fun sendPrivateMessage(
        chatId: String,
        ciphertext: ByteArray,
        nonce: ByteArray,
        associatedData: ByteArray
    ): Result<Shared.EncryptedMessage> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.SendPrivateMessageRequest.newBuilder()
                .setChatId(chatId)
                .setCiphertext(com.google.protobuf.ByteString.copyFrom(ciphertext))
                .setNonce(com.google.protobuf.ByteString.copyFrom(nonce))
                .setAssociatedData(com.google.protobuf.ByteString.copyFrom(associatedData))
                .build()
            Result.success(client.sendPrivateMessage(request).message)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отправки приватного сообщения в $chatId", e)
            Result.failure(e)
        }
    }

    suspend fun listPrivateMessages(
        chatId: String,
        fromMessageId: Long,
        offsetBefore: Int,
        offsetAfter: Int
    ): Result<List<Shared.EncryptedMessage>> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.ListPrivateMessagesRequest.newBuilder()
                .setChatId(chatId)
                .setFromMessageId(fromMessageId)
                .setOffsetBefore(offsetBefore)
                .setOffsetAfter(offsetAfter)
                .build()
            Result.success(client.listPrivateMessages(request).messagesList.toList())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения приватных сообщений чата $chatId", e)
            Result.failure(e)
        }
    }

    suspend fun markPrivateMessagesAsRead(chatId: String, lastReadMessageId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            client.markPrivateMessagesAsRead(MessagesApiOuterClass.MarkPrivateMessagesAsReadRequest.newBuilder()
                .setChatId(chatId)
                .setLastReadMessageId(lastReadMessageId)
                .build())
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отметки приватных сообщений прочитанными", e)
            Result.failure(e)
        }
    }

    suspend fun editPrivateMessage(
        messageId: Long,
        ciphertext: ByteArray,
        nonce: ByteArray,
        associatedData: ByteArray
    ): Result<Shared.EncryptedMessage> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.EditPrivateMessageRequest.newBuilder()
                .setMessageId(messageId)
                .setCiphertext(com.google.protobuf.ByteString.copyFrom(ciphertext))
                .setNonce(com.google.protobuf.ByteString.copyFrom(nonce))
                .setAssociatedData(com.google.protobuf.ByteString.copyFrom(associatedData))
                .build()
            Result.success(client.editPrivateMessage(request).message)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка редактирования приватного сообщения $messageId", e)
            Result.failure(e)
        }
    }

    suspend fun deletePrivateMessage(messageId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.DeletePrivateMessageRequest.newBuilder().setMessageId(messageId).build()
            client.deletePrivateMessage(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка удаления приватного сообщения $messageId", e)
            Result.failure(e)
        }
    }

    // ==================== Секретные чаты (Signal Double Ratchet) ====================

    data class SecretInviteSent(val inviteId: String, val expiresAtSeconds: Long)
    data class SecretMessageSent(val messageId: String, val expiresAtSeconds: Long)

    suspend fun sendSecretChatInvite(
        recipientUserId: Long,
        recipientDeviceId: String,
        initialEnvelope: ByteArray
    ): Result<SecretInviteSent> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.SendSecretChatInviteRequest.newBuilder()
                .setRecipientUserId(recipientUserId)
                .setRecipientDeviceId(recipientDeviceId)
                .setInitialEnvelope(com.google.protobuf.ByteString.copyFrom(initialEnvelope))
                .build()
            val resp = client.sendSecretChatInvite(request)
            Result.success(SecretInviteSent(resp.inviteId, resp.inviteExpiresAt.seconds))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отправки инвайта секретного чата", e)
            Result.failure(e)
        }
    }

    suspend fun acceptSecretChatInvite(inviteId: String, responseEnvelope: ByteArray = ByteArray(0)): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.AcceptSecretChatInviteRequest.newBuilder()
                .setInviteId(inviteId)
                .setResponseEnvelope(com.google.protobuf.ByteString.copyFrom(responseEnvelope))
                .build()
            client.acceptSecretChatInvite(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка принятия секретного инвайта $inviteId", e)
            Result.failure(e)
        }
    }

    suspend fun rejectSecretChatInvite(inviteId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.RejectSecretChatInviteRequest.newBuilder().setInviteId(inviteId).build()
            client.rejectSecretChatInvite(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отклонения секретного инвайта $inviteId", e)
            Result.failure(e)
        }
    }

    suspend fun sendSecretMessage(
        recipientUserId: Long,
        recipientDeviceId: String,
        envelope: ByteArray
    ): Result<SecretMessageSent> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.SendSecretMessageRequest.newBuilder()
                .setRecipientUserId(recipientUserId)
                .setRecipientDeviceId(recipientDeviceId)
                .setEnvelope(com.google.protobuf.ByteString.copyFrom(envelope))
                .build()
            val resp = client.sendSecretMessage(request)
            Result.success(SecretMessageSent(resp.messageId, resp.expiresAt.seconds))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка отправки секретного сообщения", e)
            Result.failure(e)
        }
    }

    suspend fun ackSecretMessage(messageId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = messagesClient ?: return@withContext Result.failure(IllegalStateException("Messages клиент не создан"))
            val request = MessagesApiOuterClass.AckSecretMessageRequest.newBuilder().setMessageId(messageId).build()
            client.ackSecretMessage(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка подтверждения секретного сообщения $messageId", e)
            Result.failure(e)
        }
    }

    // ==================== Prekey bundle (X3DH) ====================

    suspend fun registerPrekeyBundle(
        registrationId: Int,
        identityPubkey: ByteArray,
        signedPreKey: UsersApiOuterClass.SignedPreKey,
        oneTimePreKeys: List<UsersApiOuterClass.OneTimePreKey>
    ): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.RegisterPrekeyBundleRequest.newBuilder()
                .setRegistrationId(registrationId)
                .setIdentityPubkey(com.google.protobuf.ByteString.copyFrom(identityPubkey))
                .setSignedPrekey(signedPreKey)
                .addAllOneTimePrekeys(oneTimePreKeys)
                .build()
            client.registerPrekeyBundle(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка регистрации prekey-bundle", e)
            Result.failure(e)
        }
    }

    suspend fun fetchPrekeyBundle(
        userId: Long,
        deviceId: String
    ): Result<UsersApiOuterClass.FetchPrekeyBundleResponse> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.FetchPrekeyBundleRequest.newBuilder()
                .setUserId(userId)
                .setDeviceId(deviceId)
                .build()
            Result.success(client.fetchPrekeyBundle(request))
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения prekey-bundle для $userId/$deviceId", e)
            Result.failure(e)
        }
    }

    suspend fun listPeerDevices(userId: Long): Result<List<UsersApiOuterClass.PeerDeviceInfo>> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.ListPeerDevicesRequest.newBuilder().setUserId(userId).build()
            Result.success(client.listPeerDevices(request).devicesList.toList())
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения списка устройств пользователя $userId", e)
            Result.failure(e)
        }
    }

    suspend fun replenishOneTimePrekeys(prekeys: List<UsersApiOuterClass.OneTimePreKey>): Result<Int> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.ReplenishOneTimePrekeysRequest.newBuilder()
                .addAllPrekeys(prekeys)
                .build()
            Result.success(client.replenishOneTimePrekeys(request).totalOneTimePrekeys)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка пополнения one-time prekeys", e)
            Result.failure(e)
        }
    }

    suspend fun rotateSignedPrekey(signedPreKey: UsersApiOuterClass.SignedPreKey): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val client = usersClient ?: return@withContext Result.failure(IllegalStateException("Users клиент не создан"))
            val request = UsersApiOuterClass.RotateSignedPrekeyRequest.newBuilder().setSignedPrekey(signedPreKey).build()
            client.rotateSignedPrekey(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка ротации signed prekey", e)
            Result.failure(e)
        }
    }
}
