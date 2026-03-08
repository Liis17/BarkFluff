package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.launch

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
        grpcManager = GrpcManager()

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
        return globalParam.socketBeacon.isNotBlank() &&
               globalParam.socketIdentity.isNotBlank()
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

            // Загружаем данные текущего пользователя
            val userDataResult = grpcManager.getCurrentUserData()
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

    private fun navigateToChats() {
        val intent = Intent(this, MainActivity::class.java)
        startActivity(intent)
        finish()
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }
}
