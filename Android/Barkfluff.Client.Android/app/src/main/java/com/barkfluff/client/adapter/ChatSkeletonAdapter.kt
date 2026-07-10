package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemChatSkeletonBinding

class ChatSkeletonAdapter(
    private val skeletonCount: Int = 7
) : RecyclerView.Adapter<ChatSkeletonAdapter.SkeletonViewHolder>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): SkeletonViewHolder {
        val binding = ItemChatSkeletonBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return SkeletonViewHolder(binding)
    }

    override fun onBindViewHolder(holder: SkeletonViewHolder, position: Int) = Unit

    override fun getItemCount(): Int = skeletonCount

    class SkeletonViewHolder(binding: ItemChatSkeletonBinding) : RecyclerView.ViewHolder(binding.root)
}
