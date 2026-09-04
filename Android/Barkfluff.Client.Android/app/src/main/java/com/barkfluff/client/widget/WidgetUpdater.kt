package com.barkfluff.client.widget

import android.appwidget.AppWidgetManager
import android.content.Context
import android.util.Log
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.model.ChatSummary
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
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

    @EntryPoint
    @InstallIn(SingletonComponent::class)
    interface Dependencies {
        fun authGateway(): AuthGateway
        fun chatDirectoryGateway(): ChatDirectoryGateway
        fun fileMediaGateway(): FileMediaGateway
    }

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

    private var cachedChats: List<ChatSummary>? = null
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
                    val globalParam = GlobalParam(ctx)
                    val loggedIn = !globalParam.accessToken.isNullOrBlank()
                    val dependencies = EntryPointAccessors.fromApplication(ctx, Dependencies::class.java)

                    val chats = if (loggedIn) {
                        fetchChats(dependencies.authGateway(), dependencies.chatDirectoryGateway())
                    } else emptyList()

                    val views = WidgetRenderer.render(
                        context = ctx,
                        appWidgetId = appWidgetId,
                        config = config,
                        chats = chats,
                        loggedIn = loggedIn,
                        fileMediaGateway = dependencies.fileMediaGateway(),
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
        authGateway: AuthGateway,
        chatDirectoryGateway: ChatDirectoryGateway,
    ): List<ChatSummary> {
        val now = System.currentTimeMillis()
        cachedChats?.let { cached ->
            if (now - cachedAt < CACHE_TTL_MS) return cached
        }

        if (!authGateway.ensureValid()) return emptyList()
        val result = chatDirectoryGateway.chats(offset = 0, size = 100)
        return if (result.isSuccess) {
            val list = result.getOrNull()?.chats.orEmpty()
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
