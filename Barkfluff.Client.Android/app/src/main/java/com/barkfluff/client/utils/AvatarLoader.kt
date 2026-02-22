package com.barkfluff.client.utils

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.View
import android.widget.ImageView
import android.widget.TextView
import coil.load
import coil.transform.CircleCropTransformation
import com.barkfluff.client.R

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
            // Есть URL - загружаем через Coil
            imageView.visibility = View.VISIBLE
            placeholderView.visibility = View.GONE

            imageView.load(avatarUrl) {
                crossfade(200)
                transformations(CircleCropTransformation())
                listener(
                    onError = { _, _ ->
                        // Ошибка загрузки — показываем плейсхолдер
                        imageView.visibility = View.GONE
                        showPlaceholder(placeholderView, displayName, userId)
                    }
                )
            }
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

    private fun showPlaceholder(placeholderView: TextView, displayName: String, userId: Long) {
        placeholderView.visibility = View.VISIBLE
        placeholderView.text = getInitials(displayName)

        val color = getColorForId(userId)
        val bg = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(color)
        }
        placeholderView.background = bg
        placeholderView.setTextColor(Color.WHITE)
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
