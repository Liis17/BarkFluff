package com.barkfluff.client.views

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.util.TypedValue
import android.view.View
import androidx.core.content.res.ResourcesCompat

class ScannerOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null
) : View(context, attrs) {

    private val overlayPaint = Paint().apply {
        color = Color.BLACK
        alpha = 0xCC
    }
    private val clearPaint = Paint().apply {
        xfermode = PorterDuffXfermode(PorterDuff.Mode.CLEAR)
    }
    private val cornerPaint = Paint().apply {
        color = resolveThemeColor(androidx.appcompat.R.attr.colorPrimary, Color.WHITE)
        style = Paint.Style.STROKE
        strokeWidth = dp(3f)
        isAntiAlias = true
        strokeCap = Paint.Cap.ROUND
    }

    private val frameRect = RectF()
    private val cornerLen = dp(24f)
    private val cornerRadius = dp(28f) // shape_corner_extra_large
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

        canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), overlayPaint)
        canvas.drawRoundRect(frameRect, cornerRadius, cornerRadius, clearPaint)

        val l = frameRect.left
        val t = frameRect.top
        val r = frameRect.right
        val b = frameRect.bottom

        canvas.drawLine(l, t + cornerLen, l, t, cornerPaint)
        canvas.drawLine(l, t, l + cornerLen, t, cornerPaint)
        canvas.drawLine(r - cornerLen, t, r, t, cornerPaint)
        canvas.drawLine(r, t, r, t + cornerLen, cornerPaint)
        canvas.drawLine(l, b - cornerLen, l, b, cornerPaint)
        canvas.drawLine(l, b, l + cornerLen, b, cornerPaint)
        canvas.drawLine(r - cornerLen, b, r, b, cornerPaint)
        canvas.drawLine(r, b, r, b - cornerLen, cornerPaint)
    }

    private fun resolveThemeColor(attr: Int, fallback: Int): Int {
        val tv = TypedValue()
        return if (context.theme.resolveAttribute(attr, tv, true)) {
            if (tv.resourceId != 0) ResourcesCompat.getColor(resources, tv.resourceId, context.theme)
            else tv.data
        } else fallback
    }

    private fun dp(value: Float): Float =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics)
}
