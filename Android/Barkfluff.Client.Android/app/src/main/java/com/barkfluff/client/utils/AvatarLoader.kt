package com.barkfluff.client.utils

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.util.Log
import android.view.View
import android.widget.ImageView
import android.widget.TextView
import coil.ImageLoader
import coil.load
import coil.request.ImageRequest
import coil.size.Size
import coil.transform.CircleCropTransformation
import com.barkfluff.client.R
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import java.util.concurrent.ConcurrentHashMap
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

/**
 * Утилита для загрузки и отображения аватаров.
 * Использует Coil для загрузки/кеширования изображений.
 * Показывает инициалы на цветном фоне при отсутствии аватара.
 */
object AvatarLoader {

    // Палитра цветов для плейсхолдеров (Material 3)
    private val PLACEHOLDER_COLORS = intArrayOf(
        0xFFE57373.toInt(), // red
        0xFFFF8A65.toInt(), // deep orange
        0xFFFFB74D.toInt(), // orange
        0xFFFFD54F.toInt(), // amber
        0xFFAED581.toInt(), // light green
        0xFF4DB6AC.toInt(), // teal
        0xFF4FC3F7.toInt(), // light blue
        0xFF7986CB.toInt(), // indigo
        0xFFBA68C8.toInt(), // purple
        0xFFF06292.toInt(), // pink
        0xFF90A4AE.toInt(), // blue grey
        0xFFA1887F.toInt(), // brown
    )

