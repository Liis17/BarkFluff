package com.barkfluff.client.calls

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.os.SystemClock
import android.util.AttributeSet
import android.view.View
import kotlin.random.Random

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
    private val random = Random(System.nanoTime())
    private val currentFactors = FloatArray(barCount) { randomFactor() }
    private val targetFactors = FloatArray(barCount) { randomFactor() }
    private val nextTargetAt = LongArray(barCount)

    var barColor: Int = 0xFF43D67C.toInt()
        set(value) { field = value; paint.color = value; invalidate() }

    private var active = false

    fun setActive(value: Boolean) {
        if (active == value) return
        active = value
        visibility = if (value) VISIBLE else GONE
        if (value) {
            resetBars()
            postInvalidateOnAnimation()
        }
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

        val now = SystemClock.uptimeMillis()
        val centerY = height / 2f
        var x = paddingLeft.toFloat()
        for (i in 0 until barCount) {
            if (now >= nextTargetAt[i]) {
                targetFactors[i] = randomFactor()
                nextTargetAt[i] = now + random.nextLong(MIN_TARGET_DELAY_MS, MAX_TARGET_DELAY_MS)
            }

            currentFactors[i] += (targetFactors[i] - currentFactors[i]) * BAR_EASING
            val barHeight = minBar + (maxBar - minBar) * currentFactors[i]
            rect.set(x, centerY - barHeight / 2f, x + barWidth, centerY + barHeight / 2f)
            canvas.drawRoundRect(rect, cornerRadius, cornerRadius, paint)
            x += barWidth + barGap
        }
        postInvalidateOnAnimation()
    }

    private fun resetBars() {
        val now = SystemClock.uptimeMillis()
        for (i in 0 until barCount) {
            currentFactors[i] = randomFactor()
            targetFactors[i] = randomFactor()
            nextTargetAt[i] = now + random.nextLong(MIN_TARGET_DELAY_MS, MAX_TARGET_DELAY_MS)
        }
    }

    private fun randomFactor(): Float = random.nextFloat() * (MAX_RANDOM_FACTOR - MIN_RANDOM_FACTOR) + MIN_RANDOM_FACTOR

    companion object {
        private const val BAR_EASING = 0.32f
        private const val MIN_RANDOM_FACTOR = 0.15f
        private const val MAX_RANDOM_FACTOR = 1f
        private const val MIN_TARGET_DELAY_MS = 90L
        private const val MAX_TARGET_DELAY_MS = 260L
    }
}
