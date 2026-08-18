package com.barkfluff.client.utils

import android.app.Activity
import android.view.MotionEvent
import android.view.View
import kotlin.math.abs

/**
 * Свайп вверх/вниз для закрытия полноэкранного вьюера (фото/видео) с уменьшением
 * и затуханием контента — Activity должна быть translucent, чтобы под ним
 * проступал открытый чат.
 *
 * Перехватывает жест через [onDispatchTouchEvent], вызываемый из
 * `Activity.dispatchTouchEvent()`, а не через обычный `View.OnTouchListener`:
 * дочерние view (ViewPager2/PhotoView) сами забирают touch-поток себе на ACTION_DOWN
 * и OnTouchListener корневого layout'а до них просто не доходит.
 */
class SwipeToDismissHelper(
    private val activity: Activity,
    private val target: View,
    private val onDismissed: () -> Unit
) {

    private var startX = 0f
    private var startY = 0f
    private var isDragging = false
    private val screenHeight = activity.resources.displayMetrics.heightPixels.toFloat()

    /**
     * Вызывать из `Activity.dispatchTouchEvent()` до `super.dispatchTouchEvent(ev)`.
     * Возвращает true, если событие уже обработано жестом закрытия — в этом случае
     * дальше в `super.dispatchTouchEvent()` его передавать не нужно.
     */
    fun onDispatchTouchEvent(ev: MotionEvent): Boolean {
        when (ev.action) {
            MotionEvent.ACTION_DOWN -> {
                startX = ev.rawX
                startY = ev.rawY
                isDragging = false
            }
            MotionEvent.ACTION_MOVE -> {
                val deltaY = ev.rawY - startY
                val deltaX = ev.rawX - startX

                if (!isDragging && abs(deltaY) > DRAG_LOCK_THRESHOLD && abs(deltaY) > abs(deltaX)) {
                    isDragging = true
                    // Отменяем жест у дочерних view (ViewPager2/PhotoView), чтобы они
                    // не продолжали параллельно обрабатывать те же MOVE-события.
                    cancelChildTouch(ev)
                }

                if (isDragging) {
                    target.translationY = deltaY
                    val progress = (abs(deltaY) / (screenHeight * 0.5f)).coerceIn(0f, 1f)
                    target.alpha = 1f - progress
                    val scale = 1f - progress * (1f - SCALE_MIN)
                    target.scaleX = scale
                    target.scaleY = scale
                    return true
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (isDragging) {
                    val deltaY = ev.rawY - startY
                    if (abs(deltaY) > screenHeight * RELEASE_THRESHOLD_FRACTION) {
                        dismiss(if (deltaY > 0) 1f else -1f)
                    } else {
                        snapBack()
                    }
                    isDragging = false
                    return true
                }
            }
        }
        return false
    }

    private fun cancelChildTouch(source: MotionEvent) {
        val cancelEvent = MotionEvent.obtain(source).apply { action = MotionEvent.ACTION_CANCEL }
        target.dispatchTouchEvent(cancelEvent)
        cancelEvent.recycle()
    }

    fun dismiss(sign: Float = 1f) {
        target.animate()
            .translationY(sign * screenHeight)
            .alpha(0f)
            .scaleX(SCALE_MIN)
            .scaleY(SCALE_MIN)
            .setDuration(DISMISS_DURATION_MS)
            .withEndAction {
                onDismissed()
                activity.overridePendingTransition(0, 0)
            }
            .start()
    }

    private fun snapBack() {
        target.animate()
            .translationY(0f)
            .alpha(1f)
            .scaleX(1f)
            .scaleY(1f)
            .setDuration(SNAP_BACK_DURATION_MS)
            .start()
    }

    companion object {
        private const val DRAG_LOCK_THRESHOLD = 20f
        private const val RELEASE_THRESHOLD_FRACTION = 0.25f
        private const val SCALE_MIN = 0.82f
        private const val DISMISS_DURATION_MS = 250L
        private const val SNAP_BACK_DURATION_MS = 200L
    }
}
