package com.barkfluff.client.utils

import android.animation.ValueAnimator
import android.view.View

/**
 * Пульсация прозрачности для заглушек загрузки (скелетонов) — та же анимация, что у списка чатов.
 * Владелец обязан вызвать [stop], когда данные пришли или экран уничтожается.
 */
class SkeletonPulse {
    private var animator: ValueAnimator? = null
    private var views: List<View> = emptyList()

    fun start(targets: List<View>) {
        stop()
        views = targets
        animator = ValueAnimator.ofFloat(1f, 0.35f, 1f).apply {
            duration = 900L
            repeatCount = ValueAnimator.INFINITE
            addUpdateListener { anim ->
                val alpha = anim.animatedValue as Float
                views.forEach { it.alpha = alpha }
            }
            start()
        }
    }

    fun stop() {
        animator?.cancel()
        animator = null
        views.forEach { it.alpha = 1f }
        views = emptyList()
    }
}
