package com.barkfluff.client.editor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PorterDuff
import android.graphics.RectF
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import kotlin.math.max

/**
 * View поверх ImageView для рисования пальцем по картинке.
 * Хранит список Path'ов с цветом и шириной кисти. На подтверждение умеет
 * вернуть Bitmap-результат с применёнными штрихами.
 */
class DrawingOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    private data class Stroke(val path: Path, val color: Int, val widthPx: Float)

    private val strokes = ArrayDeque<Stroke>()
    private var current: Stroke? = null

    /** Bitmap картинки в её пиксельных координатах. Используется для расчёта rect отображения и финального flatten. */
    private var sourceBitmap: Bitmap? = null

    /** Прямоугольник, в который ImageView рисует bitmap (fitCenter). Используется для маскирования рисунка картинкой. */
    private val displayRect = RectF()

    var brushColor: Int = Color.RED
    var brushWidthPx: Float = dp(8f)

    private val paintCache = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeJoin = Paint.Join.ROUND
        strokeCap = Paint.Cap.ROUND
    }

    fun setSourceBitmap(bitmap: Bitmap?) {
        sourceBitmap = bitmap
        recalcDisplayRect()
        strokes.clear()
        current = null
        invalidate()
    }

    /** Имеются ли нарисованные штрихи. */
    fun hasDrawings(): Boolean = strokes.isNotEmpty() || current != null

    fun undo() {
        if (strokes.isNotEmpty()) {
            strokes.removeLast()
            invalidate()
        }
    }

    fun clearAll() {
        strokes.clear()
        current = null
        invalidate()
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        recalcDisplayRect()
    }

    private fun recalcDisplayRect() {
        val bmp = sourceBitmap ?: run {
            displayRect.setEmpty()
            return
        }
        if (width <= 0 || height <= 0) return
        val viewRatio = width.toFloat() / height.toFloat()
        val bmpRatio = bmp.width.toFloat() / bmp.height.toFloat()
        if (bmpRatio > viewRatio) {
            val drawW = width.toFloat()
            val drawH = drawW / bmpRatio
            val top = (height - drawH) / 2f
            displayRect.set(0f, top, drawW, top + drawH)
        } else {
            val drawH = height.toFloat()
            val drawW = drawH * bmpRatio
            val left = (width - drawW) / 2f
            displayRect.set(left, 0f, left + drawW, drawH)
        }
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        if (displayRect.isEmpty) return
        canvas.save()
        canvas.clipRect(displayRect)
        for (s in strokes) drawStroke(canvas, s)
        current?.let { drawStroke(canvas, it) }
        canvas.restore()
    }

    private fun drawStroke(canvas: Canvas, stroke: Stroke) {
        paintCache.color = stroke.color
        paintCache.strokeWidth = stroke.widthPx
        canvas.drawPath(stroke.path, paintCache)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (sourceBitmap == null || displayRect.isEmpty) return false
        val x = event.x.coerceIn(displayRect.left, displayRect.right)
        val y = event.y.coerceIn(displayRect.top, displayRect.bottom)
        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                val p = Path().apply { moveTo(x, y) }
                current = Stroke(p, brushColor, brushWidthPx)
                invalidate()
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                current?.path?.lineTo(x, y)
                invalidate()
                return true
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                current?.let { strokes.addLast(it) }
                current = null
                invalidate()
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    /**
     * Создаёт результирующий Bitmap: оригинал + штрихи, отскейленные в координаты bitmap.
     */
    fun renderResultBitmap(): Bitmap? {
        val src = sourceBitmap ?: return null
        if (!hasDrawings()) return src
        val out = Bitmap.createBitmap(src.width, src.height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(out)
        canvas.drawColor(Color.TRANSPARENT, PorterDuff.Mode.CLEAR)
        canvas.drawBitmap(src, 0f, 0f, null)
        val sx = src.width.toFloat() / max(displayRect.width(), 1f)
        val sy = src.height.toFloat() / max(displayRect.height(), 1f)
        val matrix = Matrix().apply {
            postTranslate(-displayRect.left, -displayRect.top)
            postScale(sx, sy)
        }
        for (s in strokes) {
            val transformed = Path()
            s.path.transform(matrix, transformed)
            paintCache.color = s.color
            paintCache.strokeWidth = s.widthPx * sx
            canvas.drawPath(transformed, paintCache)
        }
        return out
    }

    private fun dp(v: Float): Float = v * resources.displayMetrics.density
}
