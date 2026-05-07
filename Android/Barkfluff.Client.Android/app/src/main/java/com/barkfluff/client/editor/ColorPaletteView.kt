package com.barkfluff.client.editor

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View

/**
 * Горизонтальная палитра из 10 базовых цветов. Тап по кружку — выбор цвета.
 * Активный цвет подчёркивается белой обводкой.
 */
class ColorPaletteView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    val colors = intArrayOf(
        Color.parseColor("#FFFFFF"),
        Color.parseColor("#000000"),
        Color.parseColor("#F44336"), // red
        Color.parseColor("#FF9800"), // orange
        Color.parseColor("#FFEB3B"), // yellow
        Color.parseColor("#4CAF50"), // green
        Color.parseColor("#2196F3"), // blue
        Color.parseColor("#3F51B5"), // indigo
        Color.parseColor("#9C27B0"), // purple
        Color.parseColor("#795548")  // brown
    )

    private var selectedIndex: Int = 2 // red by default

    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val borderPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        color = Color.WHITE
        strokeWidth = dp(2f)
    }
    private val outlinePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        color = Color.parseColor("#80000000")
        strokeWidth = dp(1f)
    }

    var onColorSelected: ((Int) -> Unit)? = null

    fun selectedColor(): Int = colors[selectedIndex]

    fun selectIndex(index: Int) {
        if (index in colors.indices && index != selectedIndex) {
            selectedIndex = index
            invalidate()
            onColorSelected?.invoke(selectedColor())
        }
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val n = colors.size
        if (n == 0 || width == 0 || height == 0) return
        val padding = dp(8f)
        val avail = (width - padding * 2f).coerceAtLeast(1f)
        val cellW = avail / n
        val cy = height / 2f
        val baseR = (minOf(cellW, height.toFloat()) / 2f) - dp(4f)
        for (i in 0 until n) {
            val cx = padding + cellW * (i + 0.5f)
            paint.color = colors[i]
            val r = if (i == selectedIndex) baseR else baseR * 0.85f
            canvas.drawCircle(cx, cy, r, paint)
            canvas.drawCircle(cx, cy, r, outlinePaint)
            if (i == selectedIndex) {
                canvas.drawCircle(cx, cy, r + dp(3f), borderPaint)
            }
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (event.action != MotionEvent.ACTION_UP) {
            return event.action == MotionEvent.ACTION_DOWN
        }
        val n = colors.size
        if (n == 0 || width == 0) return false
        val padding = dp(8f)
        val avail = (width - padding * 2f).coerceAtLeast(1f)
        val cellW = avail / n
        val rel = event.x - padding
        val idx = (rel / cellW).toInt().coerceIn(0, n - 1)
        selectIndex(idx)
        return true
    }

    private fun dp(v: Float): Float = v * resources.displayMetrics.density
}
