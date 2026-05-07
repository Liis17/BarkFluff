package com.barkfluff.client.editor

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View

/**
 * Вертикальная шкала ширины кисти. Слева сверху — самая толстая, снизу — самая тонкая.
 * При перетаскивании пальца отображает превью круга-кисти текущего размера и цвета.
 */
class BrushSizeSliderView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    private val trackPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#80FFFFFF")
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeWidth = dp(2f)
    }
    private val knobPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.RED
        style = Paint.Style.FILL
    }
    private val knobBorderPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        style = Paint.Style.STROKE
        strokeWidth = dp(1.5f)
    }

    private val minBrushPx = dp(4f)
    private val maxBrushPx = dp(60f)

    /** 0..1 — позиция ползунка (0 — внизу = тонкая, 1 — вверху = толстая). */
    private var position: Float = 0.4f

    var brushColor: Int
        get() = knobPaint.color
        set(value) {
            knobPaint.color = value
            invalidate()
        }

    var onWidthChanged: ((Float) -> Unit)? = null

    fun currentWidthPx(): Float =
        minBrushPx + (maxBrushPx - minBrushPx) * position

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cx = width / 2f
        val padding = maxBrushPx / 2f + dp(8f)
        val top = padding
        val bottom = height - padding
        canvas.drawLine(cx, top, cx, bottom, trackPaint)

        val knobY = bottom - position * (bottom - top)
        val r = currentWidthPx() / 2f
        canvas.drawCircle(cx, knobY, r, knobPaint)
        canvas.drawCircle(cx, knobY, r, knobBorderPaint)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.action) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_MOVE -> {
                val padding = maxBrushPx / 2f + dp(8f)
                val top = padding
                val bottom = height - padding
                val span = (bottom - top).coerceAtLeast(1f)
                val y = event.y.coerceIn(top, bottom)
                position = (1f - (y - top) / span).coerceIn(0f, 1f)
                invalidate()
                onWidthChanged?.invoke(currentWidthPx())
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    private fun dp(v: Float): Float = v * resources.displayMetrics.density
}
