package com.barkfluff.client.utils

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.TimeInterpolator
import android.animation.ValueAnimator
import android.view.View
import android.view.ViewPropertyAnimator
import android.view.animation.DecelerateInterpolator
import androidx.recyclerview.widget.DefaultItemAnimator
import androidx.recyclerview.widget.RecyclerView

/**
 * Кастомный ItemAnimator для RecyclerView с iOS-подобной анимацией появления сообщений.
 * Анимация включает:
 * - Fade in (появление из прозрачности)
 * - Slide up (выезжание снизу)
 * - Scale bounce (эффект "пружины" - элемент сначала больше, потом уменьшается до нормального размера)
 */
class MessageItemAnimator : DefaultItemAnimator() {

    companion object {
        private const val ANIMATION_DURATION = 350L
        private const val SLIDE_DISTANCE_DP = 60f
        private const val SCALE_INITIAL = 1.12f
        private const val SCALE_OVERSHOOT = 0.97f
        private const val SCALE_FINAL = 1.0f

        // iOS-подобный интерполятор с небольшим bounce эффектом
        private val BOUNCE_INTERPOLATOR = DecelerateInterpolator(1.8f)
    }

    private val pendingAnimations = mutableMapOf<RecyclerView.ViewHolder, Runnable>()
    private val animatorMap = mutableMapOf<RecyclerView.ViewHolder, ViewPropertyAnimator>()

    override fun animateAdd(holder: RecyclerView.ViewHolder): Boolean {
        // Пропускаем анимацию для разделителей дат
        if (shouldSkipAnimation(holder)) {
            dispatchAddFinished(holder)
            return false
        }

        // Начальное состояние: прозрачный, смещён вниз, увеличен
        holder.itemView.alpha = 0f
        holder.itemView.translationY = SLIDE_DISTANCE_DP * holder.itemView.context.resources.displayMetrics.density
        holder.itemView.scaleX = SCALE_INITIAL
        holder.itemView.scaleY = SCALE_INITIAL

        // Запускаем анимацию
        val animator = holder.itemView.animate()
            .alpha(1f)
            .translationY(0f)
            .scaleX(SCALE_OVERSHOOT)
            .scaleY(SCALE_OVERSHOOT)
            .setDuration(ANIMATION_DURATION)
            .setInterpolator(BOUNCE_INTERPOLATOR)
            .setListener(object : AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: Animator) {
                    // Финальная анимация - возврат к нормальному размеру
                    holder.itemView.animate()
                        .scaleX(SCALE_FINAL)
                        .scaleY(SCALE_FINAL)
                        .setDuration(100L)
                        .setInterpolator(DecelerateInterpolator())
                        .setListener(object : AnimatorListenerAdapter() {
                            override fun onAnimationEnd(animation: Animator) {
                                dispatchAddFinished(holder)
                                animatorMap.remove(holder)
                            }
                        })
                        .start()
                }

                override fun onAnimationCancel(animation: Animator) {
                    holder.itemView.alpha = 1f
                    holder.itemView.translationY = 0f
                    holder.itemView.scaleX = SCALE_FINAL
                    holder.itemView.scaleY = SCALE_FINAL
                }
            })

        animatorMap[holder] = animator
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
        // Для изменений используем стандартную анимацию
        return super.animateChange(oldHolder, newHolder, fromX, fromY, toX, toY)
    }

    override fun animateMove(
        holder: RecyclerView.ViewHolder,
        fromX: Int,
        fromY: Int,
        toX: Int,
        toY: Int
    ): Boolean {
        // Сбрасываем состояние перед перемещением
        resetViewHolderState(holder)
        return super.animateMove(holder, fromX, fromY, toX, toY)
    }

    override fun animateRemove(holder: RecyclerView.ViewHolder): Boolean {
        // Для удаления используем простую fade анимацию
        val animator = holder.itemView.animate()
            .alpha(0f)
            .setDuration(200L)
            .setListener(object : AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: Animator) {
                    dispatchRemoveFinished(holder)
                    holder.itemView.alpha = 1f
                }

                override fun onAnimationCancel(animation: Animator) {
                    holder.itemView.alpha = 1f
                }
            })

        animatorMap[holder] = animator
        animator.start()

        return true
    }

    override fun endAnimation(holder: RecyclerView.ViewHolder) {
        // Отменяем все анимации для данного holder
        animatorMap[holder]?.cancel()
        animatorMap.remove(holder)

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
        animatorMap.values.forEach { it.cancel() }
        animatorMap.clear()

        pendingAnimations.forEach { (holder, runnable) ->
            holder.itemView.removeCallbacks(runnable)
        }
        pendingAnimations.clear()

        super.endAnimations()
    }

    override fun isRunning(): Boolean {
        return animatorMap.isNotEmpty() || pendingAnimations.isNotEmpty() || super.isRunning()
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
