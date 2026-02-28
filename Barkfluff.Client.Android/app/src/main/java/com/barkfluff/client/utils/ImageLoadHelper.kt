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
     */
    fun loadByFileId(
        imageView: ImageView,
        fileId: String,
        getUrlCallback: suspend () -> String?,
        onSuccess: (() -> Unit)? = null,
        onError: (() -> Unit)? = null
    ) {
        // Привязываем fileId к ImageView для защиты от race condition при recycling
        imageView.tag = fileId

        // 1. URL cache — Coil сам проверит свой memory/disk cache по memoryCacheKey
        val cachedUrl = AvatarLoader.urlCache[fileId]
        if (cachedUrl != null) {
            loadFromUrl(imageView, cachedUrl, fileId, onSuccess, onError)
            return
        }

        // 2. Fetch URL via callback (gRPC или preview_url)
        MainScope().launch {
            val url = withContext(Dispatchers.IO) { getUrlCallback() }
            if (url.isNullOrBlank()) {
                withContext(Dispatchers.Main) { if (imageView.tag == fileId) onError?.invoke() }
                return@launch
            }
            AvatarLoader.urlCache[fileId] = url
            withContext(Dispatchers.Main) {
                if (imageView.tag != fileId) return@withContext // View recycled
                loadFromUrl(imageView, url, fileId, onSuccess, onError)
            }
        }
    }

    private fun loadFromUrl(
        imageView: ImageView,
        url: String,
        cacheKey: String,
        onSuccess: (() -> Unit)?,
        onError: (() -> Unit)?
    ) {
        val imageLoader = AvatarLoader.getImageLoader(imageView.context)
        val request = ImageRequest.Builder(imageView.context)
            .data(url)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .size(Size.ORIGINAL)
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
            .build()
        imageLoader.enqueue(request)
    }
}
