package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

class ChatAdapter(
    private val onChatClick: (GrpcManager.ChatData) -> Unit,
    private val getFileUrl: suspend (String) -> String?
) : ListAdapter<ChatAdapter.ChatDisplayItem, ChatAdapter.ChatViewHolder>(ChatDiffCallback()) {

    var currentUserId: Long = 0

    data class ChatDisplayItem(
        val chatData: GrpcManager.ChatData,
        val displayTitle: String,
        val displayAvatarFileId: String?,
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
            binding.chatTitle.text = item.displayTitle.trim()

            // Аватар через AvatarLoader с fileId
            AvatarLoader.loadByFileId(
                imageView = binding.chatAvatar,
                placeholderView = binding.chatAvatarPlaceholder,
                fileId = item.displayAvatarFileId,
                displayName = item.displayTitle,
                userId = item.otherUserId.takeIf { it != 0L }
                    ?: chat.id.hashCode().toLong(),
                size = 128
            ) {
                // Callback для получения URL по fileId
                getFileUrl(item.displayAvatarFileId ?: "")
            }

            // Последнее сообщение
            if (chat.lastMessage != null) {
                val text = chat.lastMessage.text
                binding.lastMessage.text = when {
                    text.isNotBlank() -> text.trim()
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
                binding.unreadBadgeCard.visibility = View.VISIBLE
                binding.unreadBadge.text = when {
                    chat.countUnread > 99 -> "99+"
                    else -> chat.countUnread.toString()
                }
            } else {
                binding.unreadBadgeCard.visibility = View.GONE
            }

            // Статус прочтения (галочки)
            val lastMsg = chat.lastMessage
            if (lastMsg != null && lastMsg.senderId == currentUserId) {
                val readByOthers = lastMsg.readBy.any { it != currentUserId }
                if (readByOthers) {
                    binding.readStatus.setImageResource(R.drawable.ic_double_check)
                } else {
                    binding.readStatus.setImageResource(R.drawable.ic_check)
                }
                binding.readStatus.visibility = View.VISIBLE
            } else {
                binding.readStatus.visibility = View.GONE
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

    /**
     * Обновляет чат при получении нового сообщения: перемещает наверх, обновляет lastMessage, увеличивает счётчик непрочитанных.
     */
    fun updateChatWithNewMessage(chatId: String, senderId: Long, messageId: Long, text: String, sentAt: Long, currentUserId: Long): Boolean {
        val list = currentList.toMutableList()
        val index = list.indexOfFirst { it.chatData.id == chatId }
        if (index < 0) return false

        val item = list[index]
        val newLastMessage = GrpcManager.LastMessageData(
            id = messageId,
            senderId = senderId,
            text = text,
            sentAt = sentAt,
            readBy = listOf(senderId)
        )
        val newUnread = if (senderId != currentUserId) item.chatData.countUnread + 1 else item.chatData.countUnread
        val updatedChat = item.chatData.copy(lastMessage = newLastMessage, countUnread = newUnread)
        val updatedItem = item.copy(chatData = updatedChat)

        list.removeAt(index)
        list.add(0, updatedItem)
        submitList(list)
        return true
    }

    /**
     * Добавляет новый чат в начало списка.
     */
    fun addNewChat(item: ChatDisplayItem) {
        val list = currentList.toMutableList()
        list.add(0, item)
        submitList(list)
    }

    /**
     * Обновляет статус прочтения сообщения в чате.
     */
    fun updateReadStatus(chatId: String, messageId: Long, newReadBy: List<Long>, currentUserId: Long) {
        val list = currentList.toMutableList()
        val index = list.indexOfFirst { it.chatData.id == chatId }
        if (index < 0) return

        val item = list[index]
        val lastMsg = item.chatData.lastMessage ?: return

        // Если текущий пользователь прочитал — обнуляем счётчик непрочитанных
        val newUnread = if (newReadBy.contains(currentUserId)) 0L else item.chatData.countUnread
        val updatedLastMessage = if (lastMsg.id == messageId) {
            lastMsg.copy(readBy = newReadBy)
        } else {
            lastMsg
        }
        val updatedChat = item.chatData.copy(lastMessage = updatedLastMessage, countUnread = newUnread)
        val updatedItem = item.copy(chatData = updatedChat)

        list[index] = updatedItem
        submitList(list)
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
