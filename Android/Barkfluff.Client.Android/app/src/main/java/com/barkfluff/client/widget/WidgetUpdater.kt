package com.barkfluff.client.widget

import android.appwidget.AppWidgetManager
import android.content.Context
import android.util.Log
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import java.util.concurrent.ConcurrentHashMap

/**
 * Единая точка обновления App Widget'ов.
 *
 * - refreshWidget / refreshAllWidgets — синхронные suspend методы для немедленного обновления.
 * - scheduleRefreshForChat — дебаунсит обновления (500мс) при шторме realtime-событий,
 *   ранний return если виджеты не размещены.
 */
object WidgetUpdater {

    private const val TAG = "WidgetUpdater"
    private const val DEBOUNCE_MS = 500L

    /**
     * Бюджет на обновление одного виджета. Вызов может прийти из BroadcastReceiver,
     * где на весь goAsync() отведено ~10 с; сетевые таймауты Coil/OkHttp сами по себе
     * втрое больше этого окна, поэтому ограничиваем сверху здесь.
     */
    const val REFRESH_BUDGET_MS = 8_000L

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val pendingJobs = ConcurrentHashMap<Int, Job>()
    private val refreshMutex = Mutex()

    private var cachedChats: List<GrpcManager.ChatData>? = null
    @Volatile
    private var cachedAt: Long = 0L
    private const val CACHE_TTL_MS = 10_000L

    suspend fun refreshAllWidgets(context: Context) {
        val placed = WidgetRepository.placedAppWidgetIds(context.applicationContext)
        if (placed.isEmpty()) return
        invalidateCache()
        for (id in placed) {
            refreshWidget(context.applicationContext, id)
        }
    }

    suspend fun refreshWidget(context: Context, appWidgetId: Int) {
        // Таймаут внутри withLock: ожидание мьютекса не должно съедать бюджет самого обновления.
        refreshMutex.withLock {
            try {
                withTimeout(REFRESH_BUDGET_MS) {
                    val ctx = context.applicationContext
                    val config = WidgetRepository.getConfig(ctx, appWidgetId) ?: run {
                        Log.v(TAG, "No config for widget $appWidgetId, skipping")
                        return@withTimeout
                    }
                    val app = ctx as? BarkFluffApplication
                    val grpcManager = app?.grpcManager
                    val globalParam = GlobalParam(ctx)
                    val loggedIn = !globalParam.accessToken.isNullOrBlank()

                    val chats = if (loggedIn && grpcManager != null) {
                        fetchChats(ctx, grpcManager, globalParam)
                    } else emptyList()

                    val views = WidgetRenderer.render(
                        context = ctx,
                        appWidgetId = appWidgetId,
                        config = config,
                        chats = chats,
                        loggedIn = loggedIn,
                        grpcManager = grpcManager
                    )
                    AppWidgetManager.getInstance(ctx).updateAppWidget(appWidgetId, views)
                }
            } catch (e: TimeoutCancellationException) {
                // Не ошибка: сеть не уложилась в бюджет, виджет остаётся с прежним содержимым.
                Log.w(TAG, "Refresh budget (${REFRESH_BUDGET_MS}ms) exceeded for widget $appWidgetId")
            } catch (e: CancellationException) {
                // Штатная отмена (например, дебаунс в scheduleRefreshForChat) — пробрасываем.
                throw e
            } catch (e: Exception) {
                Log.e(TAG, "Failed to refresh widget $appWidgetId", e)
            }
        }
    }

    /**
     * Подписан на realtime-события. Дебаунсит вызовы 500мс, делает один refresh всех затронутых
     * виджетов. Если ни один виджет не содержит chatId — ничего не делает.
     */
    fun scheduleRefreshForChat(context: Context, chatId: String) {
        val ctx = context.applicationContext
        val affected = WidgetRepository.findAppWidgetIdsForChat(ctx, chatId)
        if (affected.isEmpty()) return
        invalidateCache()
        for (appWidgetId in affected) {
            val existing = pendingJobs[appWidgetId]
            existing?.cancel()
            val job = scope.launch {
                delay(DEBOUNCE_MS)
                if (!isActive) return@launch
                refreshWidget(ctx, appWidgetId)
                pendingJobs.remove(appWidgetId)
            }
            pendingJobs[appWidgetId] = job
        }
    }

    private suspend fun fetchChats(
        context: Context,
        grpcManager: GrpcManager,
        globalParam: GlobalParam
    ): List<GrpcManager.ChatData> {
        val now = System.currentTimeMillis()
        cachedChats?.let { cached ->
            if (now - cachedAt < CACHE_TTL_MS) return cached
        }

        // Если клиент не создан (например, виджет работает в фоне без активного приложения) —
        // создаём messages-клиент по адресу из GlobalParam.
        if (grpcManager.messagesClient == null) {
            val addr = globalParam.socketMessages
            if (addr.isBlank()) {
                Log.w(TAG, "No messages endpoint configured")
                return emptyList()
            }
            val r = grpcManager.createMessagesClient(addr, context, includeDeviceInfo = true)
            if (r.isFailure) {
                Log.w(TAG, "Failed to create messages client for widget: ${r.exceptionOrNull()?.message}")
                return emptyList()
            }
        }

        grpcManager.ensureTokenValid(context)
        val result = grpcManager.getChats(offset = 0, size = 100)
        return if (result.isSuccess) {
            val list = result.getOrNull().orEmpty()
            cachedChats = list
            cachedAt = now
            list
        } else {
            Log.w(TAG, "getChats failed: ${result.exceptionOrNull()?.message}")
            emptyList()
        }
    }

    private fun invalidateCache() {
        cachedChats = null
        cachedAt = 0L
    }
}
