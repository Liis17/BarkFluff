package com.barkfluff.client.views

import android.content.Context
import android.util.AttributeSet
import androidx.appcompat.widget.AppCompatImageView

/**
 * ImageView с фиксированным соотношением сторон ширина:высота = 2:3.
 * Используется для превью фонов чата в сетке (вертикальный портрет).
 */
class AspectRatioImageView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : AppCompatImageView(context, attrs, defStyleAttr) {

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        super.onMeasure(widthMeasureSpec, heightMeasureSpec)
        val width = measuredWidth
        // Соотношение 2:3 → высота = ширина * 3 / 2
        val height = (width * 3f / 2f).toInt()
        setMeasuredDimension(width, height)
    }
}
