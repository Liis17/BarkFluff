package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.FirebaseTokenHelper
import com.barkfluff.client.utils.ServerInfoRefreshResult
import com.barkfluff.client.utils.refreshServerInfoFromBeacon
import kotlinx.coroutines.launch
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope

/**
 * SplashActivity - точка входа приложения
 * Проверяет сохраненные данные и решает, какую страницу показывать:
 * - Если нет настроек сервера -> WelcomeActivity -> SelectServerActivity
 * - Если есть сервер, но нет токенов -> LoginActivity
 * - Если есть сервер и refresh токен -> MainActivity (с проверкой/обновлением токена)
 */
class SplashActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "SplashActivity"
        private const val TOKEN_BUFFER_MINUTES = 5
    }

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager(applicationContext)

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

            val refreshResult = refreshServerInfoFromBeacon(grpcManager, globalParam, applicationContext)
            if (refreshResult == ServerInfoRefreshResult.CertificateApprovalRequired) {
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
            val createResult = grpcManager.createIdentityClient(identityAddress, this)
            if (createResult.isFailure) {
                return false
            }

            // Обновляем токен
            val refreshToken = globalParam.refreshToken
            if (refreshToken == null) {
                return false
            }

            val refreshResult = grpcManager.refreshAccessToken(refreshToken, globalParam.refreshTokenExpiration)
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
        } finally {
            grpcManager.shutdown()
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
                grpcManager.createIdentityClient(identityAddress, this)
            }
            if (usersAddress.isNotBlank()) {
                grpcManager.createUsersClient(usersAddress, this)
            }

            val shouldContinue = coroutineScope {
                // Загружаем профиль и синхронизируемые настройки параллельно.
                val userSettingsDeferred = async { grpcManager.getUserSettings() }
                var userDataResult = grpcManager.getCurrentUserData()
                var reloadUserSettings = false

                // Если 401 — токен инвалидирован на сервере, пробуем обновить принудительно
                if (userDataResult.isFailure) {
                    val errMsg = userDataResult.exceptionOrNull()?.message ?: ""
                    if (errMsg.contains("401") || errMsg.contains("UNAUTHENTICATED")) {
                        val refreshed = grpcManager.forceRefreshToken(this@SplashActivity)
                        if (!refreshed) {
                            // Refresh тоже невалиден — на Login
                            globalParam.clearUserData()
                            navigateToLogin()
                            userSettingsDeferred.cancel()
                            return@coroutineScope false
                        }
                        // Повторяем запрос с новым токеном
                        userDataResult = grpcManager.getCurrentUserData()
                        reloadUserSettings = true
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
                    grpcManager.getUserSettings()
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
                FirebaseTokenHelper.getTokenAndSendToServer(this, grpcManager)
            } catch (e: Exception) {
                // Не критично — продолжаем
            }
        } catch (e: Exception) {
            // Ошибка загрузки данных пользователя - не критично, продолжаем
        } finally {
            grpcManager.shutdown()
        }

        navigateToChats()
    }

    private fun navigateToWelcome() {
        val intent = Intent(this, WelcomeActivity::class.java)
        startActivity(intent)
        finish()
    }

    private fun navigateToLogin() {
        val intent = Intent(this, LoginActivity::class.java)
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
        // Пересоздаём каналы app-level grpcManager — гарантирует свежие каналы
        // с актуальным токеном (AuthInterceptor читает из GlobalParam динамически,
        // но каналы могут быть устаревшими после предыдущей сессии)
        val app = applicationContext as BarkFluffApplication
        app.grpcManager.recreateAllClients(this, globalParam)

        val intent = Intent(this, MainActivity::class.java)
        // Пробрасываем chatId из уведомления, если есть
        val chatId = MainActivity.pendingChatId
        if (chatId != null) {
            intent.putExtra(NotificationHelper.EXTRA_CHAT_ID, chatId)
            MainActivity.pendingChatId = null
        }
        startActivity(intent)
        finish()
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }
}
