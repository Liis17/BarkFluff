package com.barkfluff.client.view

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.AttributeSet
import android.util.TypedValue
import android.view.MotionEvent
import android.view.View
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.sin

class VoiceWaveformView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    private val density = resources.displayMetrics.density
    private val minBarHeight = 4f * density
    private val preferredHeight = 32f * density
    private val preferredWidth = 190f * density
    private val barGap = 2f * density
    private val cornerRadius = 2f * density
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val rect = RectF()

    private var amplitudes = defaultAmplitudes()
    private var progress = 0f

    var onSeekRequested: ((Float) -> Unit)? = null

    var playedColor: Int = resolveThemeColor(androidx.appcompat.R.attr.colorPrimary, 0xFFFF6B35.toInt())
        set(value) {
            field = value
            invalidate()
        }

    var remainingColor: Int = resolveThemeColor(com.google.android.material.R.attr.colorOutlineVariant, 0xFFCDC6C4.toInt())
        set(value) {
            field = value
            invalidate()
        }

    init {
        isFocusable = true
    }

    fun setColors(played: Int, remaining: Int) {
        playedColor = played
        remainingColor = remaining
    }

    fun setAmplitudes(values: FloatArray) {
        if (values.isEmpty()) return
        amplitudes = FloatArray(values.size) { index -> values[index].coerceIn(0f, 1f) }
        invalidate()
    }

    fun resetAmplitudes() {
        amplitudes = defaultAmplitudes()
        setProgress(0f)
    }

    fun setProgress(value: Float) {
        val next = value.coerceIn(0f, 1f)
        if (progress == next) return
        progress = next
        invalidate()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val desiredWidth = preferredWidth.toInt() + paddingLeft + paddingRight
        val desiredHeight = preferredHeight.toInt() + paddingTop + paddingBottom
        setMeasuredDimension(
            resolveSize(desiredWidth, widthMeasureSpec),
            resolveSize(desiredHeight, heightMeasureSpec)
        )
    }

    override fun onDraw(canvas: Canvas) {
        val contentWidth = width - paddingLeft - paddingRight
        val contentHeight = height - paddingTop - paddingBottom
        if (contentWidth <= 0 || contentHeight <= 0) return

        val count = amplitudes.size
        val gap = minOf(barGap, contentWidth / (count * 3f).coerceAtLeast(1f))
        val barWidth = max(1f, (contentWidth - gap * (count - 1)) / count)
        val centerY = paddingTop + contentHeight / 2f
        val maxBarHeight = contentHeight.toFloat()
        val playedEdge = paddingLeft + contentWidth * progress

        var x = paddingLeft.toFloat()
        for (index in 0 until count) {
            val factor = amplitudes[index].coerceAtLeast(0.08f)
            val barHeight = minBarHeight + (maxBarHeight - minBarHeight) * factor
            paint.color = if (x + barWidth / 2f <= playedEdge) playedColor else remainingColor
            rect.set(x, centerY - barHeight / 2f, x + barWidth, centerY + barHeight / 2f)
            canvas.drawRoundRect(rect, cornerRadius, cornerRadius, paint)
            x += barWidth + gap
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (!isEnabled) return false
        return when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                parent?.requestDisallowInterceptTouchEvent(true)
                isPressed = true
                seekFromTouch(event.x)
                true
            }
            MotionEvent.ACTION_MOVE -> {
                seekFromTouch(event.x)
                true
            }
            MotionEvent.ACTION_UP -> {
                seekFromTouch(event.x)
                isPressed = false
                parent?.requestDisallowInterceptTouchEvent(false)
                performClick()
                true
            }
            MotionEvent.ACTION_CANCEL -> {
                isPressed = false
                parent?.requestDisallowInterceptTouchEvent(false)
                true
            }
            else -> super.onTouchEvent(event)
        }
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    private fun seekFromTouch(x: Float) {
        val contentWidth = width - paddingLeft - paddingRight
        if (contentWidth <= 0) return
        val next = ((x - paddingLeft) / contentWidth).coerceIn(0f, 1f)
        setProgress(next)
        onSeekRequested?.invoke(next)
    }

    private fun resolveThemeColor(attr: Int, fallback: Int): Int {
        val value = TypedValue()
        return if (context.theme.resolveAttribute(attr, value, true)) value.data else fallback
    }

    private fun defaultAmplitudes(): FloatArray = FloatArray(DEFAULT_BAR_COUNT) { index ->
        val wave = abs(sin(index * 0.58f)).toFloat()
        val accent = abs(sin(index * 0.21f + 1.4f)).toFloat()
        (0.18f + wave * 0.58f + accent * 0.24f).coerceIn(0.12f, 1f)
    }

    companion object {
        private const val DEFAULT_BAR_COUNT = 48
    }
}
