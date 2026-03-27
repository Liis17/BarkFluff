package com.barkfluff.client.utils

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import com.barkfluff.client.adapter.StickerPanelItem
import org.json.JSONArray
import org.json.JSONObject

object StickerCache {

    private const val TAG = "StickerCache"
    private const val PREFS_NAME = "sticker_cache"
    private const val KEY_DATA = "panel_data"

    private lateinit var prefs: SharedPreferences

    fun init(context: Context) {
        prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
    }

    fun savePanelData(items: List<StickerPanelItem>) {
        if (!::prefs.isInitialized) return
        try {
            val arr = JSONArray()
            for (item in items) {
                when (item) {
                    is StickerPanelItem.PackHeader -> {
                        arr.put(JSONObject().apply {
                            put("type", "header")
                            put("packId", item.packId)
                            put("packName", item.packName)
                            put("stickerCount", item.stickerCount)
                            put("coverStickerId", item.coverStickerId)
                        })
                    }
                    is StickerPanelItem.Sticker -> {
                        val s = item.stickerInfo
                        arr.put(JSONObject().apply {
                            put("type", "sticker")
                            put("packId", item.packId)
                            put("id", s.id)
                            put("stickerPackId", s.stickerPackId)
                            put("fileId", s.fileId)
                            put("previewFileId", s.previewFileId)
                            put("fileUrl", s.fileUrl)
                            put("previewUrl", s.previewUrl)
                            put("emoji", s.emoji)
                        })
                    }
                    else -> {} // Loading/Empty не кешируем
                }
            }
            prefs.edit().putString(KEY_DATA, arr.toString()).apply()
            Log.d(TAG, "Saved ${items.size} items to cache")
        } catch (e: Exception) {
            Log.e(TAG, "Error saving sticker cache", e)
        }
    }

    fun loadPanelData(): List<StickerPanelItem>? {
        if (!::prefs.isInitialized) return null
        try {
            val json = prefs.getString(KEY_DATA, null) ?: return null
            val arr = JSONArray(json)
            val items = mutableListOf<StickerPanelItem>()

            for (i in 0 until arr.length()) {
                val obj = arr.getJSONObject(i)
                when (obj.getString("type")) {
                    "header" -> {
                        items.add(StickerPanelItem.PackHeader(
                            packId = obj.getString("packId"),
                            packName = obj.getString("packName"),
                            stickerCount = obj.getInt("stickerCount"),
                            coverStickerId = obj.optString("coverStickerId", "")
                        ))
                    }
                    "sticker" -> {
                        val stickerInfo = barkfluff.files.FilesApiOuterClass.StickerInfo.newBuilder()
                            .setId(obj.getString("id"))
                            .setStickerPackId(obj.optString("stickerPackId", ""))
                            .setFileId(obj.optString("fileId", ""))
                            .setPreviewFileId(obj.optString("previewFileId", ""))
                            .setFileUrl(obj.optString("fileUrl", ""))
                            .setPreviewUrl(obj.optString("previewUrl", ""))
                            .setEmoji(obj.optString("emoji", ""))
                            .build()
                        items.add(StickerPanelItem.Sticker(stickerInfo, obj.getString("packId")))
                    }
                }
            }

            if (items.isEmpty()) return null
            Log.d(TAG, "Loaded ${items.size} items from cache")
            return items
        } catch (e: Exception) {
            Log.e(TAG, "Error loading sticker cache", e)
            return null
        }
    }

    fun clear() {
        if (!::prefs.isInitialized) return
        prefs.edit().remove(KEY_DATA).apply()
    }
}
