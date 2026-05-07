package com.barkfluff.client.editor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PorterDuff
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/**
 * View поверх картинки для рисования пальцем + pinch-to-zoom + pan.
 *
 * Особенности:
 *  - сама рисует [sourceBitmap] (PhotoView под ней должен быть скрыт);
 *  - хранит штрихи в **координатах bitmap** — это позволяет корректно сохранять рисунок при любом zoom/pan;
 *  - 1 палец → рисует штрих текущего цвета и толщины;
 *  - 2 пальца → масштабирует/сдвигает (zoom от 1× до 6×);
 *  - при переходе 1→2 пальцев текущий штрих отбрасывается (чтобы случайно не остался обрывок).
 */
class DrawingOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    private data class Stroke(val path: Path, val color: Int, val widthBitmap: Float)

    private val strokes = ArrayDeque<Stroke>()
    private var current: Stroke? = null

    private var sourceBitmap: Bitmap? = null

    private val viewMatrix = Matrix()
    private val invMatrix = Matrix()
    private val tmpPts = FloatArray(2)

    private var baseScale: Float = 1f
    private var minScale: Float = 1f
    private var maxScale: Float = 6f

    private var lastTouchX = 0f
    private var lastTouchY = 0f
    private var isPinching = false
    private var multiTouchActive = false

    private val scaleDetector = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScaleBegin(d: ScaleGestureDetector): Boolean {
            isPinching = true
            current = null
            return true
        }

        override fun onScale(d: ScaleGestureDetector): Boolean {
            val factor = d.scaleFactor
            val newScale = (currentScale() * factor).coerceIn(minScale, maxScale)
            val effective = newScale / currentScale()
            viewMatrix.postScale(effective, effective, d.focusX, d.focusY)
            constrainMatrix()
            invalidate()
            return true
        }

        override fun onScaleEnd(d: ScaleGestureDetector) {
            // флаг сбросится по ACTION_UP/ACTION_POINTER_UP
        }
    })

    var brushColor: Int = Color.RED
    /** Толщина кисти в пикселях view (на текущем scale). */
    var brushWidthPx: Float = dp(8f)

    private val paintCache = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeJoin = Paint.Join.ROUND
        strokeCap = Paint.Cap.ROUND
    }

    fun setSourceBitmap(bitmap: Bitmap?) {
        sourceBitmap = bitmap
        strokes.clear()
        current = null
        recalcInitialMatrix()
        invalidate()
    }

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
        viewMatrix.reset()
        recalcInitialMatrix()
        invalidate()
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        recalcInitialMatrix()
    }

    private fun recalcInitialMatrix() {
        val bmp = sourceBitmap ?: return
        if (width <= 0 || height <= 0) return
        val sx = width.toFloat() / bmp.width
        val sy = height.toFloat() / bmp.height
        baseScale = min(sx, sy)
        minScale = baseScale
        maxScale = baseScale * 6f
        viewMatrix.reset()
        viewMatrix.postScale(baseScale, baseScale)
        // Центрирование
        val drawnW = bmp.width * baseScale
        val drawnH = bmp.height * baseScale
        viewMatrix.postTranslate((width - drawnW) / 2f, (height - drawnH) / 2f)
    }

    private fun currentScale(): Float {
        val v = FloatArray(9)
        viewMatrix.getValues(v)
        return v[Matrix.MSCALE_X]
    }

    private fun constrainMatrix() {
        val bmp = sourceBitmap ?: return
        val v = FloatArray(9)
        viewMatrix.getValues(v)
        val scale = v[Matrix.MSCALE_X]
        var tx = v[Matrix.MTRANS_X]
        var ty = v[Matrix.MTRANS_Y]
        val drawnW = bmp.width * scale
        val drawnH = bmp.height * scale

        // Bitmap не должен полностью уйти за границы — оставляем его минимум на 30% видимым
        val minVisible = min(width, height) * 0.3f
        val minTx = -drawnW + minVisible
        val maxTx = width - minVisible
        val minTy = -drawnH + minVisible
        val maxTy = height - minVisible
        tx = tx.coerceIn(minTx, maxTx)
        ty = ty.coerceIn(minTy, maxTy)

        v[Matrix.MTRANS_X] = tx
        v[Matrix.MTRANS_Y] = ty
        viewMatrix.setValues(v)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val bmp = sourceBitmap ?: return
        canvas.save()
        canvas.concat(viewMatrix)
        canvas.drawBitmap(bmp, 0f, 0f, null)
        // Paths в координатах bitmap — рисуем как есть
        for (s in strokes) {
            paintCache.color = s.color
            paintCache.strokeWidth = s.widthBitmap
            canvas.drawPath(s.path, paintCache)
        }
        current?.let {
            paintCache.color = it.color
            paintCache.strokeWidth = it.widthBitmap
            canvas.drawPath(it.path, paintCache)
        }
        canvas.restore()
    }

    private fun mapToBitmap(x: Float, y: Float): FloatArray {
        viewMatrix.invert(invMatrix)
        tmpPts[0] = x
        tmpPts[1] = y
        invMatrix.mapPoints(tmpPts)
        return tmpPts
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        // Pinch-zoom детектор первым — он сам отслеживает count fingers
        scaleDetector.onTouchEvent(event)

        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                if (event.pointerCount == 1) {
                    val pts = mapToBitmap(event.x, event.y)
                    val widthBitmap = brushWidthPx / max(currentScale(), 0.0001f)
                    val p = Path().apply { moveTo(pts[0], pts[1]) }
                    current = Stroke(p, brushColor, widthBitmap)
                    lastTouchX = event.x
                    lastTouchY = event.y
                    invalidate()
                }
                return true
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                // переходим в zoom-режим, отбрасываем текущий path
                multiTouchActive = true
                current = null
                lastTouchX = focusX(event)
                lastTouchY = focusY(event)
                invalidate()
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount >= 2 || isPinching || multiTouchActive) {
                    val fx = focusX(event)
                    val fy = focusY(event)
                    val dx = fx - lastTouchX
                    val dy = fy - lastTouchY
                    if (abs(dx) > 0.5f || abs(dy) > 0.5f) {
                        viewMatrix.postTranslate(dx, dy)
                        constrainMatrix()
                        invalidate()
                    }
                    lastTouchX = fx
                    lastTouchY = fy
                } else if (current != null) {
                    val pts = mapToBitmap(event.x, event.y)
                    current?.path?.lineTo(pts[0], pts[1])
                    invalidate()
                }
                return true
            }
            MotionEvent.ACTION_POINTER_UP -> {
                lastTouchX = focusX(event, ignoreIndex = event.actionIndex)
                lastTouchY = focusY(event, ignoreIndex = event.actionIndex)
                if (event.pointerCount - 1 < 2) {
                    isPinching = false
                }
                return true
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (!multiTouchActive) {
                    current?.let { strokes.addLast(it) }
                }
                current = null
                multiTouchActive = false
                isPinching = false
                invalidate()
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    private fun focusX(event: MotionEvent, ignoreIndex: Int = -1): Float {
        var sum = 0f
        var n = 0
        for (i in 0 until event.pointerCount) {
            if (i == ignoreIndex) continue
            sum += event.getX(i)
            n++
        }
        return if (n > 0) sum / n else event.x
    }

    private fun focusY(event: MotionEvent, ignoreIndex: Int = -1): Float {
        var sum = 0f
        var n = 0
        for (i in 0 until event.pointerCount) {
            if (i == ignoreIndex) continue
            sum += event.getY(i)
            n++
        }
        return if (n > 0) sum / n else event.y
    }

    /**
     * Создаёт результирующий Bitmap: оригинал + штрихи в bitmap-coords.
     */
    fun renderResultBitmap(): Bitmap? {
        val src = sourceBitmap ?: return null
        if (!hasDrawings()) return src
        val out = Bitmap.createBitmap(src.width, src.height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(out)
        canvas.drawColor(Color.TRANSPARENT, PorterDuff.Mode.CLEAR)
        canvas.drawBitmap(src, 0f, 0f, null)
        for (s in strokes) {
            paintCache.color = s.color
            paintCache.strokeWidth = s.widthBitmap
            canvas.drawPath(s.path, paintCache)
        }
        return out
    }

    private fun dp(v: Float): Float = v * resources.displayMetrics.density
}
