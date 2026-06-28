package com.barkfluff.client.utils

import android.content.Context
import android.util.Log
import java.io.File
import java.io.InputStream

/**
 * Кэш загруженных медиафайлов на диске.
 * Хранит файлы в cacheDir/media_files/ с именем, производным от fileId.
 */
object FileCache {

    private const val TAG = "FileCache"
    private const val CACHE_DIR = "media_files"

    private lateinit var cacheDir: File

    fun init(context: Context) {
        cacheDir = File(context.cacheDir, CACHE_DIR).also { it.mkdirs() }
    }

    fun hasFile(fileId: String): Boolean {
        if (!::cacheDir.isInitialized) return false
        return getFile(fileId)?.exists() == true
    }

    fun getFile(fileId: String): File? {
        if (!::cacheDir.isInitialized) return null
        val file = File(cacheDir, sanitize(fileId))
        return if (file.exists()) file else null
    }

    fun saveFile(fileId: String, data: ByteArray): File {
        val file = File(cacheDir, sanitize(fileId))
        file.writeBytes(data)
        Log.d(TAG, "Saved file: ${file.name} (${data.size} bytes)")
        return file
    }

    fun saveFile(fileId: String, stream: InputStream, totalBytes: Long = -1): File {
        val file = File(cacheDir, sanitize(fileId))
        file.outputStream().use { out -> stream.copyTo(out) }
        Log.d(TAG, "Saved file: ${file.name}")
        return file
    }

    fun deleteFile(fileId: String): Boolean {
        if (!::cacheDir.isInitialized) return false
        val file = File(cacheDir, sanitize(fileId))
        return if (file.exists()) {
            val deleted = file.delete()
            Log.d(TAG, "Deleted file: ${file.name} -> $deleted")
            deleted
        } else false
    }

    private fun sanitize(fileId: String): String =
        fileId.replace(Regex("[^a-zA-Z0-9_\\-]"), "_")
}
