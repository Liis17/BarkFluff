package com.barkfluff.client.adapter

import android.animation.ValueAnimator
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemChatSkeletonBinding

class ChatSkeletonAdapter(
    private val skeletonCount: Int = 7
) : RecyclerView.Adapter<ChatSkeletonAdapter.SkeletonViewHolder>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): SkeletonViewHolder {
        val binding = ItemChatSkeletonBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return SkeletonViewHolder(binding)
    }

    override fun onBindViewHolder(holder: SkeletonViewHolder, position: Int) {
        holder.startPulse(position)
    }

    override fun onViewRecycled(holder: SkeletonViewHolder) {
        holder.stopPulse()
    }

    override fun getItemCount(): Int = skeletonCount

    class SkeletonViewHolder(binding: ItemChatSkeletonBinding) : RecyclerView.ViewHolder(binding.root) {
        private val pulseViews = listOf(binding.skeletonAvatar, binding.skeletonLine1, binding.skeletonLine2)
        private var animator: ValueAnimator? = null

        fun startPulse(position: Int) {
            stopPulse()
            val anim = ValueAnimator.ofFloat(1f, 0.35f, 1f).apply {
                duration = 900L
                startDelay = (position % 4) * 120L
                repeatCount = ValueAnimator.INFINITE
            }
            anim.addUpdateListener { valueAnimator ->
                val alpha = valueAnimator.animatedValue as Float
                pulseViews.forEach { it.alpha = alpha }
            }
            anim.start()
            animator = anim
        }

        fun stopPulse() {
            animator?.cancel()
            animator = null
            pulseViews.forEach { it.alpha = 1f }
        }
    }
}
