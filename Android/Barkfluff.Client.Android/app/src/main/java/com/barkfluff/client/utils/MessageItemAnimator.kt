package com.barkfluff.client.utils

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ObjectAnimator
import android.animation.PropertyValuesHolder
import android.view.View
import android.view.animation.DecelerateInterpolator
import androidx.recyclerview.widget.DefaultItemAnimator
import androidx.recyclerview.widget.RecyclerView

/**
 * Кастомный ItemAnimator для RecyclerView с iOS-подобной анимацией появления сообщений.
 * Анимация включает:
 * - Появление из центра экрана с увеличенного масштаба
 * - Fade in (появление из прозрачности)
 * - Spring bounce эффект при достижении конечной позиции
 *
 * Сообщения появляются снизу из центра экрана, начиная с увеличенного размера (1.2x),
 * прозрачные, и плавно "встают" на своё место с эффектом пружины.
 */
class MessageItemAnimator : DefaultItemAnimator() {

    companion object {
        private const val ANIMATION_DURATION = 350L
        private const val SCALE_INITIAL = 1.08f
        private const val SCALE_FINAL = 1.0f

        // Интерполятор с плавным замедлением
        private val DECELERATE_INTERPOLATOR = DecelerateInterpolator(1.5f)
    }

    private val pendingAnimations = mutableMapOf<RecyclerView.ViewHolder, Runnable>()

    override fun animateAdd(holder: RecyclerView.ViewHolder): Boolean {
        // Пропускаем анимацию для разделителей дат
        if (shouldSkipAnimation(holder)) {
            dispatchAddFinished(holder)
            return false
        }

        val view = holder.itemView
        view.alpha = 0f
        view.scaleX = SCALE_INITIAL
        view.scaleY = SCALE_INITIAL

        // Анимация появления: масштаб + прозрачность
        val scaleX = PropertyValuesHolder.ofFloat(View.SCALE_X, SCALE_INITIAL, SCALE_FINAL)
        val scaleY = PropertyValuesHolder.ofFloat(View.SCALE_Y, SCALE_INITIAL, SCALE_FINAL)
        val alpha = PropertyValuesHolder.ofFloat(View.ALPHA, 0f, 1f)

        val animator = ObjectAnimator.ofPropertyValuesHolder(view, scaleX, scaleY, alpha)
        animator.duration = ANIMATION_DURATION
        animator.interpolator = DECELERATE_INTERPOLATOR
        animator.addListener(object : AnimatorListenerAdapter() {
            override fun onAnimationEnd(animation: Animator) {
                dispatchAddFinished(holder)
            }
            override fun onAnimationCancel(animation: Animator) {
                view.alpha = 1f
                view.scaleX = SCALE_FINAL
                view.scaleY = SCALE_FINAL
            }
        })

        animator.start()
        return true
    }

    override fun animateChange(
        oldHolder: RecyclerView.ViewHolder,
        newHolder: RecyclerView.ViewHolder,
        fromX: Int,
        fromY: Int,
        toX: Int,
        toY: Int
    ): Boolean {
        // Для изменений используем плавную fade анимацию с масштабированием
        val view = newHolder.itemView
        view.alpha = 0f
        view.scaleX = SCALE_INITIAL
        view.scaleY = SCALE_INITIAL
        
        view.animate()
            .alpha(1f)
            .scaleX(SCALE_FINAL)
            .scaleY(SCALE_FINAL)
            .setDuration(ANIMATION_DURATION)
            .setInterpolator(DECELERATE_INTERPOLATOR)
            .setListener(object : AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: Animator) {
                    dispatchChangeFinished(newHolder, true)
                }
                override fun onAnimationCancel(animation: Animator) {
                    view.alpha = 1f
                    view.scaleX = SCALE_FINAL
                    view.scaleY = SCALE_FINAL
                }
            })
            .start()
        
        return true
    }

    override fun animateMove(
        holder: RecyclerView.ViewHolder,
        fromX: Int,
        fromY: Int,
        toX: Int,
        toY: Int
    ): Boolean {
        val view = holder.itemView

        // Вычисляем дельту перемещения
        val deltaX = toX - fromX
        val deltaY = toY - fromY

        // Начальное состояние - смещаем в противоположную сторону
        view.translationX = -deltaX.toFloat()
        view.translationY = -deltaY.toFloat()
        view.alpha = 0.5f
        view.scaleX = 0.95f
        view.scaleY = 0.95f

        // Анимация перемещения с плавным замедлением
        view.animate()
            .translationX(0f)
            .translationY(0f)
            .alpha(1f)
            .scaleX(SCALE_FINAL)
            .scaleY(SCALE_FINAL)
            .setDuration(ANIMATION_DURATION)
            .setInterpolator(DECELERATE_INTERPOLATOR)
            .setListener(object : AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: Animator) {
                    dispatchMoveFinished(holder)
                }
                override fun onAnimationCancel(animation: Animator) {
                    view.translationX = 0f
                    view.translationY = 0f
                    view.alpha = 1f
                    view.scaleX = SCALE_FINAL
                    view.scaleY = SCALE_FINAL
                }
            })
            .start()

        return true
    }

    override fun animateRemove(holder: RecyclerView.ViewHolder): Boolean {
        // Для удаления используем анимацию с масштабированием и fade
        val view = holder.itemView

        view.animate()
            .alpha(0f)
            .scaleX(0.9f)
            .scaleY(0.9f)
            .setDuration(250L)
            .setInterpolator(DECELERATE_INTERPOLATOR)
            .setListener(object : AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: Animator) {
                    dispatchRemoveFinished(holder)
                    view.alpha = 1f
                    view.scaleX = SCALE_FINAL
                    view.scaleY = SCALE_FINAL
                }

                override fun onAnimationCancel(animation: Animator) {
                    view.alpha = 1f
                    view.scaleX = SCALE_FINAL
                    view.scaleY = SCALE_FINAL
                }
            })
            .start()

        return true
    }

    override fun endAnimation(holder: RecyclerView.ViewHolder) {
        // Отменяем все анимации для данного holder
        holder.itemView.animate().cancel()

        pendingAnimations[holder]?.let {
            holder.itemView.removeCallbacks(it)
            pendingAnimations.remove(holder)
        }

        // Сбрасываем состояние
        resetViewHolderState(holder)

        super.endAnimation(holder)
    }

    override fun endAnimations() {
        // Отменяем все анимации
        pendingAnimations.forEach { (holder, runnable) ->
            holder.itemView.removeCallbacks(runnable)
        }
        pendingAnimations.clear()

        super.endAnimations()
    }

    override fun isRunning(): Boolean {
        return pendingAnimations.isNotEmpty() || super.isRunning()
    }

    override fun canReuseUpdatedViewHolder(holder: RecyclerView.ViewHolder): Boolean {
        return true
    }

    private fun shouldSkipAnimation(holder: RecyclerView.ViewHolder): Boolean {
        // Можно добавить проверку по типу ViewHolder, если нужно пропускать анимацию
        // для определённых типов элементов (например, разделителей дат)
        return false
    }

    private fun resetViewHolderState(holder: RecyclerView.ViewHolder) {
        holder.itemView.alpha = 1f
        holder.itemView.translationY = 0f
        holder.itemView.translationX = 0f
        holder.itemView.scaleX = SCALE_FINAL
        holder.itemView.scaleY = SCALE_FINAL
    }
}
