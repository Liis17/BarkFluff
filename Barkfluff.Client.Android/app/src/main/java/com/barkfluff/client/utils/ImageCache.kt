package com.barkfluff.client.utils

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest

/**
 * Кэш для изображений с поддержкой memory и disk кэширования.
 * Использует fileid как ключ для кэширования.
 */
class ImageCache private constructor(context: Context) {

    // Memory cache (LRU)
    private val memoryCache: LruCache<String, Bitmap>

    // Disk cache directory
    private val diskCacheDir: File

    // Максимальный размер memory cache (1/8 от доступной памяти)
    private val maxMemory = (Runtime.getRuntime().maxMemory() / 1024).toInt()
    private val cacheSize = maxMemory / 8

    init {
        // Инициализация memory cache
        memoryCache = object : LruCache<String, Bitmap>(cacheSize) {
            override fun sizeOf(key: String, bitmap: Bitmap): Int {
                return bitmap.byteCount / 1024
            }
        }

        // Инициализация disk cache
        diskCacheDir = File(context.cacheDir, "bitmap_cache").apply {
            if (!exists()) {
                mkdirs()
            }
        }
    }

    /**
     * Получить bitmap из кэша по fileid.
     * Сначала проверяет memory cache, потом disk cache.
     */
    suspend fun getBitmap(fileId: String): Bitmap? = withContext(Dispatchers.IO) {
        // Проверяем memory cache
        memoryCache[fileId]?.let {
            return@withContext it
        }

        // Проверяем disk cache
        val file = getDiskCacheFile(fileId)
        if (file.exists()) {
            val bitmap = BitmapFactory.decodeFile(file.absolutePath)
            if (bitmap != null) {
                // Добавляем в memory cache
                memoryCache.put(fileId, bitmap)
                return@withContext bitmap
            }
        }

        return@withContext null
    }

    /**
     * Сохранить bitmap в кэш по fileid.
     * Сохраняет и в memory cache, и в disk cache.
     */
    suspend fun putBitmap(fileId: String, bitmap: Bitmap) = withContext(Dispatchers.IO) {
        // Сохраняем в memory cache
        memoryCache.put(fileId, bitmap)

        // Сохраняем в disk cache
        val file = getDiskCacheFile(fileId)
        try {
            FileOutputStream(file).use { outputStream ->
                bitmap.compress(Bitmap.CompressFormat.PNG, 100, outputStream)
                outputStream.flush()
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    /**
     * Получить файл disk cache по fileid.
     */
    private fun getDiskCacheFile(fileId: String): File {
        val fileName = hashFileName(fileId)
        return File(diskCacheDir, fileName)
    }

    /**
     * Хэширует имя файла для безопасного хранения.
     */
    private fun hashFileName(fileName: String): String {
        val bytes = MessageDigest.getInstance("MD5").digest(fileName.toByteArray())
        return bytes.joinToString("") { "%02x".format(it) }
    }

    /**
     * Очистить disk cache.
     */
    suspend fun clearDiskCache() = withContext(Dispatchers.IO) {
        diskCacheDir.deleteRecursively()
        diskCacheDir.mkdirs()
    }

    /**
     * Очистить memory cache.
     */
    fun clearMemoryCache() {
        memoryCache.evictAll()
    }

    /**
     * Очистить весь кэш.
     */
    suspend fun clearAll() = withContext(Dispatchers.IO) {
        clearMemoryCache()
        clearDiskCache()
    }

    companion object {
        @Volatile
        private var instance: ImageCache? = null

        fun getInstance(context: Context): ImageCache {
            return instance ?: synchronized(this) {
                instance ?: ImageCache(context.applicationContext).also {
                    instance = it
                }
            }
        }
    }
}
