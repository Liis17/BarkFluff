package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemChatFolderBinding
import com.barkfluff.client.grpc.GrpcManager

class ChatFoldersAdapter(
    private val onClick: (GrpcManager.ChatFolder) -> Unit
) : ListAdapter<GrpcManager.ChatFolder, ChatFoldersAdapter.VH>(Diff()) {

    fun moveItem(from: Int, to: Int) {
        val list = currentList.toMutableList()
        if (from < 0 || from >= list.size || to < 0 || to >= list.size) return
        val item = list.removeAt(from)
        list.add(to, item)
        submitList(list)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val binding = ItemChatFolderBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return VH(binding)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        holder.bind(getItem(position), position == itemCount - 1)
    }

    inner class VH(private val binding: ItemChatFolderBinding) : RecyclerView.ViewHolder(binding.root) {
        fun bind(folder: GrpcManager.ChatFolder, isLast: Boolean) {
            binding.folderIcon.text = folder.folderIcon.ifBlank { "📁" }
            binding.folderName.text = folder.folderName
            val count = folder.chatIds.size
            binding.folderCount.text = when {
                count == 0 -> "Пусто"
                count == 1 -> "1 чат"
                count in 2..4 -> "$count чата"
                else -> "$count чатов"
            }
            binding.folderDivider.visibility = if (isLast) View.GONE else View.VISIBLE
            binding.root.setOnClickListener { onClick(folder) }
        }
    }

    private class Diff : DiffUtil.ItemCallback<GrpcManager.ChatFolder>() {
        override fun areItemsTheSame(a: GrpcManager.ChatFolder, b: GrpcManager.ChatFolder) = a.folderId == b.folderId
        override fun areContentsTheSame(a: GrpcManager.ChatFolder, b: GrpcManager.ChatFolder) = a == b
    }
}
