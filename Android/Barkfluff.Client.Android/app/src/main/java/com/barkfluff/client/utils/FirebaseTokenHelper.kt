package com.barkfluff.client.utils

import android.content.Context
import android.util.Log
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withContext

/**
 * Хелпер для работы с Firebase токеном.
 * Получает токен и отправляет его на сервер.
 */
object FirebaseTokenHelper {

    private const val TAG = "FirebaseTokenHelper"

    /**
     * Получает Firebase токен и отправляет его на сервер.
     * Если токен уже сохранен локально, использует его.
     * Иначе запрашивает новый у Firebase.
     */
    suspend fun getTokenAndSendToServer(context: Context, grpcManager: GrpcManager) {
        try {
            val globalParam = GlobalParam(context)
            var token = globalParam.firebaseToken

            // Если токен еще не сохранен, получаем его из Firebase
            if (token.isBlank()) {
                Log.d(TAG, "getTokenAndSendToServer: локальный токен пуст, запрашиваем у Firebase")
                token = withContext(Dispatchers.IO) {
                    FirebaseMessaging.getInstance().token.await()
                }
                globalParam.firebaseToken = token
                Log.d(TAG, "getTokenAndSendToServer: получен новый токен: ${token.take(20)}...")
            } else {
                Log.d(TAG, "getTokenAndSendToServer: используем сохраненный токен: ${token.take(20)}...")
            }

            // Отправляем токен на сервер
            sendTokenToServer(grpcManager, token)

        } catch (e: Exception) {
            Log.e(TAG, "getTokenAndSendToServer: ошибка", e)
        }
    }

    /**
     * Принудительно запрашивает новый токен у Firebase и отправляет на сервер.
     * Используется при обновлении токена (onNewToken в сервисе).
     */
    suspend fun refreshTokenAndSendToServer(context: Context, grpcManager: GrpcManager) {
        try {
            Log.d(TAG, "refreshTokenAndSendToServer: запрашиваем новый токен")

            val token = withContext(Dispatchers.IO) {
                FirebaseMessaging.getInstance().token.await()
            }

            val globalParam = GlobalParam(context)
            globalParam.firebaseToken = token

            Log.d(TAG, "refreshTokenAndSendToServer: получен токен: ${token.take(20)}...")

            sendTokenToServer(grpcManager, token)

        } catch (e: Exception) {
            Log.e(TAG, "refreshTokenAndSendToServer: ошибка", e)
        }
    }

    private suspend fun sendTokenToServer(grpcManager: GrpcManager, token: String) {
        try {
            val result = grpcManager.setFirebaseToken(token)
            if (result.isSuccess) {
                Log.i(TAG, "sendTokenToServer: токен успешно отправлен на сервер")
            } else {
                Log.e(TAG, "sendTokenToServer: ошибка: ${result.exceptionOrNull()?.message}")
            }
        } catch (e: Exception) {
            Log.e(TAG, "sendTokenToServer: исключение", e)
        }
    }
}
