package com.barkfluff.client.view

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.util.AttributeSet
import android.view.Gravity
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.ImageCache
import com.google.android.material.imageview.ShapeableImageView
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Кастомный контрол для отображения аватаров с поддержкой кэширования по fileid.
 * Принимает на вход fileid и проверяет его в кэше.
 * Если есть в кэше - отображает, если нет - скачивает и сохраняет в кэш.
 * 
 * Сейчас используется просто как обертка над ImageView + TextView для плейсхолдера.
 * Загрузка изображений осуществляется через AvatarLoader.
 */
class AvatarView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : FrameLayout(context, attrs, defStyleAttr) {

    private val imageView: ShapeableImageView
    private val placeholderView: TextView

    private val imageCache: ImageCache = ImageCache.getInstance(context)
    private val avatarLoader: AvatarLoader = AvatarLoader

    private var currentLoadJob: Job? = null

    init {
        // Создаем ImageView для аватара
        imageView = ShapeableImageView(context).apply {
            scaleType = ImageView.ScaleType.CENTER_CROP
            visibility = GONE
        }

        // Создаем TextView для плейсхолдера
        placeholderView = TextView(context).apply {
            gravity = Gravity.CENTER
            textSize = 20f
            setTextColor(Color.WHITE)
            visibility = VISIBLE
        }

        // Добавляем view в layout
        addView(imageView, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
        addView(placeholderView, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
    }

    /**
     * Загрузить аватар по URL (для обратной совместимости).
     */
    fun loadAvatar(
        avatarUrl: String?,
        displayName: String,
        userId: Long = 0
    ) {
        avatarLoader.load(
            imageView = imageView,
            placeholderView = placeholderView,
            avatarUrl = avatarUrl,
            displayName = displayName,
            userId = userId
        )
    }

    override fun onDetachedFromWindow() {
        super.onDetachedFromWindow()
        currentLoadJob?.cancel()
    }

    companion object {
        // Размер аватара по умолчанию (в dp)
        const val DEFAULT_SIZE_DP = 56
    }
}
