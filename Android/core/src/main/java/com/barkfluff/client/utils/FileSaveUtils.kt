package com.barkfluff.client.utils

import android.content.ContentValues
import android.content.Context
import android.graphics.Bitmap
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import java.io.File
import java.io.FileInputStream

object FileSaveUtils {

    private const val APP_FOLDER = "BarkFluff"

    fun getMimeType(fileName: String): String {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "application/octet-stream"
    }

    fun saveImageToGallery(context: Context, source: File, fileName: String): Boolean {
        return saveFileTo(
            context = context,
            source = source,
            fileName = fileName,
            collection = MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
            relativePath = "${Environment.DIRECTORY_PICTURES}/$APP_FOLDER",
            legacyDir = Environment.DIRECTORY_PICTURES
        )
    }

    fun saveToDownloads(context: Context, source: File, fileName: String): Boolean {
        val collection = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            MediaStore.Downloads.EXTERNAL_CONTENT_URI
        } else {
            null
        }
        return saveFileTo(
            context = context,
            source = source,
            fileName = fileName,
            collection = collection,
            relativePath = "${Environment.DIRECTORY_DOWNLOADS}/$APP_FOLDER",
            legacyDir = Environment.DIRECTORY_DOWNLOADS
        )
    }

    fun saveBitmapToGallery(context: Context, bitmap: Bitmap, fileName: String): Boolean {
        return try {
            val resolver = context.contentResolver
            val mime = "image/jpeg"
            val values = ContentValues().apply {
                put(MediaStore.Images.Media.DISPLAY_NAME, fileName)
                put(MediaStore.Images.Media.MIME_TYPE, mime)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    put(MediaStore.Images.Media.RELATIVE_PATH, "${Environment.DIRECTORY_PICTURES}/$APP_FOLDER")
                    put(MediaStore.Images.Media.IS_PENDING, 1)
                }
            }
            val uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values) ?: return false
            resolver.openOutputStream(uri).use { os ->
                if (os == null) return false
                bitmap.compress(Bitmap.CompressFormat.JPEG, 95, os)
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                values.clear()
                values.put(MediaStore.Images.Media.IS_PENDING, 0)
                resolver.update(uri, values, null, null)
            }
            true
        } catch (e: Exception) {
            false
        }
    }

    private fun saveFileTo(
        context: Context,
        source: File,
        fileName: String,
        collection: android.net.Uri?,
        relativePath: String,
        legacyDir: String
    ): Boolean {
        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && collection != null) {
                val resolver = context.contentResolver
                val values = ContentValues().apply {
                    put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
                    put(MediaStore.MediaColumns.MIME_TYPE, getMimeType(fileName))
                    put(MediaStore.MediaColumns.RELATIVE_PATH, relativePath)
                    put(MediaStore.MediaColumns.IS_PENDING, 1)
                }
                val uri = resolver.insert(collection, values) ?: return false
                resolver.openOutputStream(uri).use { os ->
                    if (os == null) return false
                    FileInputStream(source).use { it.copyTo(os) }
                }
                values.clear()
                values.put(MediaStore.MediaColumns.IS_PENDING, 0)
                resolver.update(uri, values, null, null)
                true
            } else {
                val publicDir = Environment.getExternalStoragePublicDirectory(legacyDir)
                val targetDir = File(publicDir, APP_FOLDER).apply { if (!exists()) mkdirs() }
                val target = uniqueFile(targetDir, fileName)
                FileInputStream(source).use { input ->
                    target.outputStream().use { input.copyTo(it) }
                }
                true
            }
        } catch (e: Exception) {
            false
        }
    }

    private fun uniqueFile(dir: File, fileName: String): File {
        var candidate = File(dir, fileName)
        if (!candidate.exists()) return candidate
        val base = fileName.substringBeforeLast('.', fileName)
        val ext = fileName.substringAfterLast('.', "")
        var i = 1
        while (candidate.exists()) {
            val name = if (ext.isEmpty()) "$base ($i)" else "$base ($i).$ext"
            candidate = File(dir, name)
            i++
        }
        return candidate
    }
}
