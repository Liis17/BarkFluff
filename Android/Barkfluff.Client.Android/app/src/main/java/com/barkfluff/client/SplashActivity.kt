package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.ServerDiscoveryGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.gateway.UserSettingsGateway
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.grpc.TokenValidity
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.FirebaseTokenHelper
import com.barkfluff.client.utils.LogoutHelper
import com.barkfluff.client.utils.SessionGate
import com.barkfluff.client.utils.gateTokenValidity
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.barkfluff.client.utils.ServerInfoRefreshResult
import com.barkfluff.client.utils.refreshServerInfoFromBeacon
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withTimeoutOrNull

/**
 * SplashActivity - точка входа приложения
 * Проверяет сохраненные данные и решает, какую страницу показывать:
 * - Если нет настроек сервера -> WelcomeActivity -> SelectServerActivity
 * - Если есть сервер, но нет токенов -> LoginActivity
 * - Если есть сервер и refresh токен -> MainActivity (с проверкой/обновлением токена)
 */
@AndroidEntryPoint
class SplashActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "SplashActivity"
        private const val TOKEN_BUFFER_MINUTES = 5
        private const val SERVER_INFO_REFRESH_TIMEOUT_MS = 3_000L
    }

    private lateinit var globalParam: GlobalParam
    @javax.inject.Inject lateinit var serverDiscoveryGateway: ServerDiscoveryGateway
    @javax.inject.Inject lateinit var authGateway: AuthGateway
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var userSettingsGateway: UserSettingsGateway
    @javax.inject.Inject lateinit var clientRegistry: GrpcClientRegistry

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        globalParam = GlobalParam(this)

        // Проверяем данные и переходим на нужный экран
        checkDataAndNavigate()
    }

    private fun checkDataAndNavigate() {
        lifecycleScope.launch {
            val hasServerConfig = hasServerConfiguration()
            
            if (!hasServerConfig) {
                // Нет настроек сервера -> WelcomeActivity
                navigateToWelcome()
                return@launch
            }

            val refreshResult = withTimeoutOrNull(SERVER_INFO_REFRESH_TIMEOUT_MS) {
                refreshServerInfoFromBeacon(serverDiscoveryGateway, globalParam, applicationContext)
            } ?: ServerInfoRefreshResult.Unavailable
            if (
                refreshResult == ServerInfoRefreshResult.CertificateApprovalRequired ||
                refreshResult == ServerInfoRefreshResult.CertificateRejected
            ) {
                navigateToCertificateReview()
                return@launch
            }

            // Local chat cache must be reachable even when Beacon or token refresh is offline.
            if (globalParam.refreshToken != null && globalParam.socketIdentity.isNotBlank()) {
                navigateToChats()
                return@launch
            }
            if (globalParam.socketIdentity.isBlank()) {
                navigateToWelcome()
                return@launch
            }

            // Есть настройки сервера, проверяем токены
            val hasRefreshToken = globalParam.refreshToken != null
            val hasAccessToken = globalParam.accessToken != null
            val isAccessTokenExpired = isAccessTokenExpired()

            when {
                !hasRefreshToken -> {
                    // Нет refresh токена -> LoginActivity
                    navigateToLogin()
                }
                hasRefreshToken && (!hasAccessToken || isAccessTokenExpired) -> {
                    // Есть refresh токен, но нет access токена или он истек
                    // Пытаемся обновить токен
                    val refreshResult = tryRefreshToken()
                    if (refreshResult) {
                        // Успешно обновили токен -> загружаем данные пользователя и переходим в чаты
                        loadUserDataAndNavigateToChats()
                    } else {
                        // Не удалось обновить токен -> LoginActivity
                        navigateToLogin()
                    }
                }
                hasRefreshToken && hasAccessToken && !isAccessTokenExpired -> {
                    // Все токены на месте и валидны -> загружаем данные и переходим в чаты
                    loadUserDataAndNavigateToChats()
                }
                else -> {
                    // fallback -> LoginActivity
                    navigateToLogin()
                }
            }
        }
    }

    /**
     * Проверяет, есть ли настройки сервера (адрес beacon)
     */
    private fun hasServerConfiguration(): Boolean {
        return globalParam.socketBeacon.isNotBlank()
    }

    /**
     * Проверяет, истек ли access токен (с учетом буфера времени)
     */
    private fun isAccessTokenExpired(): Boolean {
        val accessTokenExpiration = globalParam.accessTokenExpiration
        if (accessTokenExpiration <= 0) return true

        val now = System.currentTimeMillis()
        val bufferMillis = TOKEN_BUFFER_MINUTES * 60 * 1000L
        return now + bufferMillis >= accessTokenExpiration
    }

    /**
     * Пытается обновить access токен используя refresh токен
     */
    private suspend fun tryRefreshToken(): Boolean {
        return try {
            val identityAddress = globalParam.socketIdentity
            if (identityAddress.isBlank()) {
                return false
            }

            // Создаем Identity клиент с interceptor для авторизованных вызовов
            val createResult = authGateway.createIdentity(identityAddress, this)
            if (createResult.isFailure) {
                return false
            }

            // Обновляем токен
            val refreshToken = globalParam.refreshToken
            if (refreshToken == null) {
                return false
            }

            val refreshResult = authGateway.refresh(refreshToken, globalParam.refreshTokenExpiration)
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
            false
        }
    }

    /**
     * Загружает данные пользователя и переходит в чаты
     */
    private suspend fun loadUserDataAndNavigateToChats() {
        try {
            // Создаем клиенты для загрузки данных
            val identityAddress = globalParam.socketIdentity
            val usersAddress = globalParam.socketUsers

            if (identityAddress.isNotBlank()) {
                authGateway.createIdentity(identityAddress, this)
            }
            if (usersAddress.isNotBlank()) {
                clientRegistry.createUsersClient(usersAddress, this, includeDeviceInfo = true)
            }

            val shouldContinue = coroutineScope {
                // Загружаем профиль и синхронизируемые настройки параллельно.
                val userSettingsDeferred = async { userSettingsGateway.syncedChatBackgrounds() }
                var userDataResult = userProfileGateway.currentUser()
                var reloadUserSettings = false

                // Если 401 — токен инвалидирован на сервере, пробуем обновить принудительно
                if (userDataResult.isFailure) {
                    val errMsg = userDataResult.exceptionOrNull()?.message ?: ""
                    if (errMsg.contains("401") || errMsg.contains("UNAUTHENTICATED")) {
                        // Only a rejected refresh is destructive; an unavailable Identity keeps
                        // the session and continues to the offline chat cache.
                        when (val validity = authGateway.tokenValidity(forceRefresh = true)) {
                            TokenValidity.REJECTED -> {
                                userSettingsDeferred.cancel()
                                handleRejectedSession(validity)
                                return@coroutineScope false
                            }
                            TokenValidity.UNAVAILABLE -> Unit
                            TokenValidity.VALID -> {
                                // Повторяем запрос с новым токеном
                                userDataResult = userProfileGateway.currentUser()
                                reloadUserSettings = true
                            }
                        }
                    }
                }

                if (userDataResult.isSuccess) {
                    val userData = userDataResult.getOrNull()
                    if (userData != null) {
                        globalParam.userId = userData.userId
                        globalParam.userName = userData.username
                        globalParam.firstName = userData.firstName
                        globalParam.lastName = userData.lastName
                        globalParam.description = userData.bio
                    }
                }
                // Настройки фонов не входят в публичный профиль: забираем отдельным RPC при запуске.
                // Ошибка не блокирует вход — до следующего удачного старта используется кэш.
                val userSettingsResult = if (reloadUserSettings) {
                    userSettingsDeferred.cancel()
                    userSettingsGateway.syncedChatBackgrounds()
                } else {
                    userSettingsDeferred.await()
                }
                userSettingsResult.onSuccess { settings ->
                    globalParam.applyChatBackgroundSettings(
                        settings.globalChatBackgroundFileId,
                        settings.chatBackgroundFileIds
                    )
                }
                true
            }
            if (!shouldContinue) return
            // Отправляем актуальный FCM-токен на сервер (уже залогинен — не пересоздаём)
            try {
                FirebaseTokenHelper.getTokenAndSendToServer(this, userProfileGateway)
            } catch (e: Exception) {
                // Не критично — продолжаем
            }
        } catch (e: Exception) {
            // Ошибка загрузки данных пользователя - не критично, продолжаем
        }
        navigateToChats()
    }

    /** A failed cleanup journal keeps credentials; offer an explicit retry instead of a stuck Splash. */
    private suspend fun handleRejectedSession(validity: TokenValidity) {
        val app = applicationContext as BarkFluffApplication
        app.realtimeService.shutdown()
        val gate = gateTokenValidity(validity) { LogoutHelper.clearCredentials(this, app.prekeyManager) }
        if (gate == SessionGate.LOGIN) {
            navigateToLogin()
            return
        }
        if (isFinishing || isDestroyed) return
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.error)
            .setMessage(R.string.crypto_cleanup_failed)
            .setCancelable(false)
            .setPositiveButton(R.string.action_retry) { _, _ ->
                lifecycleScope.launch { handleRejectedSession(validity) }
            }
            .setNegativeButton(R.string.btn_close) { _, _ -> finish() }
            .show()
    }

    private fun navigateToWelcome() {
        val intent = Intent(this, WelcomeActivity::class.java)
        startActivity(intent)
        finish()
    }

    private fun navigateToLogin() {
        val intent = Intent(this, LoginActivity::class.java)
        if (this.intent.hasExtra(NotificationHelper.EXTRA_KIND)) {
            intent.action = this.intent.action
            intent.data = this.intent.data
            listOf(
                NotificationHelper.EXTRA_SCOPE_ID,
                NotificationHelper.EXTRA_KIND,
                NotificationHelper.EXTRA_CHAT_ID,
                NotificationHelper.EXTRA_EVENT_ID,
                NotificationHelper.EXTRA_THREAD_ID,
            ).forEach { key ->
                if (this.intent.hasExtra(key)) intent.putExtra(key, this.intent.getStringExtra(key))
            }
            if (this.intent.hasExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT)) {
                intent.putExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT,
                    this.intent.getBooleanExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT, false))
            }
        }
        startActivity(intent)
        finish()
    }

    private fun navigateToCertificateReview() {
        val intent = Intent(this, SelectServerActivity::class.java)
            .putExtra(SelectServerActivity.EXTRA_TRUST_REVIEW_ADDRESS, globalParam.socketBeacon)
        startActivity(intent)
        finish()
    }

    private fun navigateToChats() {
        val expectedScope = intent.getStringExtra(NotificationHelper.EXTRA_SCOPE_ID)
        val isNotificationRoute = intent.hasExtra(NotificationHelper.EXTRA_KIND)
        val beforeInitScope = com.barkfluff.client.cache.CacheScope.from(globalParam)?.id
        // Создаём недостающие каналы. Живые не пересоздаём: токен interceptor читает на каждый вызов,
        // адрес ноды lazy-геттеры сверяют с GlobalParam, а пересоздание оборвало бы уже запущенные стримы.
        clientRegistry.initAllClients(globalParam, this)

        val intent = Intent(this, MainActivity::class.java)
        val afterInitScope = com.barkfluff.client.cache.CacheScope.from(GlobalParam(this))?.id
        if (isNotificationRoute && (expectedScope != beforeInitScope || expectedScope != afterInitScope)) {
            Log.w("SplashActivity", "Forwarding notification route for a stale scope to MainActivity for rejection")
        }
        if (isNotificationRoute) {
            intent.action = this.intent.action
            intent.data = this.intent.data
            this.intent.extras?.let(intent::putExtras)
        }
        startActivity(intent)
        finish()
    }

}
