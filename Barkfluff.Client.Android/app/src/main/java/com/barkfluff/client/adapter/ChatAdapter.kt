package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

class ChatAdapter(
    private val onChatClick: (GrpcManager.ChatData) -> Unit
) : ListAdapter<ChatAdapter.ChatDisplayItem, ChatAdapter.ChatViewHolder>(ChatDiffCallback()) {

    data class ChatDisplayItem(
        val chatData: GrpcManager.ChatData,
        val displayTitle: String,
        val displayAvatarUrl: String?,
        val otherUserId: Long = 0
    )

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ChatViewHolder {
        val binding = ItemChatBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return ChatViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ChatViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class ChatViewHolder(
        private val binding: ItemChatBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: ChatDisplayItem) {
            val chat = item.chatData

            // Название чата
            binding.chatTitle.text = item.displayTitle

            // Аватар через AvatarLoader
            AvatarLoader.load(
                imageView = binding.chatAvatar,
                placeholderView = binding.chatAvatarPlaceholder,
                avatarUrl = item.displayAvatarUrl,
                displayName = item.displayTitle,
                userId = item.otherUserId.takeIf { it != 0L }
                    ?: chat.id.hashCode().toLong()
            )

            // Последнее сообщение
            if (chat.lastMessage != null) {
                val text = chat.lastMessage.text
                binding.lastMessage.text = when {
                    text.isNotBlank() -> text
                    else -> "Вложение"
                }
                binding.lastMessage.visibility = View.VISIBLE
                binding.messageTime.text = formatTime(chat.lastMessage.sentAt)
                binding.messageTime.visibility = View.VISIBLE
            } else {
                binding.lastMessage.text = "Нет сообщений"
                binding.lastMessage.visibility = View.VISIBLE
                binding.messageTime.visibility = View.GONE
            }

            // Непрочитанные
            if (chat.countUnread > 0) {
                binding.unreadBadge.visibility = View.VISIBLE
                binding.unreadBadge.text = when {
                    chat.countUnread > 99 -> "99+"
                    else -> chat.countUnread.toString()
                }
            } else {
                binding.unreadBadge.visibility = View.GONE
            }

            binding.root.setOnClickListener {
                onChatClick(chat)
            }
        }

        private fun formatTime(timestampMillis: Long): String {
            if (timestampMillis <= 0) return ""

            val date = Date(timestampMillis)
            val now = Calendar.getInstance()
            val messageDate = Calendar.getInstance().apply { time = date }

            return when {
                // Сегодня — показываем время
                now.get(Calendar.YEAR) == messageDate.get(Calendar.YEAR) &&
                    now.get(Calendar.DAY_OF_YEAR) == messageDate.get(Calendar.DAY_OF_YEAR) -> {
                    SimpleDateFormat("HH:mm", Locale.getDefault()).format(date)
                }
                // Вчера
                now.get(Calendar.YEAR) == messageDate.get(Calendar.YEAR) &&
                    now.get(Calendar.DAY_OF_YEAR) - messageDate.get(Calendar.DAY_OF_YEAR) == 1 -> {
                    "Вчера"
                }
                // На этой неделе — день недели
                now.get(Calendar.YEAR) == messageDate.get(Calendar.YEAR) &&
                    now.get(Calendar.WEEK_OF_YEAR) == messageDate.get(Calendar.WEEK_OF_YEAR) -> {
                    SimpleDateFormat("EE", Locale("ru")).format(date)
                }
                // Старше — дата
                else -> {
                    SimpleDateFormat("dd.MM.yy", Locale.getDefault()).format(date)
                }
            }
        }
    }

    class ChatDiffCallback : DiffUtil.ItemCallback<ChatDisplayItem>() {
        override fun areItemsTheSame(oldItem: ChatDisplayItem, newItem: ChatDisplayItem): Boolean {
            return oldItem.chatData.id == newItem.chatData.id
        }

        override fun areContentsTheSame(oldItem: ChatDisplayItem, newItem: ChatDisplayItem): Boolean {
            return oldItem == newItem
        }
    }
}