    // OkHttpClient с доверием ко всем SSL сертификатам (для серверов с самоподписанными сертификатами)
    private val okHttpClient by lazy {
        val trustManager = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
            override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
        }

        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf<TrustManager>(trustManager), null)
        }

        OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier { _, _ -> true }
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()
    }

    // Кэш URL-ов: fileId -> download URL (персистентный кэш для предотвращения повторных gRPC запросов)
    // Используем FileUrlCache для персистентности + ConcurrentHashMap для быстрого доступа в runtime
    private var fileUrlCache: FileUrlCache? = null
    
    internal fun initializeCache(context: Context) {
        if (fileUrlCache == null) {
            fileUrlCache = FileUrlCache.getInstance(context)
            MainScope().launch {
                fileUrlCache?.initialize()
            }
        }
    }
    
    internal fun getUrlFromCache(fileId: String): String? {
        return fileUrlCache?.getUrl(fileId)
    }
    
    internal fun putUrlInCache(fileId: String, url: String) {
        MainScope().launch {
            fileUrlCache?.putUrl(fileId, url)
        }
    }

    // Кэш URL-ов в памяти для быстрого доступа (runtime only)
    internal val urlCache = ConcurrentHashMap<String, String>()

    /**
     * Очищает все кеши: memory, disk, URL cache (runtime и персистентный)
     */
    fun clearAllCaches(context: Context) {
        // Очищаем memory cache Coil
        imageLoaderInstance?.memoryCache?.clear()

        // Очищаем disk cache Coil через API
        imageLoaderInstance?.diskCache?.clear()

        // Очищаем runtime URL кеш
        urlCache.clear()

        // Очищаем персистентный URL кеш
        MainScope().launch {
            fileUrlCache?.clear()
        }
    }

    // ImageLoader с кастомным OkHttpClient (ленивая инициализация)
    private var imageLoaderInstance: ImageLoader? = null

    internal fun getImageLoader(context: android.content.Context): ImageLoader {
        if (imageLoaderInstance == null) {
            synchronized(this) {
                if (imageLoaderInstance == null) {
                    imageLoaderInstance = ImageLoader.Builder(context.applicationContext)
                        .okHttpClient(okHttpClient)
                        .memoryCache {
                            coil.memory.MemoryCache.Builder(context.applicationContext)
                                .maxSizePercent(0.25)
                                .build()
                        }
                        .diskCache {
                            coil.disk.DiskCache.Builder()
                                .directory(context.cacheDir.resolve("image_cache"))
                                .maxSizePercent(0.10)
                                .build()
                        }
                        .respectCacheHeaders(false) // Игнорируем HTTP Cache-Control заголовки сервера
                        .build()
                }
            }
        }
        return imageLoaderInstance!!
    }

    /**
     * Загружает аватар в ImageView из URL.
     * При отсутствии URL показывает плейсхолдер с инициалами.
     */
    fun load(
        imageView: ImageView,
        placeholderView: TextView,
        avatarUrl: String?,
        displayName: String,
        userId: Long = 0
    ) {
        Log.d("AvatarLoader", "load: avatarUrl='$avatarUrl', displayName='$displayName', userId=$userId")
        
        if (!avatarUrl.isNullOrBlank()) {
            placeholderView.visibility = View.GONE
            imageView.visibility = View.VISIBLE

            val request = ImageRequest.Builder(imageView.context)
                .data(avatarUrl)
                .crossfade(200)
                .transformations(CircleCropTransformation())
                .target(
                    onSuccess = { drawable ->
                        android.util.Log.d("AvatarLoader", "load: onSuccess url=$avatarUrl")
                        imageView.setImageDrawable(drawable)
                        imageView.visibility = View.VISIBLE
                        placeholderView.visibility = View.GONE
                    },
                    onError = {
                        android.util.Log.e("AvatarLoader", "load: onError url=$avatarUrl")
                        imageView.visibility = View.GONE
                        showPlaceholderInternal(placeholderView, displayName, userId)
                    }
                )
                .build()

            Log.d("AvatarLoader", "load: enqueueing request for url=$avatarUrl")
            getImageLoader(imageView.context).enqueue(request)
        } else {
            Log.d("AvatarLoader", "load: avatarUrl is null or blank, showing placeholder")
            imageView.visibility = View.GONE
            showPlaceholderInternal(placeholderView, displayName, userId)
        }
    }

    /**
     * Загружает аватар только в ImageView (без отдельного placeholder TextView).
     * При ошибке показывает цветной круг с инициалами.
     */
    fun loadIntoImageView(
        imageView: ImageView,
        avatarUrl: String?,
        displayName: String,
        userId: Long = 0
    ) {
        if (!avatarUrl.isNullOrBlank()) {
            imageView.load(avatarUrl) {
                crossfade(200)
                transformations(CircleCropTransformation())
                placeholder(createPlaceholderDrawable(displayName, userId))
                error(createPlaceholderDrawable(displayName, userId))
            }
        } else {
            imageView.setImageDrawable(createPlaceholderDrawable(displayName, userId))
        }
    }

    /**
     * Загружает аватар по fileId с использованием кэша.
     */
    fun loadByFileId(
        imageView: ImageView,
        placeholderView: TextView,
        fileId: String?,
        displayName: String,
        userId: Long = 0,
        getUrlCallback: suspend () -> String?
    ) {
        loadByFileIdInternal(imageView, placeholderView, fileId, displayName, userId, 0, getUrlCallback)
    }

    /**
     * Загружает аватар по fileId с использованием кэша и масштабированием.
     * @param size Размер изображения (0 = без ограничения, >0 = фиксированный размер)
     */
    fun loadByFileId(
        imageView: ImageView,
        placeholderView: TextView,
        fileId: String?,
        displayName: String,
        userId: Long = 0,
        size: Int = 0,
        getUrlCallback: suspend () -> String?
    ) {
        loadByFileIdInternal(imageView, placeholderView, fileId, displayName, userId, size, getUrlCallback)
    }

    private fun loadByFileIdInternal(
        imageView: ImageView,
        placeholderView: TextView,
        fileId: String?,
        displayName: String,
        userId: Long,
        size: Int,
        getUrlCallback: suspend () -> String?
    ) {
        // Привязываем fileId к ImageView для защиты от race condition при recycling
        imageView.tag = fileId

        if (fileId.isNullOrBlank()) {
            imageView.visibility = View.GONE
            showPlaceholderInternal(placeholderView, displayName, userId)
            return
        }

        // Если fileId это URL (начинается с http/https), загружаем напрямую через Coil
        if (fileId.startsWith("http://") || fileId.startsWith("https://")) {
            android.util.Log.d("AvatarLoader", "loadByFileId: Loading directly from URL=$fileId")
            showPlaceholderInternal(placeholderView, displayName, userId)
            imageView.visibility = View.GONE
            loadImageWithCoil(imageView, placeholderView, fileId, fileId, displayName, userId, size)
            return
        }

        // 1. Проверяем runtime кэш (ConcurrentHashMap) — самый быстрый
        val cachedUrl = urlCache[fileId]
        if (cachedUrl != null) {
            android.util.Log.d("AvatarLoader", "loadByFileId: Runtime cache hit for fileId=$fileId")
            showPlaceholderInternal(placeholderView, displayName, userId)
            imageView.visibility = View.GONE
            loadImageWithCoil(imageView, placeholderView, cachedUrl, fileId, displayName, userId, size)
            return
        }

        // 2. Проверяем персистентный кэш (SharedPreferences)
        val persistentUrl = getUrlFromCache(fileId)
        if (persistentUrl != null) {
            android.util.Log.d("AvatarLoader", "loadByFileId: Persistent cache hit for fileId=$fileId")
            // Сохраняем в runtime кэш для будущих запросов
            urlCache[fileId] = persistentUrl
            showPlaceholderInternal(placeholderView, displayName, userId)
            imageView.visibility = View.GONE
            loadImageWithCoil(imageView, placeholderView, persistentUrl, fileId, displayName, userId, size)
            return
        }

        // 3. Cache miss — показываем плейсхолдер и запрашиваем URL через gRPC
        android.util.Log.d("AvatarLoader", "loadByFileId: Cache miss for fileId=$fileId, fetching URL...")
        showPlaceholderInternal(placeholderView, displayName, userId)
        imageView.visibility = View.GONE

        MainScope().launch {
            val url = withContext(Dispatchers.IO) {
                getUrlCallback()
            }
            android.util.Log.d("AvatarLoader", "loadByFileId: fileId=$fileId, url=$url")

            if (url.isNullOrBlank()) {
                android.util.Log.e("AvatarLoader", "loadByFileId: Failed to get URL for fileId=$fileId")
                return@launch
            }

            // Сохраняем URL в оба кэша
            urlCache[fileId] = url
            putUrlInCache(fileId, url)

            withContext(Dispatchers.Main) {
                if (imageView.tag != fileId) return@withContext // View recycled
                loadImageWithCoil(imageView, placeholderView, url, fileId, displayName, userId, size)
            }
        }
    }

    /**
     * Загружает изображение через Coil с указанным URL и fileId как ключом кэша.
     */
    private fun loadImageWithCoil(
        imageView: ImageView,
        placeholderView: TextView,
        url: String,
        cacheKey: String,
        displayName: String,
        userId: Long,
        size: Int
    ) {
        android.util.Log.d("AvatarLoader", "loadImageWithCoil: Loading url=$url, cacheKey=$cacheKey, size=$size")
        val imageLoader = getImageLoader(imageView.context)

        // Сохраняем cacheKey в tag для проверки при recycling
        imageView.tag = cacheKey

        val requestBuilder = ImageRequest.Builder(imageView.context)
            .data(url)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .crossfade(200)
            .transformations(CircleCropTransformation())
            .target(
                onSuccess = { drawable ->
                    if (imageView.tag == cacheKey) {
                        android.util.Log.d("AvatarLoader", "loadImageWithCoil: onSuccess cacheKey=$cacheKey")
                        imageView.setImageDrawable(drawable)
                        imageView.visibility = View.VISIBLE
                        placeholderView.visibility = View.GONE
                    }
                },
                onError = { _ ->
                    if (imageView.tag == cacheKey) {
                        android.util.Log.e("AvatarLoader", "loadImageWithCoil: onError cacheKey=$cacheKey, url=$url")
                        imageView.visibility = View.GONE
                        showPlaceholderInternal(placeholderView, displayName, userId)
                    }
                }
            )

        // Устанавливаем размер: если size > 0, используем его, иначе ORIGINAL
        if (size > 0) {
            requestBuilder.size(size)
        } else {
            requestBuilder.size(Size.ORIGINAL)
        }

        imageLoader.enqueue(requestBuilder.build())
    }

    /**
     * Показывает плейсхолдер с инициалами
     */
    fun showPlaceholder(placeholderView: TextView, displayName: String, userId: Long) {
        showPlaceholderInternal(placeholderView, displayName, userId)
    }

    private fun showPlaceholderInternal(placeholderView: TextView, displayName: String, userId: Long) {
        placeholderView.visibility = View.VISIBLE
        placeholderView.text = getInitials(displayName)
        placeholderView.setTextColor(Color.WHITE)

        val color = getColorForId(userId)
        Log.d("AvatarLoader", "showPlaceholderInternal: displayName=$displayName, userId=$userId, color=0x${Integer.toHexString(color)}, text=${getInitials(displayName)}")
        
        // Создаем oval drawable с нужным цветом
        val bg = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(color)
            // Устанавливаем размер 1x1 чтобы drawable масштабировался правильно
            setSize(1, 1)
        }
        placeholderView.background = bg
    }

    private fun createPlaceholderDrawable(displayName: String, userId: Long): GradientDrawable {
        val color = getColorForId(userId)
        return GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(color)
        }
    }

    fun getInitials(name: String): String {
        if (name.isBlank()) return "?"
        val parts = name.trim().split("\\s+".toRegex())
        return when {
            parts.size >= 2 -> "${parts[0].first().uppercaseChar()}${parts[1].first().uppercaseChar()}"
            parts[0].length >= 2 -> "${parts[0][0].uppercaseChar()}${parts[0][1].lowercaseChar()}"
            else -> parts[0].first().uppercaseChar().toString()
        }
    }

    private fun getColorForId(userId: Long): Int {
        // Используем hashCode для получения цвета, чтобы избежать проблем с большими Long
        val index = (userId.hashCode() and 0x7FFFFFFF) % PLACEHOLDER_COLORS.size
        return PLACEHOLDER_COLORS[index]
    }
}
