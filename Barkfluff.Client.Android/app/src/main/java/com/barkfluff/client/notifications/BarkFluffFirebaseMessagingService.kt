package com.barkfluff.client.notifications

import android.content.Context
import android.util.Log
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class BarkFluffFirebaseMessagingService : FirebaseMessagingService() {

    companion object {
        private const val TAG = "BarkFluffFCM"
    }

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onNewToken(token: String) {
        Log.d(TAG, "onNewToken: получен новый FCM токен: ${token.take(20)}...")

        // Сохраняем токен локально
        val globalParam = GlobalParam(applicationContext)
        globalParam.firebaseToken = token

        // Пытаемся отправить на сервер если клиент уже инициализирован
        sendTokenToServer(applicationContext, token)
    }

    override fun onMessageReceived(remoteMessage: RemoteMessage) {
        Log.d(TAG, "onMessageReceived: от ${remoteMessage.from}")

        // TODO: Обработка push-уведомлений
        // Пока уведомления обрабатываются через NotificationHelper при получении через реалтайм
    }

    /**
     * Отправляет Firebase токен на сервер.
     * Вызывается при получении нового токена и при запуске приложения.
     */
    fun sendTokenToServer(context: Context, token: String) {
        serviceScope.launch {
            try {
                val app = context.applicationContext as BarkFluffApplication
                val grpcManager = app.grpcManager

                if (grpcManager.usersClient == null) {
                    Log.d(TAG, "sendTokenToServer: usersClient не инициализирован, токен будет отправлен позже")
                    return@launch
                }

                val result = grpcManager.setFirebaseToken(token)
                if (result.isSuccess) {
                    Log.i(TAG, "sendTokenToServer: токен успешно отправлен на сервер")
                } else {
                    Log.e(TAG, "sendTokenToServer: ошибка отправки токена: ${result.exceptionOrNull()?.message}")
                }
            } catch (e: Exception) {
                Log.e(TAG, "sendTokenToServer: исключение", e)
            }
        }
    }
}
