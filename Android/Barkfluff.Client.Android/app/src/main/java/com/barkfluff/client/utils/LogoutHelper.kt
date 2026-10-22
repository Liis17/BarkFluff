package com.barkfluff.client.utils

import android.content.Context
import android.content.Intent
import android.util.Log
import android.widget.Toast
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.LoginActivity
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.send.OutgoingMessageQueue
import com.barkfluff.client.repository.PrivateChatRepository
import com.barkfluff.client.repository.SecretChatRepository
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withContext

/**
 * Хелпер для полного разлогина:
 * 1. Остановка realtime-стримов (иначе они продолжат ретраиться с 401 после очистки токенов)
 * 2. Серверный разлогин через Identity (удаляет refresh-токен на сервере)
 * 3. Удаление FCM-токена Firebase на устройстве
 * 4. Очистка данных аккаунта в SharedPreferences
 * 5. Очистка кешей (медиафайлы, аватары, стикеры)
 * 6. Переход на LoginActivity
 */
object LogoutHelper {

    private const val TAG = "LogoutHelper"

    /** Used by every credential-clearing exit, including rejected refresh on Splash. */
    suspend fun clearCredentials(context: Context, prekeyManager: PrekeyManager): Boolean {
        if (!prepareCryptoExit(context, prekeyManager)) return false
        val app = context.applicationContext as BarkFluffApplication
        app.secretChatRepository.awaitHistoryIdle()
        app.chatCacheRepository.clearAll()
        withContext(Dispatchers.IO) { GlobalParam(context).clearUserData() }
        return true
    }

    private suspend fun prepareCryptoExit(context: Context, manager: PrekeyManager): Boolean {
        var scope: com.barkfluff.client.crypto.CryptoStoreScope? = null
        try {
            withContext(Dispatchers.IO) {
                scope = manager.invalidateForLogout()
                manager.reset(scope)
            }
        } catch (e: kotlinx.coroutines.CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.e(TAG, "Crypto cleanup failed; retry is required", e)
            val retryRecorded = try {
                withContext(Dispatchers.IO) { manager.hasPendingCleanup(scope) }
            } catch (journalError: Exception) {
                if (journalError is kotlinx.coroutines.CancellationException) throw journalError
                Log.e(TAG, "Could not read crypto cleanup journal", journalError)
                false
            }
            Toast.makeText(context, context.getString(R.string.settings_error_detail, e.message.orEmpty()), Toast.LENGTH_LONG).show()
            // Retain credentials if even the exact-scope cleanup journal could not be saved.
            if (!retryRecorded) return false
        }
        return true
    }

    suspend fun performFullLogout(context: Context, authGateway: AuthGateway) {
        val app = context.applicationContext as BarkFluffApplication
        performFullLogout(context, authGateway, app.realtimeService, app.callEventsService,
            app.outgoingMessageQueue, app.chatCacheRepository, app.privateChatRepository,
            app.prekeyManager, app.secretChatRepository)
    }

    /** Gateway-only entry point used by migrated screens and tests. */
    suspend fun performFullLogout(
        context: Context,
        authGateway: AuthGateway,
        realtimeService: RealtimeService,
        callEventsService: CallEventsService,
        outgoingMessageQueue: OutgoingMessageQueue,
        chatCacheRepository: ChatCacheRepository,
        privateChatRepository: PrivateChatRepository,
        prekeyManager: PrekeyManager,
        secretChatRepository: SecretChatRepository,
    ) {
        performFullLogoutInternal(
            context = context,
            serverLogout = { authGateway.logout() },
            realtimeShutdown = realtimeService::shutdown,
            callEventsShutdown = callEventsService::shutdown,
            cancelOutgoing = outgoingMessageQueue::cancelAllForCurrentScope,
            clearChatCache = {
                secretChatRepository.awaitHistoryIdle()
                chatCacheRepository.clearAll()
            },
            forgetPrivateChats = privateChatRepository::forgetAll,
            prekeyManager = prekeyManager,
        )
    }

    private suspend fun performFullLogoutInternal(
        context: Context,
        serverLogout: suspend () -> Result<Unit>,
        realtimeShutdown: () -> Unit,
        callEventsShutdown: () -> Unit,
        cancelOutgoing: suspend () -> Unit,
        clearChatCache: suspend () -> Unit,
        forgetPrivateChats: () -> Unit,
        prekeyManager: PrekeyManager,
    ) {
        // 1. Остановка realtime-стримов — первым делом, чтобы они не писали в кеши
        //    во время очистки и не ушли в бесконечный retry после сброса токенов
        realtimeShutdown()
        callEventsShutdown()
        // Invalidate immediately, before any network await. Clear only after the durable
        // cleanup record exists; the captured scope survives credential/device-id removal.
        if (!prepareCryptoExit(context, prekeyManager)) return
        // WorkManager inputs contain no payload; cancel jobs before removing the scoped DB
        // rows and no-backup media so an old account can never resume after a switch.
        cancelOutgoing()
        Log.i(TAG, "Realtime-стримы остановлены")

        // 2. Очистка кешей приложения — до очистки настроек, пока контекст ещё валиден
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
            clearChatCache()
            Log.i(TAG, "Кеш чатов очищен")
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка очистки кеша чатов", e)
        }


        // 3. Удаление FCM-токена Firebase
        try {
            withContext(Dispatchers.IO) {
                FirebaseMessaging.getInstance().deleteToken().await()
            }
            Log.i(TAG, "Firebase FCM токен удалён")
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка удаления Firebase токена", e)
        }

        // 4. Серверный разлогин выполняется до удаления локального токена.
        try {
            val result = serverLogout()
            if (result.isSuccess) {
                Log.i(TAG, "Серверный разлогин выполнен успешно")
            } else {
                Log.w(TAG, "Серверный разлогин завершился с ошибкой: ${result.exceptionOrNull()?.message}")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Исключение при серверном разлогине", e)
        }

        // 5. Очистка всех настроек аккаунта (оставляем только адреса сервера).
        //    device_id удаляется — при следующем входе будет создан новый.
        val globalParam = GlobalParam(context)
        withContext(Dispatchers.IO) {
            forgetPrivateChats()
            globalParam.clearUserData()
        }
        Log.i(TAG, "Настройки аккаунта очищены (device_id сброшен)")

        // 6. Переход на экран входа.
        val intent = Intent(context, LoginActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
        }
        context.startActivity(intent)
    }
}
