package com.barkfluff.client.utils

import android.util.Log
import java.net.URI

/**
 * Подмена хоста в файловых ссылках на отдельный медиа-адрес ноды.
 *
 * Files выдаёт ссылки на свой основной адрес, который может стоять за CDN с лимитом на
 * размер файла. Если нода объявила отдельный файловый origin (Beacon `files_media_endpoint`),
 * загрузка и скачивание идут через него. Путь ссылки не меняется, поэтому ноды без такого
 * адреса (origin пуст) работают как раньше.
 */
object FileMediaUrl {

    private const val TAG = "FileMediaUrl"

    fun rewrite(url: String, mediaOrigin: String): String {
        if (url.isBlank() || mediaOrigin.isBlank()) {
            return url
        }

        return try {
            val source = URI(url)
            val origin = URI(mediaOrigin)
            if (source.host == null || origin.host == null) {
                return url
            }

            URI(
                origin.scheme ?: source.scheme,
                source.userInfo,
                origin.host,
                origin.port,
                source.path,
                source.query,
                source.fragment
            ).toString()
        } catch (e: Exception) {
            Log.w(TAG, "Не удалось подменить хост в '$url' на '$mediaOrigin'", e)
            url
        }
    }
}
