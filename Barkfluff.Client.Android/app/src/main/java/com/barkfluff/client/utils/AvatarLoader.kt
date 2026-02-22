package com.barkfluff.client.utils

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.View
import android.widget.ImageView
import android.widget.TextView
import coil.ImageLoader
import coil.load
import coil.request.ImageRequest
import coil.transform.CircleCropTransformation
import com.barkfluff.client.R
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
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
                                .maxSizePercent(0.02)
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
     *
     * @param imageView ImageView для аватара
     * @param placeholderView TextView для инициалов (скрывается если есть аватар)
     * @param avatarUrl URL аватара (null = показать плейсхолдер)
     * @param displayName Имя для генерации инициалов
     * @param userId ID для стабильного выбора цвета плейсхолдера
     */
    fun load(
        imageView: ImageView,
        placeholderView: TextView,
        avatarUrl: String?,
        displayName: String,
        userId: Long = 0
    ) {
        if (!avatarUrl.isNullOrBlank()) {
            // Есть URL - загружаем через Coil с кастомным OkHttpClient
            imageView.visibility = View.VISIBLE
            placeholderView.visibility = View.GONE

            val request = ImageRequest.Builder(imageView.context)
                .data(avatarUrl)
                .crossfade(200)
                .transformations(CircleCropTransformation())
                .listener(
                    onError = { request, result ->
                        android.util.Log.e("AvatarLoader", "load: onError url=$avatarUrl, error=${result.throwable}")
                        // Ошибка загрузки — показываем плейсхолдер
                        imageView.visibility = View.GONE
                        showPlaceholder(placeholderView, displayName, userId)
                    },
                    onSuccess = { request, result ->
                        android.util.Log.d("AvatarLoader", "load: onSuccess url=$avatarUrl")
                    }
                )
                .build()

            getImageLoader(imageView.context).enqueue(request)
        } else {
            // Нет URL — показываем плейсхолдер с инициалами
            imageView.visibility = View.GONE
            showPlaceholder(placeholderView, displayName, userId)
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
     * Получает URL через callback и использует fileId как ключ кэша.
     * Если fileId начинается с http/https, загружает напрямую по URL.
     * @param imageView ImageView для аватара
     * @param placeholderView TextView для инициалов
     * @param fileId Идентификатор файла аватара (или URL)
     * @param displayName Имя для генерации инициалов
     * @param userId ID для стабильного выбора цвета плейсхолдера
     * @param getUrlCallback Callback для получения URL по fileId (возвращает URL или null при ошибке)
     */
    fun loadByFileId(
        imageView: ImageView,
        placeholderView: TextView,
        fileId: String?,
        displayName: String,
        userId: Long = 0,
        getUrlCallback: suspend () -> String?
    ) {
        if (fileId.isNullOrBlank()) {
            imageView.visibility = View.GONE
            showPlaceholder(placeholderView, displayName, userId)
            return
        }

        // Если fileId это URL (начинается с http/https), загружаем напрямую
        if (fileId.startsWith("http://") || fileId.startsWith("https://")) {
            android.util.Log.d("AvatarLoader", "loadByFileId: Loading directly from URL=$fileId")
            MainScope().launch {
                withContext(Dispatchers.Main) {
                    showPlaceholder(placeholderView, displayName, userId)
                    imageView.visibility = View.GONE
                }

                withContext(Dispatchers.Main) {
                    val request = ImageRequest.Builder(imageView.context)
                        .data(fileId)
                        .memoryCacheKey(fileId)
                        .diskCacheKey(fileId)
                        .crossfade(200)
                        .transformations(CircleCropTransformation())
                        .listener(
                            onError = { request, result ->
                                android.util.Log.e("AvatarLoader", "loadByFileId: onError for URL=$fileId, error=${result.throwable}")
                                imageView.visibility = View.GONE
                                placeholderView.visibility = View.VISIBLE
                            },
                            onSuccess = { request, result ->
                                android.util.Log.d("AvatarLoader", "loadByFileId: onSuccess for URL=$fileId")
                                imageView.visibility = View.VISIBLE
                                placeholderView.visibility = View.GONE
                            }
                        )
                        .build()
                    getImageLoader(imageView.context).enqueue(request)
                }
            }
            return
        }

        // Запускаем загрузку в фоне для fileId
        MainScope().launch {
            // Показываем плейсхолдер пока загружаем
            withContext(Dispatchers.Main) {
                showPlaceholder(placeholderView, displayName, userId)
                imageView.visibility = View.GONE
            }

            val url = getUrlCallback()
            android.util.Log.d("AvatarLoader", "loadByFileId: fileId=$fileId, url=$url")

            if (url.isNullOrBlank()) {
                // Ошибка получения URL - показываем плейсхолдер
                android.util.Log.e("AvatarLoader", "loadByFileId: Failed to get URL for fileId=$fileId")
                withContext(Dispatchers.Main) {
                    imageView.visibility = View.GONE
                    placeholderView.visibility = View.VISIBLE
                }
                return@launch
            }

            // Загружаем через Coil с fileId как ключом кэша
            withContext(Dispatchers.Main) {
                android.util.Log.d("AvatarLoader", "loadByFileId: Loading image from URL=$url")
                
                val imageLoader = getImageLoader(imageView.context)
                
                val request = ImageRequest.Builder(imageView.context)
                    .data(url)
                    .memoryCacheKey(fileId) // Ключ для memory cache
                    .diskCacheKey(fileId)   // Ключ для disk cache
                    .crossfade(200)
                    .transformations(CircleCropTransformation()) // Возвращаем круглые аватарки
                    .listener(
                        onError = { request, result ->
                            android.util.Log.e("AvatarLoader", "loadByFileId: onError for fileId=$fileId, url=$url, error=${result.throwable}")
                            imageView.visibility = View.GONE
                            placeholderView.visibility = View.VISIBLE
                        },
                        onSuccess = { request, result ->
                            val drawable = result.drawable
                            android.util.Log.d("AvatarLoader", "loadByFileId: onSuccess for fileId=$fileId, hasDrawable=${drawable != null}, intrinsicWidth=${drawable?.intrinsicWidth}, intrinsicHeight=${drawable?.intrinsicHeight}")
                            
                            if (drawable != null) {
                                imageView.setImageDrawable(drawable)
                                imageView.visibility = View.VISIBLE
                                placeholderView.visibility = View.GONE
                                android.util.Log.d("AvatarLoader", "loadByFileId: Set drawable, imageView.visibility=${imageView.visibility}")
                            } else {
                                imageView.visibility = View.GONE
                                placeholderView.visibility = View.VISIBLE
                            }
                        }
                    )
                    .build()
                
                imageLoader.enqueue(request)
            }
        }
    }

    private fun showPlaceholder(placeholderView: TextView, displayName: String, userId: Long) {
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
