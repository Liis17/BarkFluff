package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityChatsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch

/**
 * Экран чатов (заглушка)
 * При запуске проверяет валидность токена и обновляет его при необходимости
 */
class ChatsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityChatsBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    companion object {
        private const val TAG = "ChatsActivity"
        private const val TOKEN_BUFFER_MINUTES = 5
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityChatsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager()

        // Проверяем и обновляем токен при запуске
        checkAndUpdateToken()
    }

    private fun checkAndUpdateToken() {
        lifecycleScope.launch {
            val hasRefreshToken = globalParam.refreshToken != null
            val hasAccessToken = globalParam.accessToken != null
            val isAccessTokenExpired = isAccessTokenExpired()

            when {
                !hasRefreshToken -> {
                    // Нет refresh токена -> переходим на логин
                    navigateToLogin()
                }
                hasRefreshToken && (!hasAccessToken || isAccessTokenExpired) -> {
                    // Есть refresh токен, но нет access токена или он истек
                    // Пытаемся обновить токен
                    val refreshResult = tryRefreshToken()
                    if (!refreshResult) {
                        // Не удалось обновить токен -> переходим на логин
                        navigateToLogin()
                    }
                    // Если успешно обновили, продолжаем работу
                }
            }
            // Если все токены на месте и валидны, продолжаем работу
        }
    }

    private fun isAccessTokenExpired(): Boolean {
        val accessTokenExpiration = globalParam.accessTokenExpiration
        if (accessTokenExpiration <= 0) return true

        val now = System.currentTimeMillis()
        val bufferMillis = TOKEN_BUFFER_MINUTES * 60 * 1000L
        return now + bufferMillis >= accessTokenExpiration
    }

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
        }
    }

    private fun navigateToLogin() {
        val intent = Intent(this, LoginActivity::class.java)
        startActivity(intent)
        finish()
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        MaterialAlertDialogBuilder(this)
            .setTitle("Выход из приложения")
            .setMessage("Вы действительно хотите выйти?")
            .setPositiveButton("Выйти") { _, _ ->
                super.onBackPressed()
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }
}
