package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import barkfluff.shared.Shared
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.MarkdownRenderer
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

class ChatAdapter(
    private val onChatClick: (GrpcManager.ChatData) -> Unit,
    private val getFileUrl: suspend (String) -> String?
) : ListAdapter<ChatAdapter.ChatDisplayItem, RecyclerView.ViewHolder>(ChatDiffCallback()) {

    var currentUserId: Long = 0

    data class ChatDisplayItem(
        val chatData: GrpcManager.ChatData,
        val displayTitle: String,
        val displayAvatarFileId: String?,
        val otherUserId: Long = 0,
        val isFooter: Boolean = false
    )

    companion object {
        private const val VIEW_TYPE_CHAT = 0
        private const val VIEW_TYPE_FOOTER = 1

        /** Прозрачный спейсер, всегда находящийся в конце списка. */
        private val FOOTER_ITEM = ChatDisplayItem(
            chatData = GrpcManager.ChatData(
                id = "__footer__",
                title = "",
                picture = "",
                isGroupChat = false,
                lastMessage = null,
                memberIds = emptyList(),
                countUnread = 0,
                firstUnreadMessageId = 0
            ),
            displayTitle = "",
            displayAvatarFileId = null,
            isFooter = true
        )
    }

    /**
     * Удаляет все footer-элементы из списка (в любом месте) и добавляет один в конец.
     * Вызывается перед каждым submitList, чтобы footer всегда был в самом низу.
     */
    private fun MutableList<ChatDisplayItem>.ensureFooter() {
        removeAll { it.isFooter }
        add(FOOTER_ITEM)
    }

    /** Переопределяем submitList, чтобы footer автоматически добавлялся/фиксировался в конце. */
    override fun submitList(list: List<ChatDisplayItem>?) {
        val mutable = (list ?: emptyList()).toMutableList().also { it.ensureFooter() }
        super.submitList(mutable)
    }

    override fun getItemViewType(position: Int): Int {
        return if (getItem(position).isFooter) VIEW_TYPE_FOOTER else VIEW_TYPE_CHAT
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return if (viewType == VIEW_TYPE_FOOTER) {
            val view = LayoutInflater.from(parent.context).inflate(R.layout.item_chat_footer, parent, false)
            FooterViewHolder(view)
        } else {
            val binding = ItemChatBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            ChatViewHolder(binding)
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        if (holder is ChatViewHolder) {
            holder.bind(getItem(position))
        }
    }

    /** ViewHolder для прозрачного спейсера — не требует биндинга. */
    class FooterViewHolder(view: View) : RecyclerView.ViewHolder(view)

    inner class ChatViewHolder(
        private val binding: ItemChatBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: ChatDisplayItem) {
            val chat = item.chatData

            // Название чата
            binding.chatTitle.text = item.displayTitle.trim()
            val isPrivateChat = chat.chatType == Shared.ChatType.CHAT_TYPE_PRIVATE
            binding.privateChatLock.visibility = if (isPrivateChat) View.VISIBLE else View.GONE

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
            val lastMessage = chat.lastMessage
            binding.lastMessage.setTextColor(
                com.google.android.material.color.MaterialColors.getColor(binding.lastMessage, com.google.android.material.R.attr.colorOnSurfaceVariant)
            )
            if (isPrivateChat) {
                if (chat.privateInviteState != Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_ACCEPTED) {
                    // Запрос на приватный чат: вместо скелетона — статус инвайта.
                    val isInvitee = chat.privateInviterUserId != 0L && chat.privateInviterUserId != currentUserId
                    binding.privatePreviewSkeleton.visibility = View.GONE
                    binding.lastMessage.text = binding.root.context.getString(
                        when {
                            chat.privateInviteState == Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_REJECTED ->
                                R.string.private_chat_invite_rejected
                            isInvitee -> R.string.private_chat_invite_incoming
                            else -> R.string.private_chat_invite_waiting
                        }
                    )
                    binding.lastMessage.visibility = View.VISIBLE
                } else {
                    binding.lastMessage.visibility = View.GONE
                    binding.privatePreviewSkeleton.visibility = View.VISIBLE
                }
                binding.messageTime.text = formatTime(chat.lastActivityAt)
                binding.messageTime.visibility = if (chat.lastActivityAt > 0) View.VISIBLE else View.GONE
            } else if (chat.hasDraft) {
                binding.privatePreviewSkeleton.visibility = View.GONE
                binding.lastMessage.setText(R.string.chat_draft_preview)
                binding.lastMessage.setTextColor(
                    com.google.android.material.color.MaterialColors.getColor(binding.lastMessage, androidx.appcompat.R.attr.colorPrimary)
                )
                binding.lastMessage.visibility = View.VISIBLE
                binding.messageTime.visibility = View.GONE
            } else if (lastMessage != null) {
                binding.privatePreviewSkeleton.visibility = View.GONE
                val text = lastMessage.text
                binding.lastMessage.text = when {
                    text.isNotBlank() -> MarkdownRenderer.strip(text)
                    else -> "Вложение"
                }
                binding.lastMessage.setTextColor(
                    com.google.android.material.color.MaterialColors.getColor(binding.lastMessage, com.google.android.material.R.attr.colorOnSurfaceVariant)
                )
                binding.lastMessage.visibility = View.VISIBLE
                binding.messageTime.text = formatTime(lastMessage.sentAt)
                binding.messageTime.visibility = View.VISIBLE
            } else {
                binding.privatePreviewSkeleton.visibility = View.GONE
                binding.lastMessage.text = "Нет сообщений"
                binding.lastMessage.setTextColor(
                    com.google.android.material.color.MaterialColors.getColor(binding.lastMessage, com.google.android.material.R.attr.colorOnSurfaceVariant)
                )
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
            if (!isPrivateChat && !chat.hasDraft && lastMsg != null && lastMsg.senderId == currentUserId) {
                val readByOthers = lastMsg.readBy.any { it != currentUserId }
                if (readByOthers) {
                    binding.readStatus.setImageResource(R.drawable.ic_status_read)
                } else {
                    binding.readStatus.setImageResource(R.drawable.ic_status_sent)
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
        val index = list.indexOfFirst { !it.isFooter && it.chatData.id == chatId }
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
        val index = list.indexOfFirst { !it.isFooter && it.chatData.id == chatId }
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
            if (oldItem.isFooter && newItem.isFooter) return true
            if (oldItem.isFooter || newItem.isFooter) return false
            return oldItem.chatData.id == newItem.chatData.id
        }

        override fun areContentsTheSame(oldItem: ChatDisplayItem, newItem: ChatDisplayItem): Boolean {
            if (oldItem.isFooter && newItem.isFooter) return true
            return oldItem == newItem
        }
    }
}
