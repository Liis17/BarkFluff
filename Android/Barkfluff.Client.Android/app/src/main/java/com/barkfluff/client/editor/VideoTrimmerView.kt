package com.barkfluff.client.editor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.max
import kotlin.math.min

/**
 * Полоса обрезки видео: фон — 8 миниатюр кадров, две ручки (start/end), индикатор текущей позиции.
 *
 * Координаты: [0..1] для миллисекунд позиции вдоль ширины view.
 */
class VideoTrimmerView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    private val frames = mutableListOf<Bitmap>()
    private var durationMs: Long = 0L

    /** 0..1 — относительные позиции ручек. */
    private var startFrac: Float = 0f
    private var endFrac: Float = 1f

    /** 0..1 — индикатор плеера. */
    private var playFrac: Float = 0f

    private var draggingHandle: Handle = Handle.NONE
    private enum class Handle { NONE, START, END }

    private val maskPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#A6000000")
    }
    private val handlePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#FFFFFFFF")
    }
    private val handleStrokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#1A1A1A")
        style = Paint.Style.STROKE
        strokeWidth = dp(1f)
    }
    private val borderPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#FFFFFFFF")
        style = Paint.Style.STROKE
        strokeWidth = dp(2f)
    }
    private val playPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#FF6B35")
        strokeWidth = dp(2f)
    }
    private val frameRect = Rect()
    private val drawRect = RectF()

    private val handleWidthPx = dp(12f)
    private val verticalPadding = dp(2f)

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    var onRangeChanged: ((startMs: Long, endMs: Long) -> Unit)? = null
    var onSeekRequested: ((timeMs: Long) -> Unit)? = null

    fun setVideo(uri: Uri, durationMs: Long, frameCount: Int = 8) {
        this.durationMs = durationMs
        this.startFrac = 0f
        this.endFrac = 1f
        this.playFrac = 0f
        frames.clear()
        invalidate()

        scope.launch {
            val list = withContext(Dispatchers.IO) { extractFrames(uri, durationMs, frameCount) }
            frames.clear()
            frames.addAll(list)
            invalidate()
        }
    }

    fun setPlayPosition(positionMs: Long) {
        if (durationMs <= 0) return
        playFrac = (positionMs.toFloat() / durationMs.toFloat()).coerceIn(0f, 1f)
        invalidate()
    }

    fun startMs(): Long = (startFrac * durationMs).toLong()
    fun endMs(): Long = (endFrac * durationMs).toLong()

    private suspend fun extractFrames(uri: Uri, durationMs: Long, count: Int): List<Bitmap> {
        if (durationMs <= 0 || count <= 0) return emptyList()
        val r = MediaMetadataRetriever()
        return try {
            r.setDataSource(context, uri)
            val out = mutableListOf<Bitmap>()
            for (i in 0 until count) {
                val tUs = (durationMs * 1000L * i / count)
                val bmp = r.getFrameAtTime(tUs, MediaMetadataRetriever.OPTION_CLOSEST_SYNC)
                if (bmp != null) out.add(bmp)
            }
            out
        } catch (e: Exception) { emptyList() } finally {
            try { r.release() } catch (_: Throwable) {}
        }
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0 || h <= 0) return

        val top = verticalPadding
        val bottom = h - verticalPadding

        // Frames background
        if (frames.isNotEmpty()) {
            val cellW = w / frames.size
            for ((i, bmp) in frames.withIndex()) {
                drawRect.set(i * cellW, top, (i + 1) * cellW, bottom)
                frameRect.set(0, 0, bmp.width, bmp.height)
                canvas.drawBitmap(bmp, frameRect, drawRect, null)
            }
        } else {
            drawRect.set(0f, top, w, bottom)
            handlePaint.color = Color.parseColor("#202020")
            canvas.drawRect(drawRect, handlePaint)
            handlePaint.color = Color.WHITE
        }

        val startX = startFrac * w
        val endX = endFrac * w

        // Mask outside [start..end]
        canvas.drawRect(0f, top, startX, bottom, maskPaint)
        canvas.drawRect(endX, top, w, bottom, maskPaint)

        // White border around selection
        canvas.drawRect(startX, top, endX, bottom, borderPaint)

        // Handles
        val handleTop = 0f
        val handleBottom = h
        val sLeft = startX - handleWidthPx
        val sRight = startX
        val eLeft = endX
        val eRight = endX + handleWidthPx
        canvas.drawRect(sLeft, handleTop, sRight, handleBottom, handlePaint)
        canvas.drawRect(sLeft, handleTop, sRight, handleBottom, handleStrokePaint)
        canvas.drawRect(eLeft, handleTop, eRight, handleBottom, handlePaint)
        canvas.drawRect(eLeft, handleTop, eRight, handleBottom, handleStrokePaint)

        // Play position marker (within selection only)
        if (playFrac in startFrac..endFrac) {
            val px = playFrac * w
            canvas.drawLine(px, top, px, bottom, playPaint)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val w = width.toFloat()
        if (w <= 0 || durationMs <= 0) return false
        val x = event.x
        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                val startX = startFrac * w
                val endX = endFrac * w
                draggingHandle = when {
                    x in (startX - handleWidthPx * 2)..(startX + handleWidthPx) -> Handle.START
                    x in (endX - handleWidthPx)..(endX + handleWidthPx * 2) -> Handle.END
                    else -> Handle.NONE
                }
                if (draggingHandle == Handle.NONE) {
                    // Tap внутри полосы — seek
                    val frac = (x / w).coerceIn(0f, 1f)
                    onSeekRequested?.invoke((frac * durationMs).toLong())
                    return true
                }
                parent?.requestDisallowInterceptTouchEvent(true)
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                if (draggingHandle == Handle.NONE) return false
                val frac = (x / w).coerceIn(0f, 1f)
                if (draggingHandle == Handle.START) {
                    startFrac = min(frac, max(0f, endFrac - 0.02f))
                } else {
                    endFrac = max(frac, min(1f, startFrac + 0.02f))
                }
                onRangeChanged?.invoke(startMs(), endMs())
                invalidate()
                return true
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (draggingHandle != Handle.NONE) {
                    onSeekRequested?.invoke(if (draggingHandle == Handle.START) startMs() else endMs())
                }
                draggingHandle = Handle.NONE
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    override fun onDetachedFromWindow() {
        super.onDetachedFromWindow()
        scope.cancel()
    }

    private fun dp(v: Float): Float = v * resources.displayMetrics.density
}
