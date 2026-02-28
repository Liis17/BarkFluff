package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import coil.size.Size
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemAttachmentBinding
import com.barkfluff.client.databinding.ItemMessageReceivedBinding
import com.barkfluff.client.databinding.ItemMessageSentBinding
import barkfluff.shared.Shared
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Адаптер для отображения сообщений в чате.
 * Поддерживает два типа view: отправленные сообщения и полученные.
 */
class MessageAdapter(
    private val currentUserId: Long,
    private val isGroupChat: Boolean,
    private val getFileUrl: (String) -> String? = { null },
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main)
) : ListAdapter<MessageItem, RecyclerView.ViewHolder>(MessageDiffCallback()) {

    companion object {
        private const val VIEW_TYPE_SENT = 1
        private const val VIEW_TYPE_RECEIVED = 2
    }

    override fun getItemViewType(position: Int): Int {
        val item = getItem(position)
        return if (item.senderId == currentUserId) {
            VIEW_TYPE_SENT
        } else {
            VIEW_TYPE_RECEIVED
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return when (viewType) {
            VIEW_TYPE_SENT -> {
                val binding = ItemMessageSentBinding.inflate(
                    LayoutInflater.from(parent.context), parent, false
                )
                SentMessageViewHolder(binding)
            }
            else -> {
                val binding = ItemMessageReceivedBinding.inflate(
                    LayoutInflater.from(parent.context), parent, false
                )
                ReceivedMessageViewHolder(binding)
            }
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        val item = getItem(position)
        when (holder) {
            is SentMessageViewHolder -> holder.bind(item)
            is ReceivedMessageViewHolder -> holder.bind(item)
        }
    }

    inner class SentMessageViewHolder(
        private val binding: ItemMessageSentBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessageItem) {
            // Текст сообщения
            if (item.text.isNotBlank()) {
                binding.messageTextView.text = item.text
                binding.messageTextView.visibility = View.VISIBLE
            } else {
                binding.messageTextView.visibility = View.GONE
            }

            // Время
            binding.timeTextView.text = formatTime(item.timestamp)

            // Статус прочтения (только для своих сообщений)
            when (item.readStatus) {
                ReadStatus.READ -> {
                    binding.readStatusImageView.setImageResource(R.drawable.ic_double_check)
                    binding.readStatusImageView.visibility = View.VISIBLE
                }
                ReadStatus.SENT -> {
                    binding.readStatusImageView.setImageResource(R.drawable.ic_check)
                    binding.readStatusImageView.visibility = View.VISIBLE
                }
                ReadStatus.NONE -> {
                    binding.readStatusImageView.visibility = View.GONE
                }
            }

            // Вложения
            if (item.attachments.isNotEmpty()) {
                setupAttachmentsRecyclerView(binding.attachmentsRecyclerView, item.attachments)
                binding.attachmentsRecyclerView.visibility = View.VISIBLE
            } else {
                binding.attachmentsRecyclerView.visibility = View.GONE
            }
        }
    }

    inner class ReceivedMessageViewHolder(
        private val binding: ItemMessageReceivedBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessageItem) {
            // Информация об отправителе (только для групповых чатов)
            if (isGroupChat) {
                binding.senderInfoLayout.visibility = View.VISIBLE
                binding.senderNameTextView.text = item.senderName

                // Аватар отправителя
                if (!item.senderAvatarFileId.isNullOrBlank()) {
                    binding.senderAvatarPlaceholder.visibility = View.GONE
                    binding.senderAvatarImageView.visibility = View.VISIBLE
                    val url = getFileUrl(item.senderAvatarFileId)
                    if (url != null) {
                        binding.senderAvatarImageView.load(url) {
                            size(Size(48, 48))
                            crossfade(true)
                            error(R.drawable.ic_person)
                        }
                    }
                } else {
                    binding.senderAvatarImageView.visibility = View.GONE
                    binding.senderAvatarPlaceholder.visibility = View.VISIBLE
                    binding.senderAvatarPlaceholder.text = getInitials(item.senderName)
                    binding.senderAvatarPlaceholder.setBackgroundColor(
                        getColorForName(item.senderName)
                    )
                }
            } else {
                binding.senderInfoLayout.visibility = View.GONE
            }

            // Текст сообщения
            if (item.text.isNotBlank()) {
                binding.messageTextView.text = item.text
                binding.messageTextView.visibility = View.VISIBLE
            } else {
                binding.messageTextView.visibility = View.GONE
            }

            // Время
            binding.timeTextView.text = formatTime(item.timestamp)

            // Вложения
            if (item.attachments.isNotEmpty()) {
                setupAttachmentsRecyclerView(binding.attachmentsRecyclerView, item.attachments)
                binding.attachmentsRecyclerView.visibility = View.VISIBLE
            } else {
                binding.attachmentsRecyclerView.visibility = View.GONE
            }
        }

        private fun getInitials(name: String?): String {
            if (name.isNullOrBlank()) return "?"
            val parts = name.trim().split("\\s+".toRegex())
            return when {
                parts.size >= 2 -> "${parts[0].first()}${parts[1].first()}".uppercase()
                parts.size == 1 -> parts[0].first().uppercase()
                else -> "?"
            }
        }

        private fun getColorForName(name: String?): Int {
            if (name.isNullOrBlank()) return 0xFF6200EE.toInt()
            val colors = listOf(
                0xFFE91E63.toInt(),
                0xFF9C27B0.toInt(),
                0xFF673AB7.toInt(),
                0xFF3F51B5.toInt(),
                0xFF2196F3.toInt(),
                0xFF03A9F4.toInt(),
                0xFF00BCD4.toInt(),
                0xFF009688.toInt(),
                0xFF4CAF50.toInt(),
                0xFF8BC34A.toInt(),
                0xFFCDDC39.toInt(),
                0xFFFFEB3B.toInt(),
                0xFFFFC107.toInt(),
                0xFFFF9800.toInt(),
                0xFFFF5722.toInt()
            )
            val index = Math.abs(name.hashCode() % colors.size)
            return colors[index]
        }
    }

    private fun setupAttachmentsRecyclerView(
        recyclerView: androidx.recyclerview.widget.RecyclerView,
        attachments: List<Shared.MessageAttachment>
    ) {
        val context = recyclerView.context
        val adapter = (recyclerView.adapter as? AttachmentAdapter) ?: AttachmentAdapter(getFileUrl, scope).also {
            recyclerView.adapter = it
            recyclerView.layoutManager = androidx.recyclerview.widget.LinearLayoutManager(
                context,
                androidx.recyclerview.widget.LinearLayoutManager.HORIZONTAL,
                false
            )
        }
        adapter.submitList(attachments)
    }

    private fun formatTime(timestampMillis: Long): String {
        if (timestampMillis <= 0) return ""
        val date = Date(timestampMillis)
        return SimpleDateFormat("HH:mm", Locale.getDefault()).format(date)
    }

    class MessageDiffCallback : DiffUtil.ItemCallback<MessageItem>() {
        override fun areItemsTheSame(oldItem: MessageItem, newItem: MessageItem): Boolean {
            return oldItem.messageId == newItem.messageId
        }

        override fun areContentsTheSame(oldItem: MessageItem, newItem: MessageItem): Boolean {
            return oldItem == newItem
        }
    }
}

