package com.barkfluff.client.utils

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.security.MessageDigest

/**
 * Персистентный кэш для URL файлов.
 * Сохраняет mapping fileId -> URL в SharedPreferences.
 * Используется для предотвращения повторных gRPC запросов при каждом запуске приложения.
 */
class FileUrlCache private constructor(context: Context) {

    private val sharedPreferences: SharedPreferences = context.getSharedPreferences(
        "file_url_cache",
        Context.MODE_PRIVATE
    )

    private val cache = mutableMapOf<String, String>()

    /**
     * Инициализация кэша при старте.
     * Загружает все сохранённые URL в память.
     */
    suspend fun initialize() = withContext(Dispatchers.IO) {
        synchronized(this@FileUrlCache) {
            cache.clear()
            val allEntries = sharedPreferences.all
            for ((key, value) in allEntries) {
                if (value is String) {
                    cache[key] = value
                }
            }
            Log.d(TAG, "Initialized cache with ${cache.size} entries")
        }
    }

    /**
     * Получить URL из кэша по fileId.
     */
    fun getUrl(fileId: String): String? {
        synchronized(this@FileUrlCache) {
            val url = cache[hashKey(fileId)]
            if (url != null) {
                Log.d(TAG, "Cache hit for fileId=$fileId")
            } else {
                Log.d(TAG, "Cache miss for fileId=$fileId")
            }
            return url
        }
    }

    /**
     * Сохранить URL в кэш по fileId.
     */
    suspend fun putUrl(fileId: String, url: String) = withContext(Dispatchers.IO) {
        synchronized(this@FileUrlCache) {
            val hashedKey = hashKey(fileId)
            cache[hashedKey] = url
            sharedPreferences.edit().putString(hashedKey, url).apply()
            Log.d(TAG, "Cached URL for fileId=$fileId")
        }
    }

    /**
     * Очистить весь кэш.
     */
    suspend fun clear() = withContext(Dispatchers.IO) {
        synchronized(this@FileUrlCache) {
            cache.clear()
            sharedPreferences.edit().clear().apply()
            Log.d(TAG, "Cleared cache")
        }
    }

    /**
     * Хэширует ключ для безопасного хранения в SharedPreferences.
     */
    private fun hashKey(fileId: String): String {
        val bytes = MessageDigest.getInstance("SHA-256").digest(fileId.toByteArray())
        return bytes.joinToString("") { "%02x".format(it) }
    }

    companion object {
        private const val TAG = "FileUrlCache"

        @Volatile
        private var instance: FileUrlCache? = null

        fun getInstance(context: Context): FileUrlCache {
            return instance ?: synchronized(this) {
                instance ?: FileUrlCache(context.applicationContext).also {
                    instance = it
                }
            }
        }
    }
}
