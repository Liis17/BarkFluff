package com.barkfluff.client.utils

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ObjectAnimator
import android.animation.PropertyValuesHolder
import android.view.View
import android.view.ViewPropertyAnimator
import android.view.animation.DecelerateInterpolator
import android.view.animation.OvershootInterpolator
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
        private const val ANIMATION_DURATION = 400L
        private const val ANIMATION_DURATION_SHORT = 120L
        private const val SCALE_INITIAL = 1.2f
        private const val SCALE_OVERSHOOT = 0.95f
        private const val SCALE_FINAL = 1.0f
        
        // Интерполятор с плавным замедлением и небольшим overshoot эффектом
        private val DECELERATE_INTERPOLATOR = DecelerateInterpolator(2.0f)
        private val OVERSHOOT_INTERPOLATOR = OvershootInterpolator(1.2f)
    }

    private val pendingAnimations = mutableMapOf<RecyclerView.ViewHolder, Runnable>()
    private val animatorMap = mutableMapOf<RecyclerView.ViewHolder, ViewPropertyAnimator>()

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
        
        // Добавляем spring-эффект в конце
        animator.addListener(object : AnimatorListenerAdapter() {
            private var wasCancelled = false
            
            override fun onAnimationEnd(animation: Animator) {
                if (wasCancelled) return
                
                // Spring bounce анимация для эффекта "пружины"
                view.animate()
                    .scaleX(SCALE_OVERSHOOT)
                    .scaleY(SCALE_OVERSHOOT)
                    .setDuration(ANIMATION_DURATION_SHORT / 2)
                    .setInterpolator(OVERSHOOT_INTERPOLATOR)
                    .withEndAction {
                        view.animate()
                            .scaleX(SCALE_FINAL)
                            .scaleY(SCALE_FINAL)
                            .setDuration(ANIMATION_DURATION_SHORT)
                            .setInterpolator(OVERSHOOT_INTERPOLATOR)
                            .withEndAction {
                                dispatchAddFinished(holder)
                                animatorMap.remove(holder)
                            }
                            .start()
                    }
                    .start()
            }
            
            override fun onAnimationCancel(animation: Animator) {
                wasCancelled = true
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