/**
 * Элемент сообщения для отображения в адаптере.
 */
data class MessageItem(
    val messageId: Long,
    val senderId: Long,
    val senderName: String? = null,
    val senderAvatarFileId: String? = null,
    val text: String,
    val timestamp: Long,
    val attachments: List<Shared.MessageAttachment>,
    val readStatus: ReadStatus = ReadStatus.NONE
)

/**
 * Статус прочтения сообщения.
 */
enum class ReadStatus {
    NONE,    // Не отправлено / нет статуса
    SENT,    // Отправлено (одна галочка)
    READ     // Прочитано (две галочки)
}

/**
 * Адаптер для вложений (изображений) в сообщении.
 */
class AttachmentAdapter(
    private val getFileUrl: (String) -> String?,
    private val scope: CoroutineScope
) : ListAdapter<Shared.MessageAttachment, AttachmentAdapter.AttachmentViewHolder>(AttachmentDiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): AttachmentViewHolder {
        val binding = ItemAttachmentBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return AttachmentViewHolder(binding)
    }

    override fun onBindViewHolder(holder: AttachmentViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class AttachmentViewHolder(
        private val binding: ItemAttachmentBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(attachment: Shared.MessageAttachment) {
            binding.loadingProgress.visibility = View.VISIBLE
            binding.attachmentImageView.visibility = View.GONE

            val fileId = attachment.previewFileId.ifBlank { attachment.fileId }

            // Загружаем превью асинхронно
            scope.launch {
                val url = getFileUrl(fileId)
                if (url != null) {
                    withContext(Dispatchers.Main) {
                        binding.attachmentImageView.load(url) {
                            crossfade(true)
                            error(R.drawable.ic_image_placeholder)
                            listener(
                                onSuccess = { _, _ ->
                                    binding.loadingProgress.visibility = View.GONE
                                    binding.attachmentImageView.visibility = View.VISIBLE
                                },
                                onError = { _, _ ->
                                    binding.loadingProgress.visibility = View.GONE
                                    binding.attachmentImageView.visibility = View.VISIBLE
                                    binding.attachmentImageView.setImageResource(R.drawable.ic_image_placeholder)
                                }
                            )
                        }
                    }
                } else {
                    withContext(Dispatchers.Main) {
                        binding.loadingProgress.visibility = View.GONE
                        binding.attachmentImageView.visibility = View.VISIBLE
                        binding.attachmentImageView.setImageResource(R.drawable.ic_image_placeholder)
                    }
                }
            }
        }
    }

    class AttachmentDiffCallback : DiffUtil.ItemCallback<Shared.MessageAttachment>() {
        override fun areItemsTheSame(oldItem: Shared.MessageAttachment, newItem: Shared.MessageAttachment): Boolean {
            return oldItem.id == newItem.id
        }

        override fun areContentsTheSame(oldItem: Shared.MessageAttachment, newItem: Shared.MessageAttachment): Boolean {
            return oldItem == newItem
        }
    }
}
