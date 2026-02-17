package com.barkfluff.android.ui.chatlist

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import coil.transform.CircleCropTransformation
import com.barkfluff.android.R
import com.barkfluff.android.data.model.ChatItem
import com.barkfluff.android.databinding.ItemChatBinding
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit

class ChatListAdapter : ListAdapter<ChatItem, ChatListAdapter.ViewHolder>(DiffCallback) {

    inner class ViewHolder(private val binding: ItemChatBinding) :
        RecyclerView.ViewHolder(binding.root) {

        fun bind(chat: ChatItem) {
            binding.tvChatTitle.text = chat.title
            binding.tvLastMessage.text = chat.lastMessageText ?: "Нет сообщений"

            if (chat.unreadCount > 0) {
                binding.tvUnreadCount.visibility = View.VISIBLE
                binding.tvUnreadCount.text = if (chat.unreadCount > 99) "99+" else chat.unreadCount.toString()
            } else {
                binding.tvUnreadCount.visibility = View.GONE
            }

            binding.tvTimestamp.text = chat.lastMessageTime?.let { formatTime(it) } ?: ""

            if (!chat.picture.isNullOrEmpty()) {
                binding.ivAvatar.load(chat.picture) {
                    transformations(CircleCropTransformation())
                    placeholder(R.drawable.ic_default_avatar)
                    error(R.drawable.ic_default_avatar)
                }
            } else {
                binding.ivAvatar.setImageResource(R.drawable.ic_default_avatar)
            }
        }

        private fun formatTime(instant: Instant): String {
            val zone = ZoneId.systemDefault()
            val dateTime = LocalDateTime.ofInstant(instant, zone)
            val today = LocalDate.now(zone)
            val days = ChronoUnit.DAYS.between(dateTime.toLocalDate(), today)
            return when {
                days == 0L -> dateTime.format(DateTimeFormatter.ofPattern("HH:mm"))
                days < 7L -> dateTime.format(DateTimeFormatter.ofPattern("EEE"))
                else -> dateTime.format(DateTimeFormatter.ofPattern("dd/MM/yy"))
            }
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemChatBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    companion object DiffCallback : DiffUtil.ItemCallback<ChatItem>() {
        override fun areItemsTheSame(oldItem: ChatItem, newItem: ChatItem): Boolean =
            oldItem.id == newItem.id

        override fun areContentsTheSame(oldItem: ChatItem, newItem: ChatItem): Boolean =
            oldItem == newItem
    }
}
