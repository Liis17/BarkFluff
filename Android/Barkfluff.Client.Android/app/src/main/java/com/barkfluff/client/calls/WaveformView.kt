package com.barkfluff.client.calls

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.AttributeSet
import android.view.View
import kotlin.math.sin

/**
 * Анимированный индикатор голоса — несколько вертикальных палочек, «пляшущих» как эквалайзер.
 * Рисуется только пока [active] = true. Цвет настраивается через [barColor].
 */
class WaveformView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    private val density = resources.displayMetrics.density
    private val barCount = 5
    private val barWidth = 3f * density
    private val barGap = 3f * density
    private val maxBar = 18f * density
    private val minBar = 4f * density
    private val cornerRadius = 2f * density

    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val rect = RectF()

    var barColor: Int = 0xFF43D67C.toInt()
        set(value) { field = value; paint.color = value; invalidate() }

    private var active = false

    fun setActive(value: Boolean) {
        if (active == value) return
        active = value
        visibility = if (value) VISIBLE else GONE
        if (value) postInvalidateOnAnimation()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = (barCount * barWidth + (barCount - 1) * barGap).toInt() + paddingLeft + paddingRight
        val h = maxBar.toInt() + paddingTop + paddingBottom
        setMeasuredDimension(
            resolveSize(w, widthMeasureSpec),
            resolveSize(h, heightMeasureSpec)
        )
    }

    override fun onDraw(canvas: Canvas) {
        if (!active) return
        val t = System.currentTimeMillis() % 100000L / 1000.0
        val centerY = height / 2f
        var x = paddingLeft.toFloat()
        for (i in 0 until barCount) {
            // Каждая палочка со своей фазой → волнообразное движение
            val phase = i * 0.5
            val factor = (sin(t * 6.0 + phase) * 0.5 + 0.5).toFloat()
            val barHeight = minBar + (maxBar - minBar) * factor
            rect.set(x, centerY - barHeight / 2f, x + barWidth, centerY + barHeight / 2f)
            canvas.drawRoundRect(rect, cornerRadius, cornerRadius, paint)
            x += barWidth + barGap
        }
        postInvalidateOnAnimation()
    }
}
