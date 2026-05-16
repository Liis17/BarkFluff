package com.barkfluff.client.widget

import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject

/**
 * Persists widget configs (name + до 3 chatId) в SharedPreferences("barkfluff_widgets").
 * Ключ: widget_<appWidgetId> → JSON-объект.
 */
object WidgetRepository {

    private const val PREFS_NAME = "barkfluff_widgets"
    private const val KEY_PREFIX = "widget_"
    private const val JSON_NAME = "name"
    private const val JSON_CHAT_IDS = "chatIds"

    private fun prefs(context: Context): SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun getConfig(context: Context, appWidgetId: Int): WidgetConfig? {
        val raw = prefs(context).getString(KEY_PREFIX + appWidgetId, null) ?: return null
        return runCatching {
            val obj = JSONObject(raw)
            val name = obj.optString(JSON_NAME, "")
            val arr = obj.optJSONArray(JSON_CHAT_IDS) ?: JSONArray()
            val ids = ArrayList<String>(arr.length())
            for (i in 0 until arr.length()) {
                ids.add(arr.getString(i))
            }
            WidgetConfig(name, ids)
        }.getOrNull()
    }

    fun saveConfig(context: Context, appWidgetId: Int, config: WidgetConfig) {
        val obj = JSONObject().apply {
            put(JSON_NAME, config.name)
            put(JSON_CHAT_IDS, JSONArray(config.chatIds))
        }
        prefs(context).edit().putString(KEY_PREFIX + appWidgetId, obj.toString()).apply()
    }

    fun deleteConfig(context: Context, appWidgetId: Int) {
        prefs(context).edit().remove(KEY_PREFIX + appWidgetId).apply()
    }

    /**
     * Все сохранённые конфиги. Возвращает пары (appWidgetId, WidgetConfig).
     */
    fun listAllConfigs(context: Context): List<Pair<Int, WidgetConfig>> {
        val result = ArrayList<Pair<Int, WidgetConfig>>()
        for ((key, _) in prefs(context).all) {
            if (!key.startsWith(KEY_PREFIX)) continue
            val id = key.removePrefix(KEY_PREFIX).toIntOrNull() ?: continue
            val cfg = getConfig(context, id) ?: continue
            result.add(id to cfg)
        }
        return result.sortedBy { it.first }
    }

    /**
     * appWidgetId-ы виджетов, содержащих этот chatId в подборке.
     */
    fun findAppWidgetIdsForChat(context: Context, chatId: String): List<Int> =
        listAllConfigs(context).filter { chatId in it.second.chatIds }.map { it.first }

    /**
     * appWidgetId-ы реально размещённых на экране виджетов нашего провайдера.
     */
    fun placedAppWidgetIds(context: Context): IntArray {
        val mgr = AppWidgetManager.getInstance(context)
        val comp = ComponentName(context.packageName, PinnedChatsWidgetProvider::class.java.name)
        return mgr.getAppWidgetIds(comp) ?: IntArray(0)
    }
}
