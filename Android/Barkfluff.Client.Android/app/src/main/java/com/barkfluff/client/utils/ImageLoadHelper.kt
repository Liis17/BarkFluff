package com.barkfluff.client.utils

import android.widget.ImageView
import android.graphics.drawable.BitmapDrawable
import barkfluff.shared.Shared
import coil.annotation.ExperimentalCoilApi
import coil.decode.ImageDecoderDecoder
import coil.decode.VideoFrameDecoder
import coil.imageLoader
import coil.load
import coil.memory.MemoryCache
import coil.request.SuccessResult
import coil.request.ImageRequest
import coil.size.Size
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Утилита для загрузки изображений без круглой обрезки.
 * Переиспользует OkHttp-клиент, ImageLoader и URL-кэш из AvatarLoader.
 */
object ImageLoadHelper {

    fun loadLocal(imageView: ImageView, attachment: Shared.MessageAttachment, file: File) {
        imageView.tag = "local:${attachment.fileId}"
        imageView.load(file, AvatarLoader.getImageLoader(imageView.context)) {
            memoryCacheKey("attachment:${attachment.fileId}")
            size(maxOf(imageView.width, imageView.height, 256).coerceAtMost(1024))
            when (attachment.type) {
                Shared.MessageAttachmentType.VIDEO -> decoderFactory { result, options, _ -> VideoFrameDecoder(result.source, options) }
                Shared.MessageAttachmentType.GIF -> decoderFactory(ImageDecoderDecoder.Factory())
                else -> Unit
            }
        }
    }

    /** Historical preview caches remain usable without resolving a URL or issuing HTTP. */
    @OptIn(ExperimentalCoilApi::class)
    suspend fun loadCached(imageView: ImageView, attachment: Shared.MessageAttachment): Boolean {
        val tag = "cached:${attachment.fileId}"
        imageView.tag = tag
        val ids = listOf(attachment.fileId, attachment.previewFileId).filter(String::isNotBlank)
        val keys = (ids + "attachment:${attachment.fileId}" + FileMediaUrl.rewrite(imageView.context, attachment.previewUrl) +
            ids.mapNotNull { AvatarLoader.urlCache[it] ?: AvatarLoader.getUrlFromCache(it) }).filter(String::isNotBlank).distinct()
        val loaders = listOf(AvatarLoader.getImageLoader(imageView.context), imageView.context.imageLoader).distinct()
        val drawable = try { withContext(Dispatchers.IO) {
            for (loader in loaders) {
                for (key in keys) {
                    val memory = loader.memoryCache
                    val memoryKey = memory?.keys?.firstOrNull { it.key == key } ?: MemoryCache.Key(key)
                    memory?.get(memoryKey)?.bitmap?.let { return@withContext BitmapDrawable(imageView.resources, it) }
                    val snapshot = loader.diskCache?.openSnapshot(key) ?: continue
                    snapshot.use {
                        val result = loader.execute(ImageRequest.Builder(imageView.context)
                            .data(it.data.toFile())
                            .size(1024)
                            .build())
                        if (result is SuccessResult) return@withContext result.drawable
                    }
                }
            }
            null
        } } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            null
        }
        if (imageView.tag != tag || drawable == null) return false
        imageView.setImageDrawable(drawable)
        return true
    }

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
