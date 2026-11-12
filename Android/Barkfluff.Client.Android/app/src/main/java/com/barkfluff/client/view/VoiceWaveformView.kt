package com.barkfluff.client.view

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.AttributeSet
import android.util.TypedValue
import android.view.MotionEvent
import android.os.Bundle
import android.view.accessibility.AccessibilityNodeInfo
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
    private val minBarHeight = 2f * density
    private val preferredHeight = 48f * density
    private val preferredWidth = 190f * density
    private val barWidth = 3f * density
    private val barGap = 2f * density
    private val cornerRadius = barWidth / 2f
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val rect = RectF()

    private var amplitudes = defaultAmplitudes()
    private var progress = 0f

    var onSeekRequested: ((Float) -> Unit)? = null
    /** Перетаскивание пальцем: true — началось, false — закончилось или отменено. */
    var onScrubChanged: ((Boolean) -> Unit)? = null

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

        // Как в Telegram: полоски фиксированной ширины растут от нижнего края,
        // число полосок зависит от ширины, амплитуды прореживаются по максимуму в окне.
        val count = ((contentWidth + barGap) / (barWidth + barGap)).toInt().coerceAtLeast(1)
        val step = (contentWidth - barWidth * count) / max(1, count - 1).toFloat()
        val bottom = (paddingTop + contentHeight).toFloat()
        val playedEdge = paddingLeft + contentWidth * progress
        val source = amplitudes

        var x = paddingLeft.toFloat()
        for (index in 0 until count) {
            val from = index * source.size / count
            val to = max(from + 1, (index + 1) * source.size / count).coerceAtMost(source.size)
            var factor = 0f
            for (sample in from until to) factor = max(factor, source[sample])
            val barHeight = minBarHeight + (contentHeight - minBarHeight) * factor.coerceIn(0f, 1f)
            paint.color = if (x + barWidth / 2f <= playedEdge) playedColor else remainingColor
            rect.set(x, bottom - barHeight, x + barWidth, bottom)
            canvas.drawRoundRect(rect, cornerRadius, cornerRadius, paint)
            x += barWidth + step
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (!isEnabled) return false
        return when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                parent?.requestDisallowInterceptTouchEvent(true)
                isPressed = true
                onScrubChanged?.invoke(true)
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
                onScrubChanged?.invoke(false)
                performClick()
                true
            }
            MotionEvent.ACTION_CANCEL -> {
                isPressed = false
                parent?.requestDisallowInterceptTouchEvent(false)
                onScrubChanged?.invoke(false)
                true
            }
            else -> super.onTouchEvent(event)
        }
    }

    override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {
        super.onInitializeAccessibilityNodeInfo(info)
        info.className = android.widget.SeekBar::class.java.name
        info.rangeInfo = AccessibilityNodeInfo.RangeInfo.obtain(AccessibilityNodeInfo.RangeInfo.RANGE_TYPE_FLOAT, 0f, 1f, progress)
        if (isEnabled && onSeekRequested != null) {
            info.addAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_SET_PROGRESS)
            info.addAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_SCROLL_FORWARD)
            info.addAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_SCROLL_BACKWARD)
        }
    }

    override fun performAccessibilityAction(action: Int, arguments: Bundle?): Boolean {
        if (isEnabled && onSeekRequested != null) {
            val value = when (action) {
                AccessibilityNodeInfo.AccessibilityAction.ACTION_SET_PROGRESS.id -> arguments?.getFloat(AccessibilityNodeInfo.ACTION_ARGUMENT_PROGRESS_VALUE)
                AccessibilityNodeInfo.ACTION_SCROLL_FORWARD -> progress + 0.1f
                AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD -> progress - 0.1f
                else -> null
            }
            if (value != null) {
                val next = value.coerceIn(0f, 1f)
                setProgress(next)
                onSeekRequested?.invoke(next)
                return true
            }
        }
        return super.performAccessibilityAction(action, arguments)
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
