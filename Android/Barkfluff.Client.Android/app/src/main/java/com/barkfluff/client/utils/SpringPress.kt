package com.barkfluff.client.utils

import android.annotation.SuppressLint
import android.view.MotionEvent
import android.view.View
import androidx.dynamicanimation.animation.SpringAnimation
import androidx.dynamicanimation.animation.SpringForce

// M3 Expressive spring tokens (https://m3.material.io — Motion Physics):
//   spatial.fast: stiffness=1400, damping=0.9 — для коротких перемещений UI-элементов
// Pressed-state кнопок попадает именно сюда (быстрое сжатие + возврат с лёгким overshoot).
private const val SPRING_STIFFNESS = 1400f
private const val SPRING_DAMPING = 0.9f
private const val PRESS_SCALE = 0.95f

@SuppressLint("ClickableViewAccessibility")
fun View.applySpringPress() {
    val scaleX = SpringAnimation(this, SpringAnimation.SCALE_X, 1f).apply {
        spring.stiffness = SPRING_STIFFNESS
        spring.dampingRatio = SPRING_DAMPING
    }
    val scaleY = SpringAnimation(this, SpringAnimation.SCALE_Y, 1f).apply {
        spring.stiffness = SPRING_STIFFNESS
        spring.dampingRatio = SPRING_DAMPING
    }

    // Возвращаем false — View сам обработает click через свой onTouchEvent (включая
    // performClick для accessibility). Мы только наблюдаем за состоянием.
    setOnTouchListener { _, ev ->
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                scaleX.cancel(); scaleY.cancel()
                scaleX.animateToFinalPosition(PRESS_SCALE)
                scaleY.animateToFinalPosition(PRESS_SCALE)
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                scaleX.animateToFinalPosition(1f)
                scaleY.animateToFinalPosition(1f)
            }
        }
        false
    }
}
