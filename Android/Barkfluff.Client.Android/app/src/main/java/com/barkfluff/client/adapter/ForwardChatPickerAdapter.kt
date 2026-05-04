package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemChatForwardPickerBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader

/**
 * Адаптер для списка чатов в forward-picker модалке.
 * Поддерживает мультивыбор: clickListener тоглит ID в [selectedIds].
 */
class ForwardChatPickerAdapter(
    private val getFileUrl: suspend (String) -> String?,
    private val onSelectionChanged: (selectedCount: Int) -> Unit
) : ListAdapter<GrpcManager.ChatData, ForwardChatPickerAdapter.ChatViewHolder>(DiffCallback) {

    private val selectedIds = linkedSetOf<String>()

    fun getSelectedIds(): List<String> = selectedIds.toList()

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ChatViewHolder {
        val binding = ItemChatForwardPickerBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ChatViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ChatViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class ChatViewHolder(
        private val binding: ItemChatForwardPickerBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(chat: GrpcManager.ChatData) {
            binding.chatTitle.text = chat.title
            binding.checkbox.isChecked = selectedIds.contains(chat.id)

            val avatarFileId = chat.picturePreviewFileId.ifBlank { chat.pictureFileId }
            val placeholderId = chat.id.hashCode().toLong()
            if (avatarFileId.isNotBlank()) {
                AvatarLoader.loadByFileId(
                    imageView = binding.chatAvatar,
                    placeholderView = binding.chatAvatarPlaceholder,
                    fileId = avatarFileId,
                    displayName = chat.title,
                    userId = placeholderId,
                    getUrlCallback = { getFileUrl(avatarFileId) }
                )
            } else {
                binding.chatAvatar.visibility = android.view.View.GONE
                AvatarLoader.showPlaceholder(
                    binding.chatAvatarPlaceholder,
                    chat.title,
                    placeholderId
                )
            }

            binding.root.setOnClickListener {
                if (selectedIds.contains(chat.id)) {
                    selectedIds.remove(chat.id)
                } else {
                    selectedIds.add(chat.id)
                }
                notifyItemChanged(bindingAdapterPosition)
                onSelectionChanged(selectedIds.size)
            }
        }
    }

    private object DiffCallback : DiffUtil.ItemCallback<GrpcManager.ChatData>() {
        override fun areItemsTheSame(old: GrpcManager.ChatData, new: GrpcManager.ChatData): Boolean =
            old.id == new.id
        override fun areContentsTheSame(old: GrpcManager.ChatData, new: GrpcManager.ChatData): Boolean =
            old == new
    }
}
