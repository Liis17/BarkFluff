package com.barkfluff.client.widget

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.content.Intent
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class PinnedChatsWidgetProvider : AppWidgetProvider() {

    companion object {
        private const val TAG = "PinnedChatsWidget"
        const val ACTION_REFRESH = "com.barkfluff.client.widget.ACTION_REFRESH"
        const val EXTRA_APPWIDGET_ID = "appWidgetId"
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onUpdate(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetIds: IntArray
    ) {
        for (id in appWidgetIds) {
            scope.launch {
                WidgetUpdater.refreshWidget(context, id)
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
