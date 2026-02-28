package com.barkfluff.client.utils

import android.widget.ImageView
import coil.memory.MemoryCache
import coil.request.ImageRequest
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
     * Использует многоуровневый кэш: memory -> disk -> URL cache -> gRPC.
     * Без CircleCropTransformation (для превью вложений и полноэкранного просмотра).
     */
    fun loadByFileId(
        imageView: ImageView,
        fileId: String,
        getUrlCallback: suspend () -> String?,
        onSuccess: (() -> Unit)? = null,
        onError: (() -> Unit)? = null
    ) {
        val context = imageView.context
        val imageLoader = AvatarLoader.getImageLoader(context)

        // 1. Memory cache
        val cached = imageLoader.memoryCache?.get(MemoryCache.Key(fileId))
        if (cached != null) {
            imageView.setImageBitmap(cached.bitmap)
            onSuccess?.invoke()
            return
        }

        // 2. Disk cache
        val diskSnapshot = imageLoader.diskCache?.openSnapshot(fileId)
        if (diskSnapshot != null) {
            val diskFile = diskSnapshot.data.toFile()
            diskSnapshot.close()
            val request = ImageRequest.Builder(context)
                .data(diskFile)
                .target(imageView)
                .memoryCacheKey(fileId)
                .crossfade(200)
                .listener(
                    onSuccess = { _, _ -> onSuccess?.invoke() },
                    onError = { _, _ -> onError?.invoke() }
                )
                .build()
            imageLoader.enqueue(request)
            return
        }

        // 3. URL cache
        val cachedUrl = AvatarLoader.urlCache[fileId]
        if (cachedUrl != null) {
            loadFromUrl(imageView, cachedUrl, fileId, onSuccess, onError)
            return
        }

        // 4. Fetch URL via gRPC
        MainScope().launch {
            val url = withContext(Dispatchers.IO) { getUrlCallback() }
            if (url.isNullOrBlank()) {
                withContext(Dispatchers.Main) { onError?.invoke() }
                return@launch
            }
            AvatarLoader.urlCache[fileId] = url
            withContext(Dispatchers.Main) {
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
            .target(imageView)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .crossfade(200)
            .listener(
                onSuccess = { _, _ -> onSuccess?.invoke() },
                onError = { _, _ -> onError?.invoke() }
            )
            .build()
        imageLoader.enqueue(request)
    }
}
