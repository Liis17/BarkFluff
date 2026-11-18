package com.barkfluff.client.view

import android.content.Context
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.ViewConfiguration
import androidx.core.widget.NestedScrollView
import kotlin.math.abs

/**
 * NestedScrollView, отдающий вертикальный жест [listener], пока контент прокручен в самый верх:
 * оттягивание вниз раскрывает шапку, обратный жест сворачивает её, затем снова работает прокрутка.
 */
class PullToExpandScrollView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
) : NestedScrollView(context, attrs) {

    interface Listener {
        /** Можно ли начать или продолжить оттягивание при смещении пальца [dy] (>0 — вниз). */
        fun canPull(dy: Float): Boolean

        /** Применяет смещение. false — шапка свернулась и жест возвращается прокрутке. */
        fun onPull(dy: Float): Boolean

        fun onPullReleased()
    }

    var listener: Listener? = null

    private val touchSlop = ViewConfiguration.get(context).scaledTouchSlop
    private var downY = 0f
    private var lastY = 0f
    private var pulling = false

    override fun onInterceptTouchEvent(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downY = ev.y
                lastY = ev.y
                pulling = false
            }
            MotionEvent.ACTION_MOVE -> {
                val total = ev.y - downY
                if (!pulling && abs(total) > touchSlop && scrollY == 0 && listener?.canPull(total) == true) {
                    pulling = true
                    lastY = ev.y
                    return true
                }
            }
        }
        return super.onInterceptTouchEvent(ev)
    }

    override fun onTouchEvent(ev: MotionEvent): Boolean {
        val l = listener ?: return super.onTouchEvent(ev)
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downY = ev.y
                lastY = ev.y
                pulling = false
            }
            MotionEvent.ACTION_MOVE -> {
                val dy = ev.y - lastY
                lastY = ev.y
                if (!pulling && scrollY == 0 && dy != 0f && l.canPull(dy)) pulling = true
                if (pulling) {
                    if (l.onPull(dy)) return true
                    pulling = false
                    // Сбрасываем точку отсчёта прокрутки, чтобы контент не прыгнул на накопленное смещение.
                    val restart = MotionEvent.obtain(ev).apply { action = MotionEvent.ACTION_DOWN }
                    super.onTouchEvent(restart)
                    restart.recycle()
                    return true
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (pulling) {
                    pulling = false
                    l.onPullReleased()
                    val cancel = MotionEvent.obtain(ev).apply { action = MotionEvent.ACTION_CANCEL }
                    super.onTouchEvent(cancel)
                    cancel.recycle()
                    return true
                }
            }
        }
        return super.onTouchEvent(ev)
    }
}
