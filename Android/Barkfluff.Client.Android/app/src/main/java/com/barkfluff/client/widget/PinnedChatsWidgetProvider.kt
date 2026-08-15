package com.barkfluff.client.widget

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.content.Intent
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout

class PinnedChatsWidgetProvider : AppWidgetProvider() {

    companion object {
        private const val TAG = "PinnedChatsWidget"
        const val ACTION_REFRESH = "com.barkfluff.client.widget.ACTION_REFRESH"
        const val EXTRA_APPWIDGET_ID = "appWidgetId"

        /**
         * Общий бюджет на onUpdate: виджеты обновляются последовательно (под мьютексом
         * в WidgetUpdater), поэтому бюджета одного виджета на весь цикл не хватает.
         * Держим внутри ~10-секундного окна goAsync().
         */
        private const val ON_UPDATE_BUDGET_MS = 9_000L
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onUpdate(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetIds: IntArray
    ) {
        // Без goAsync() процесс может быть убит сразу после возврата из onUpdate,
        // до того как корутина успеет отрисовать виджет.
        val pendingResult = goAsync()
        scope.launch {
            try {
                withTimeout(ON_UPDATE_BUDGET_MS) {
                    for (id in appWidgetIds) {
                        WidgetUpdater.refreshWidget(context, id)
                    }
                }
            } catch (e: TimeoutCancellationException) {
                Log.w(TAG, "onUpdate budget exceeded for ${appWidgetIds.size} widget(s)")
            } catch (e: Exception) {
                Log.e(TAG, "Failed to update widgets", e)
            } finally {
                pendingResult.finish()
            }
        }
    }

    override fun onDeleted(context: Context, appWidgetIds: IntArray) {
        for (id in appWidgetIds) {
            WidgetRepository.deleteConfig(context, id)
            Log.i(TAG, "Widget deleted: id=$id")
        }
        super.onDeleted(context, appWidgetIds)
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        if (intent.action == ACTION_REFRESH) {
            val id = intent.getIntExtra(EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID)
            if (id != AppWidgetManager.INVALID_APPWIDGET_ID) {
                val pendingResult = goAsync()
                scope.launch {
                    try {
                        WidgetUpdater.refreshWidget(context, id)
                    } finally {
                        pendingResult.finish()
                    }
                }
            }
        }
    }
}
