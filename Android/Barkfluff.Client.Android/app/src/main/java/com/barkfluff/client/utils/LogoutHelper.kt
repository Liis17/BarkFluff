package com.barkfluff.client.utils

import android.content.Context
import android.content.Intent
import android.util.Log
import com.barkfluff.client.LoginActivity
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withContext

/**
 * Хелпер для полного разлогина:
 * 1. Серверный разлогин через Identity (удаляет refresh-токен на сервере)
 * 2. Удаление FCM-токена Firebase на устройстве
 * 3. Очистка данных аккаунта в SharedPreferences
 * 4. Очистка кешей (медиафайлы, аватары, стикеры)
 * 5. Переход на LoginActivity
 */
object LogoutHelper {

    private const val TAG = "LogoutHelper"

    /**
     * Выполняет полный разлогин и переходит на экран входа.
     * @param context контекст Activity или Fragment
     * @param grpcManager экземпляр GrpcManager
     */
    suspend fun performFullLogout(context: Context, grpcManager: GrpcManager) {
        // 1. Очистка кешей приложения — до очистки настроек, пока контекст ещё валиден
        try {
            AvatarLoader.clearAllCaches(context)
            Log.i(TAG, "Кеш аватаров очищен")
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка очистки кеша аватаров", e)
        }

        try {
            StickerCache.clear()
            Log.i(TAG, "Кеш стикеров очищен")
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка очистки кеша стикеров", e)
        }

        try {
            val mediaCacheDir = java.io.File(context.cacheDir, "media_files")
            if (mediaCacheDir.exists()) {
                mediaCacheDir.deleteRecursively()
                mediaCacheDir.mkdirs()
                Log.i(TAG, "Кеш медиафайлов очищен")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка очистки кеша медиафайлов", e)
        }
        try {
            (context.applicationContext as? BarkFluffApplication)?.chatCacheRepository?.clearAll()
            Log.i(TAG, "Кеш чатов очищен")
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка очистки кеша чатов", e)
        }


        // 2. Удаление FCM-токена Firebase
        try {
            withContext(Dispatchers.IO) {
                FirebaseMessaging.getInstance().deleteToken().await()
            }
            Log.i(TAG, "Firebase FCM токен удалён")
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка удаления Firebase токена", e)
        }

        // 3. Очистка всех настроек аккаунта (оставляем только адреса сервера)
        //    device_id удаляется — при следующем входе будет создан новый
        val globalParam = GlobalParam(context)
        (context.applicationContext as? BarkFluffApplication)?.privateChatRepository?.forgetAll()
        globalParam.clearUserData()
        Log.i(TAG, "Настройки аккаунта очищены (device_id сброшен)")

        // 4. Серверный разлогин — выполняется последним, т.к. требует токен из памяти grpcManager
        //    (токен уже недоступен из GlobalParam после шага 3, но grpcManager держит его в канале)
        try {
            val result = grpcManager.logout()
            if (result.isSuccess) {
                Log.i(TAG, "Серверный разлогин выполнен успешно")
            } else {
                Log.w(TAG, "Серверный разлогин завершился с ошибкой: ${result.exceptionOrNull()?.message}")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Исключение при серверном разлогине", e)
        }

        // 5. Переход на экран входа
        val intent = Intent(context, LoginActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
        }
        context.startActivity(intent)
    }
}
