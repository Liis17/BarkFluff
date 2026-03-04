package com.barkfluff.client.utils

import android.widget.ImageView
import coil.request.ImageRequest
import coil.size.Size
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Утилита для загрузки изображений без круглой обрезки.
 * Переиспользует OkHttp-клиент, ImageLoader и URL-кэш из AvatarLoader.
 */
object ImageLoadHelper {

    /**
     * Загружает изображение по fileId в ImageView.
     * Использует URL-кэш и Coil (memory/disk cache внутри Coil).
     * Без CircleCropTransformation (для превью вложений и полноэкранного просмотра).
     * Использует lambda target вместо target(imageView) для защиты от race condition при recycling.
     * @param size Размер изображения (0 = без ограничения, >0 = фиксированный размер)
     */
    fun loadByFileId(
        imageView: ImageView,
        fileId: String,
        getUrlCallback: suspend () -> String?,
        onSuccess: (() -> Unit)? = null,
        onError: (() -> Unit)? = null,
        size: Int = 0
    ) {
        // Привязываем fileId к ImageView для защиты от race condition при recycling
        imageView.tag = fileId

        // 1. Проверяем runtime кэш (ConcurrentHashMap из AvatarLoader)
        val cachedUrl = AvatarLoader.urlCache[fileId]
        if (cachedUrl != null) {
            loadFromUrl(imageView, cachedUrl, fileId, onSuccess, onError, size)
            return
        }

        // 2. Проверяем персистентный кэш
        val persistentUrl = AvatarLoader.getUrlFromCache(fileId)
        if (persistentUrl != null) {
            // Сохраняем в runtime кэш для будущих запросов
            AvatarLoader.urlCache[fileId] = persistentUrl
            loadFromUrl(imageView, persistentUrl, fileId, onSuccess, onError, size)
            return
        }

        // 3. Fetch URL via callback (gRPC или preview_url)
        MainScope().launch {
            val url = withContext(Dispatchers.IO) { getUrlCallback() }
            if (url.isNullOrBlank()) {
                withContext(Dispatchers.Main) { if (imageView.tag == fileId) onError?.invoke() }
                return@launch
            }
            // Сохраняем URL в оба кэша
            AvatarLoader.urlCache[fileId] = url
            AvatarLoader.putUrlInCache(fileId, url)
            
            withContext(Dispatchers.Main) {
                if (imageView.tag != fileId) return@withContext // View recycled
                loadFromUrl(imageView, url, fileId, onSuccess, onError, size)
            }
        }
    }

    private fun loadFromUrl(
        imageView: ImageView,
        url: String,
        cacheKey: String,
        onSuccess: (() -> Unit)?,
        onError: (() -> Unit)?,
        size: Int
    ) {
        val imageLoader = AvatarLoader.getImageLoader(imageView.context)
        val requestBuilder = ImageRequest.Builder(imageView.context)
            .data(url)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .crossfade(200)
            .target(
                onSuccess = { drawable ->
                    if (imageView.tag == cacheKey) {
                        imageView.setImageDrawable(drawable)
                        onSuccess?.invoke()
                    }
                },
                onError = { _ ->
                    if (imageView.tag == cacheKey) {
                        onError?.invoke()
                    }
                }
            )
        
        // Устанавливаем размер: если size > 0, используем его, иначе ORIGINAL
        if (size > 0) {
            requestBuilder.size(size)
        } else {
            requestBuilder.size(Size.ORIGINAL)
        }
        
        val request = requestBuilder.build()
        imageLoader.enqueue(request)
    }
}
