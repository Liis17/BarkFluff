package com.barkfluff.client.utils

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ObjectAnimator
import android.view.View
import android.view.ViewGroup
import android.view.animation.AccelerateDecelerateInterpolator
import android.view.animation.OvershootInterpolator

/**
 * Утилита для анимации переходов между страницами
 * Страницы расположены: Контакты (слева) ← Чаты (центр) → Профиль (справа)
 */
object PageTransitionAnimator {

    private const val DURATION = 300L
    private const val SLIDE_DISTANCE = 100f

    /**
     * Направление перехода
     */
    enum class Direction {
        LEFT,   // Переход на страницу левее (например, Чаты → Контакты)
        RIGHT,  // Переход на страницу правее (например, Чаты → Профиль)
        NONE    // Без анимации
    }

    /**
     * Анимация перехода для Activity
     * @param enterView View входящего Activity (может быть null)
     * @param exitView View исходящего Activity (может быть null)
     * @param direction Направление перехода
     */
    fun animateTransition(enterView: View?, exitView: View?, direction: Direction) {
        when (direction) {
            Direction.LEFT -> animateSlideLeft(enterView, exitView)
            Direction.RIGHT -> animateSlideRight(enterView, exitView)
            Direction.NONE -> {}
        }
    }

    private fun animateSlideLeft(enterView: View?, exitView: View?) {
        // Вход: новая страница выезжает слева
        enterView?.let { view ->
            view.translationX = -SLIDE_DISTANCE
            view.alpha = 0f
            ObjectAnimator.ofFloat(view, View.TRANSLATION_X, -SLIDE_DISTANCE, 0f).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
            ObjectAnimator.ofFloat(view, View.ALPHA, 0f, 1f).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
        }

        // Выход: текущая страница уходит влево
        exitView?.let { view ->
            ObjectAnimator.ofFloat(view, View.TRANSLATION_X, 0f, -SLIDE_DISTANCE).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
            ObjectAnimator.ofFloat(view, View.ALPHA, 1f, 0f).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
        }
    }

    private fun animateSlideRight(enterView: View?, exitView: View?) {
        // Вход: новая страница выезжает справа
        enterView?.let { view ->
            view.translationX = SLIDE_DISTANCE
            view.alpha = 0f
            ObjectAnimator.ofFloat(view, View.TRANSLATION_X, SLIDE_DISTANCE, 0f).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
            ObjectAnimator.ofFloat(view, View.ALPHA, 0f, 1f).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
        }

        // Выход: текущая страница уходит вправо
        exitView?.let { view ->
            ObjectAnimator.ofFloat(view, View.TRANSLATION_X, 0f, SLIDE_DISTANCE).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
            ObjectAnimator.ofFloat(view, View.ALPHA, 1f, 0f).apply {
                duration = DURATION
                interpolator = AccelerateDecelerateInterpolator()
                start()
            }
        }
    }
}
