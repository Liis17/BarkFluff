package com.barkfluff.client.utils

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.View
import android.widget.ImageView
import android.widget.TextView
import coil.ImageLoader
import coil.load
import coil.memory.MemoryCache
import coil.request.ImageRequest
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

    // Кэш URL-ов: fileId -> download URL (чтобы не делать gRPC запрос каждый раз)
    private val urlCache = ConcurrentHashMap<String, String>()

    // ImageLoader с кастомным OkHttpClient (ленивая инициализация)
    private var imageLoaderInstance: ImageLoader? = null

    private fun getImageLoader(context: android.content.Context): ImageLoader {
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
                                .maxSizePercent(0.05)
                                .build()
                        }
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
        if (!avatarUrl.isNullOrBlank()) {
            imageView.visibility = View.VISIBLE
            placeholderView.visibility = View.GONE

            val request = ImageRequest.Builder(imageView.context)
                .data(avatarUrl)
                .crossfade(200)
                .transformations(CircleCropTransformation())
                .listener(
                    onError = { request, result ->
                        android.util.Log.e("AvatarLoader", "load: onError url=$avatarUrl, error=${result.throwable}")
                        imageView.visibility = View.GONE
                        showPlaceholderInternal(placeholderView, displayName, userId)
                    },
                    onSuccess = { request, result ->
                        android.util.Log.d("AvatarLoader", "load: onSuccess url=$avatarUrl")
                    }
                )
                .build()

            getImageLoader(imageView.context).enqueue(request)
        } else {
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

        val imageLoader = getImageLoader(imageView.context)

        // 1. Проверяем Coil memory cache по fileId
        val cached = imageLoader.memoryCache?.get(MemoryCache.Key(fileId))
        if (cached != null) {
            android.util.Log.d("AvatarLoader", "loadByFileId: Memory cache hit for fileId=$fileId")
            imageView.setImageBitmap(cached.bitmap)
            imageView.visibility = View.VISIBLE
            placeholderView.visibility = View.GONE
            return
        }

        // 2. Проверяем Coil disk cache по fileId
        val diskSnapshot = imageLoader.diskCache?.openSnapshot(fileId)
        if (diskSnapshot != null) {
            android.util.Log.d("AvatarLoader", "loadByFileId: Disk cache hit for fileId=$fileId")
            val diskFile = diskSnapshot.data.toFile()
            diskSnapshot.close()

            showPlaceholderInternal(placeholderView, displayName, userId)
            imageView.visibility = View.GONE

            val requestBuilder = ImageRequest.Builder(imageView.context)
                .data(diskFile)
                .target(imageView)
                .memoryCacheKey(fileId)
                .crossfade(200)
                .transformations(CircleCropTransformation())

            if (size > 0) {
                requestBuilder.size(size)
            }

            val request = requestBuilder
                .listener(
                    onSuccess = { _, _ ->
                        imageView.visibility = View.VISIBLE
                        placeholderView.visibility = View.GONE
                    }
                )
                .build()
            imageLoader.enqueue(request)
            return
        }

        // 3. Проверяем URL кэш — если уже получали URL для этого fileId, загружаем по нему
        val cachedUrl = urlCache[fileId]
        if (cachedUrl != null) {
            android.util.Log.d("AvatarLoader", "loadByFileId: URL cache hit for fileId=$fileId, url=$cachedUrl")
            showPlaceholderInternal(placeholderView, displayName, userId)
            imageView.visibility = View.GONE
            loadImageWithCoil(imageView, placeholderView, cachedUrl, fileId, displayName, userId, size)
            return
        }

        // 4. Cache miss — показываем плейсхолдер и запрашиваем URL через gRPC
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

            // Сохраняем URL в кэш
            urlCache[fileId] = url

            withContext(Dispatchers.Main) {
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
        android.util.Log.d("AvatarLoader", "loadImageWithCoil: Loading url=$url, cacheKey=$cacheKey")
        val imageLoader = getImageLoader(imageView.context)

        val requestBuilder = ImageRequest.Builder(imageView.context)
            .data(url)
            .target(imageView)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .crossfade(200)
            .transformations(CircleCropTransformation())

        if (size > 0) {
            requestBuilder.size(size)
        }

        val request = requestBuilder
            .listener(
                onError = { _, result ->
                    android.util.Log.e("AvatarLoader", "loadImageWithCoil: onError cacheKey=$cacheKey, url=$url, error=${result.throwable}")
                    imageView.visibility = View.GONE
                    showPlaceholderInternal(placeholderView, displayName, userId)
                },
                onSuccess = { _, _ ->
                    android.util.Log.d("AvatarLoader", "loadImageWithCoil: onSuccess cacheKey=$cacheKey")
                    imageView.visibility = View.VISIBLE
                    placeholderView.visibility = View.GONE
                }
            )
            .build()

        imageLoader.enqueue(request)
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
        val bg = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(color)
        }
        placeholderView.background = bg

        android.util.Log.d("AvatarLoader", "showPlaceholder: displayName=$displayName, userId=$userId, color=$color, text=${getInitials(displayName)}")
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
        val index = (userId.toInt() and 0x7FFFFFFF) % PLACEHOLDER_COLORS.size
        return PLACEHOLDER_COLORS[index]
    }
}
