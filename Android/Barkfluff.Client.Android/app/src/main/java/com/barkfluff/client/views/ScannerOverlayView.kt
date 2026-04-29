package com.barkfluff.client.views

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.View

class ScannerOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null
) : View(context, attrs) {

    private val overlayPaint = Paint().apply {
        color = Color.parseColor("#99000000")
    }
    private val clearPaint = Paint().apply {
        xfermode = PorterDuffXfermode(PorterDuff.Mode.CLEAR)
    }
    private val cornerPaint = Paint().apply {
        color = Color.WHITE
        style = Paint.Style.STROKE
        strokeWidth = 6f
        isAntiAlias = true
        strokeCap = Paint.Cap.ROUND
    }

    private val frameRect = RectF()
    private val cornerLen = 60f
    private val frameSize get() = width.coerceAtMost(height) * 0.65f

    init {
        setLayerType(LAYER_TYPE_SOFTWARE, null)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        val cx = width / 2f
        val cy = height / 2f
        val half = frameSize / 2f

        frameRect.set(cx - half, cy - half, cx + half, cy + half)

        // Тёмный оверлей на весь экран
        canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), overlayPaint)

        // Вырезаем прозрачный квадрат
        canvas.drawRoundRect(frameRect, 16f, 16f, clearPaint)

        // Угловые уголки
        val l = frameRect.left
        val t = frameRect.top
        val r = frameRect.right
        val b = frameRect.bottom

        // Верхний левый
        canvas.drawLine(l, t + cornerLen, l, t, cornerPaint)
        canvas.drawLine(l, t, l + cornerLen, t, cornerPaint)
        // Верхний правый
        canvas.drawLine(r - cornerLen, t, r, t, cornerPaint)
        canvas.drawLine(r, t, r, t + cornerLen, cornerPaint)
        // Нижний левый
        canvas.drawLine(l, b - cornerLen, l, b, cornerPaint)
        canvas.drawLine(l, b, l + cornerLen, b, cornerPaint)
        // Нижний правый
        canvas.drawLine(r - cornerLen, b, r, b, cornerPaint)
        canvas.drawLine(r, b, r, b - cornerLen, cornerPaint)
    }
}
