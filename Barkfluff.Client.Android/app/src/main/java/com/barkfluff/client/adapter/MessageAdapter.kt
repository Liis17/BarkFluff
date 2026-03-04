package com.barkfluff.client.adapter

import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.SeekBar
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import coil.size.Size
import com.barkfluff.client.MediaViewerActivity
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemAttachmentAudioBinding
import com.barkfluff.client.databinding.ItemAttachmentDocumentBinding
import com.barkfluff.client.databinding.ItemAttachmentVideoBinding
import com.barkfluff.client.databinding.ItemMessageDateSeparatorBinding
import com.barkfluff.client.databinding.ItemMessageReceivedBinding
import com.barkfluff.client.databinding.ItemMessageSentBinding
import com.barkfluff.client.utils.AudioCallbacks
import com.barkfluff.client.utils.AudioPlayerHelper
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.ImageLoadHelper
import barkfluff.shared.Shared
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.min

/**
 * Адаптер для отображения сообщений в чате с разделителями дат.
 * Поддерживает четыре типа view: отправленные сообщения, полученные сообщения,
 * разделители дат и разделитель непрочитанных сообщений.
 */
class MessageAdapter(
    private val currentUserId: Long,
    private val isGroupChat: Boolean,
    private val getFileUrl: suspend (String) -> String? = { null },
    private val downloadToCache: suspend (fileId: String, onProgress: (Int) -> Unit) -> java.io.File? = { _, _ -> null },
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main)
) : ListAdapter<MessageItem, RecyclerView.ViewHolder>(MessageDiffCallback()) {

    companion object {
        private const val VIEW_TYPE_SENT = 1
        private const val VIEW_TYPE_RECEIVED = 2
        private const val VIEW_TYPE_DATE_SEPARATOR = 3
        private const val VIEW_TYPE_UNREAD_SEPARATOR = 4
    }

    override fun getItemViewType(position: Int): Int {
        val item = getItem(position)
        return when (item.type) {
            MessageType.DATE_SEPARATOR -> VIEW_TYPE_DATE_SEPARATOR
            MessageType.UNREAD_SEPARATOR -> VIEW_TYPE_UNREAD_SEPARATOR
            MessageType.MESSAGE -> if (item.senderId == currentUserId) VIEW_TYPE_SENT else VIEW_TYPE_RECEIVED
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return when (viewType) {
            VIEW_TYPE_SENT -> SentMessageViewHolder(
                ItemMessageSentBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
            VIEW_TYPE_RECEIVED -> ReceivedMessageViewHolder(
                ItemMessageReceivedBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
            VIEW_TYPE_UNREAD_SEPARATOR -> UnreadSeparatorViewHolder(
                ItemMessageDateSeparatorBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
            else -> DateSeparatorViewHolder(
                ItemMessageDateSeparatorBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        val item = getItem(position)
        when (holder) {
            is SentMessageViewHolder -> holder.bind(item)
            is ReceivedMessageViewHolder -> holder.bind(item)
            is DateSeparatorViewHolder -> holder.bind(item)
            is UnreadSeparatorViewHolder -> holder.bind(item)
        }
    }

    // ─── Sent Message ViewHolder ───────────────────────────────────────────────

    inner class SentMessageViewHolder(
        private val binding: ItemMessageSentBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessageItem) {
            if (item.text.isNotBlank()) {
                binding.messageTextView.text = item.text
                binding.messageTextView.visibility = View.VISIBLE
            } else {
                binding.messageTextView.visibility = View.GONE
            }

            binding.timeTextView.text = formatTime(item.timestamp)

            when (item.readStatus) {
                ReadStatus.READ -> {
                    binding.readStatusImageView.setImageResource(R.drawable.ic_double_check)
                    binding.readStatusImageView.visibility = View.VISIBLE
                }
                ReadStatus.SENT -> {
                    binding.readStatusImageView.setImageResource(R.drawable.ic_check)
                    binding.readStatusImageView.visibility = View.VISIBLE
                }
                ReadStatus.NONE -> binding.readStatusImageView.visibility = View.GONE
            }

            if (item.attachments.isNotEmpty()) {
                setupAttachmentsContainer(binding.attachmentsContainer, item.attachments)
                binding.attachmentsContainer.visibility = View.VISIBLE
            } else {
                binding.attachmentsContainer.visibility = View.GONE
                binding.attachmentsContainer.removeAllViews()
            }
        }
    }

    // ─── Received Message ViewHolder ──────────────────────────────────────────

    inner class ReceivedMessageViewHolder(
        private val binding: ItemMessageReceivedBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessageItem) {
            if (isGroupChat) {
                binding.senderInfoLayout.visibility = View.VISIBLE
                binding.senderNameTextView.text = item.senderName

                if (!item.senderAvatarFileId.isNullOrBlank()) {
                    binding.senderAvatarPlaceholder.visibility = View.GONE
                    binding.senderAvatarImageView.visibility = View.VISIBLE
                    scope.launch {
                        val url = getFileUrl(item.senderAvatarFileId)
                        if (url != null) {
                            withContext(Dispatchers.Main) {
                                binding.senderAvatarImageView.load(url) {
                                    size(Size(48, 48))
                                    crossfade(true)
                                    error(R.drawable.ic_person)
                                }
                            }
                        }
                    }
                } else {
                    binding.senderAvatarImageView.visibility = View.GONE
                    binding.senderAvatarPlaceholder.visibility = View.VISIBLE
                    binding.senderAvatarPlaceholder.text = getInitials(item.senderName)
                    binding.senderAvatarPlaceholder.setBackgroundColor(getColorForName(item.senderName))
                }
            } else {
                binding.senderInfoLayout.visibility = View.GONE
            }

            if (item.text.isNotBlank()) {
                binding.messageTextView.text = item.text
                binding.messageTextView.visibility = View.VISIBLE
            } else {
                binding.messageTextView.visibility = View.GONE
            }

            binding.timeTextView.text = formatTime(item.timestamp)

            if (item.attachments.isNotEmpty()) {
                setupAttachmentsContainer(binding.attachmentsContainer, item.attachments)
                binding.attachmentsContainer.visibility = View.VISIBLE
            } else {
                binding.attachmentsContainer.visibility = View.GONE
                binding.attachmentsContainer.removeAllViews()
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
                0xFFE91E63.toInt(), 0xFF9C27B0.toInt(), 0xFF673AB7.toInt(),
                0xFF3F51B5.toInt(), 0xFF2196F3.toInt(), 0xFF03A9F4.toInt(),
                0xFF00BCD4.toInt(), 0xFF009688.toInt(), 0xFF4CAF50.toInt(),
                0xFF8BC34A.toInt(), 0xFFCDDC39.toInt(), 0xFFFFEB3B.toInt(),
                0xFFFFC107.toInt(), 0xFFFF9800.toInt(), 0xFFFF5722.toInt()
            )
            return colors[Math.abs(name.hashCode() % colors.size)]
        }
    }

    // ─── Separator ViewHolders ─────────────────────────────────────────────────

    inner class DateSeparatorViewHolder(
        private val binding: ItemMessageDateSeparatorBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: MessageItem) { binding.dateTextView.text = item.dateText }
    }

    inner class UnreadSeparatorViewHolder(
        private val binding: ItemMessageDateSeparatorBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: MessageItem) { binding.dateTextView.text = item.dateText }
    }

    // ─── Attachment Container Setup ───────────────────────────────────────────

    private fun setupAttachmentsContainer(
        container: ViewGroup,
        attachments: List<Shared.MessageAttachment>
    ) {
        container.removeAllViews()

        val images = attachments.filter {
            it.type == Shared.MessageAttachmentType.IMAGE || it.type == Shared.MessageAttachmentType.GIF
        }
        val audios = attachments.filter { it.type == Shared.MessageAttachmentType.AUDIO }
        val videos = attachments.filter { it.type == Shared.MessageAttachmentType.VIDEO }
        val docs = attachments.filter {
            it.type == Shared.MessageAttachmentType.DOCUMENT ||
            it.type == Shared.MessageAttachmentType.MESSAGE_ATTACHMENT_TYPE_UNKNOWN
        }

        val context = container.context
        val wrapper = android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            )
        }

        // Images — edge-to-edge grid
        if (images.isNotEmpty()) {
            val columnCount = min(images.size, 3)
            val rv = RecyclerView(context).apply {
                layoutParams = ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
                )
                isNestedScrollingEnabled = false
                layoutManager = GridLayoutManager(context, columnCount)
                val adapter = ImageGridAdapter(getFileUrl)
                this.adapter = adapter
                adapter.submitList(images)
            }
            wrapper.addView(rv)
        }

        // Audio rows
        for (audio in audios) {
            val audioView = inflateAudioRow(container, audio)
            wrapper.addView(audioView)
        }

        // Video rows
        for (video in videos) {
            val videoView = inflateVideoRow(container, video)
            wrapper.addView(videoView)
        }

        // Document rows
        for (doc in docs) {
            val docView = inflateDocRow(container, doc)
            wrapper.addView(docView)
        }

        container.addView(wrapper)
    }

    // ─── Audio Row ────────────────────────────────────────────────────────────

    private fun inflateAudioRow(container: ViewGroup, attachment: Shared.MessageAttachment): View {
        val binding = ItemAttachmentAudioBinding.inflate(
            LayoutInflater.from(container.context), container, false
        )
        val fileId = attachment.fileId
        val fileName = attachment.fileName.ifBlank { "audio" }

        binding.fileNameText.text = fileName

        fun updateUiForCached() {
            binding.downloadButton.visibility = View.GONE
            binding.playPauseButton.isEnabled = true
            binding.playPauseButton.alpha = 1f
            binding.audioSeekBar.isEnabled = true
        }

        fun updateUiForNotCached() {
            binding.downloadButton.visibility = View.VISIBLE
            binding.playPauseButton.isEnabled = false
            binding.playPauseButton.alpha = 0.4f
            binding.audioSeekBar.isEnabled = false
        }

        if (FileCache.hasFile(fileId)) {
            updateUiForCached()
            // Update play/pause state if this is the currently playing file
            if (AudioPlayerHelper.isActiveFile(fileId)) {
                updateAudioPlaybackUI(binding, AudioPlayerHelper.isPlaying())
                if (AudioPlayerHelper.isPlaying()) startAudioProgressPolling(fileId, binding)
            }
        } else {
            updateUiForNotCached()
        }

        // Download button
        binding.downloadButton.setOnClickListener {
            binding.downloadButton.isEnabled = false
            binding.downloadButton.alpha = 0.4f

            binding.downloadButton.tag = fileId
            scope.launch {
                // Use seekbar as progress during download
                withContext(Dispatchers.Main) {
                    binding.audioSeekBar.isEnabled = false
                    binding.audioSeekBar.max = 100
                    binding.audioSeekBar.progress = 0
                }
                val file = downloadToCache(fileId) { progress ->
                    scope.launch(Dispatchers.Main) {
                        if (binding.downloadButton.tag == fileId) {
                            binding.audioSeekBar.progress = progress
                        }
                    }
                }
                withContext(Dispatchers.Main) {
                    if (file != null) {
                        binding.audioSeekBar.max = 1000
                        binding.audioSeekBar.progress = 0
                        updateUiForCached()
                    } else {
                        binding.downloadButton.isEnabled = true
                        binding.downloadButton.alpha = 1f
                    }
                }
            }
        }

        // Play/Pause button
        binding.playPauseButton.setOnClickListener {
            val cachedFile = FileCache.getFile(fileId) ?: return@setOnClickListener
            if (AudioPlayerHelper.isActiveFile(fileId)) {
                if (AudioPlayerHelper.isPlaying()) {
                    AudioPlayerHelper.pause()
                    updateAudioPlaybackUI(binding, false)
                } else {
                    AudioPlayerHelper.resume()
                    updateAudioPlaybackUI(binding, true)
                    startAudioProgressPolling(fileId, binding)
                }
            } else {
                AudioPlayerHelper.play(fileId, cachedFile, object : AudioCallbacks {
                    override fun onStateChanged(isPlaying: Boolean) {
                        updateAudioPlaybackUI(binding, isPlaying)
                        if (isPlaying) startAudioProgressPolling(fileId, binding)
                    }
                    override fun onProgress(positionMs: Int, durationMs: Int) {}
                    override fun onComplete() {
                        updateAudioPlaybackUI(binding, false)
                        binding.audioSeekBar.progress = 0
                        binding.durationText.text = formatAudioTime(
                            AudioPlayerHelper.getDuration().toLong()
                        )
                    }
                    override fun onError() {
                        updateAudioPlaybackUI(binding, false)
                    }
                })
            }
        }

        // SeekBar drag
        binding.audioSeekBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar, progress: Int, fromUser: Boolean) {
                if (fromUser && AudioPlayerHelper.isActiveFile(fileId)) {
                    val duration = AudioPlayerHelper.getDuration()
                    if (duration > 0) {
                        AudioPlayerHelper.seekTo((progress.toLong() * duration / 1000L).toInt())
                    }
                }
            }
            override fun onStartTrackingTouch(seekBar: SeekBar) {}
            override fun onStopTrackingTouch(seekBar: SeekBar) {}
        })

        return binding.root
    }

    private fun updateAudioPlaybackUI(
        binding: ItemAttachmentAudioBinding,
        isPlaying: Boolean
    ) {
        binding.playPauseButton.setImageResource(
            if (isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow
        )
    }

    private fun startAudioProgressPolling(fileId: String, binding: ItemAttachmentAudioBinding) {
        val handler = Handler(Looper.getMainLooper())
        val runnable = object : Runnable {
            override fun run() {
                if (!AudioPlayerHelper.isActiveFile(fileId)) return
                if (!AudioPlayerHelper.isPlaying()) return
                val pos = AudioPlayerHelper.getCurrentPosition()
                val dur = AudioPlayerHelper.getDuration()
                if (dur > 0) {
                    binding.audioSeekBar.progress = (pos.toLong() * 1000L / dur).toInt()
                    binding.durationText.text = "${formatAudioTime(pos.toLong())} / ${formatAudioTime(dur.toLong())}"
                }
                handler.postDelayed(this, 250)
            }
        }
        handler.post(runnable)
    }

    // ─── Video Row ────────────────────────────────────────────────────────────

    private fun inflateVideoRow(container: ViewGroup, attachment: Shared.MessageAttachment): View {
        val binding = ItemAttachmentVideoBinding.inflate(
            LayoutInflater.from(container.context), container, false
        )
        val fileId = attachment.fileId
        val fileName = attachment.fileName.ifBlank { "video" }

        // Load thumbnail
        val thumbnailFileId = attachment.previewFileId.ifBlank { "" }
        val thumbnailUrl = attachment.previewUrl

        if (thumbnailUrl.isNotBlank()) {
            ImageLoadHelper.loadByFileId(
                imageView = binding.videoThumbnail,
                fileId = thumbnailFileId.ifBlank { fileId },
                getUrlCallback = { thumbnailUrl },
                onError = { binding.videoThumbnail.setImageResource(R.drawable.ic_image_placeholder) }
            )
        } else if (thumbnailFileId.isNotBlank()) {
            ImageLoadHelper.loadByFileId(
                imageView = binding.videoThumbnail,
                fileId = thumbnailFileId,
                getUrlCallback = { getFileUrl(thumbnailFileId) },
                onError = { binding.videoThumbnail.setImageResource(R.drawable.ic_image_placeholder) }
            )
        }

        if (FileCache.hasFile(fileId)) {
            binding.videoDownloadButton.visibility = View.GONE
            binding.videoPlayButton.alpha = 1f
            binding.videoPlayButton.isEnabled = true
        } else {
            binding.videoDownloadButton.visibility = View.VISIBLE
            binding.videoPlayButton.alpha = 0.4f
            binding.videoPlayButton.isEnabled = false
        }

        // Download button
        binding.videoDownloadButton.setOnClickListener {
            binding.videoDownloadButton.visibility = View.GONE
            binding.videoDownloadProgress.visibility = View.VISIBLE

            scope.launch {
                val file = downloadToCache(fileId) { _ -> }
                withContext(Dispatchers.Main) {
                    binding.videoDownloadProgress.visibility = View.GONE
                    if (file != null) {
                        binding.videoPlayButton.alpha = 1f
                        binding.videoPlayButton.isEnabled = true
                    } else {
                        binding.videoDownloadButton.visibility = View.VISIBLE
                    }
                }
            }
        }

        // Play button
        binding.videoPlayButton.setOnClickListener {
            val cachedPath = FileCache.getFile(fileId)?.absolutePath
            val intent = MediaViewerActivity.createIntent(
                binding.root.context, fileId, fileName, cachedPath
            )
            binding.root.context.startActivity(intent)
        }

        return binding.root
    }

    // ─── Document Row ─────────────────────────────────────────────────────────

    private fun inflateDocRow(container: ViewGroup, attachment: Shared.MessageAttachment): View {
        val binding = ItemAttachmentDocumentBinding.inflate(
            LayoutInflater.from(container.context), container, false
        )
        binding.docFileName.text = attachment.fileName.ifBlank { "file" }
        binding.docFileSize.text = formatFileSize(attachment.attachmentSize)

        binding.docDownloadButton.setOnClickListener {
            binding.docDownloadButton.isEnabled = false
            scope.launch {
                downloadToCache(attachment.fileId) { _ -> }
                withContext(Dispatchers.Main) {
                    binding.docDownloadButton.isEnabled = true
                }
            }
        }

        return binding.root
    }

    // ─── Formatting Helpers ───────────────────────────────────────────────────

    private fun formatTime(timestampMillis: Long): String {
        if (timestampMillis <= 0) return ""
        return SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(timestampMillis))
    }

    private fun formatAudioTime(ms: Long): String {
        if (ms <= 0) return "0:00"
        val totalSec = ms / 1000
        val min = totalSec / 60
        val sec = totalSec % 60
        return "%d:%02d".format(min, sec)
    }

    private fun formatFileSize(bytes: Long): String {
        return when {
            bytes <= 0 -> ""
            bytes < 1024 -> "$bytes B"
            bytes < 1024 * 1024 -> "%.1f KB".format(bytes / 1024f)
            bytes < 1024 * 1024 * 1024 -> "%.1f MB".format(bytes / (1024f * 1024f))
            else -> "%.1f GB".format(bytes / (1024f * 1024f * 1024f))
        }
    }

    // ─── DiffCallback ─────────────────────────────────────────────────────────

    class MessageDiffCallback : DiffUtil.ItemCallback<MessageItem>() {
        override fun areItemsTheSame(oldItem: MessageItem, newItem: MessageItem): Boolean {
            if (oldItem.type != newItem.type) return false
            if (oldItem.type == MessageType.UNREAD_SEPARATOR) return true
            return oldItem.messageId == newItem.messageId
        }
        override fun areContentsTheSame(oldItem: MessageItem, newItem: MessageItem) = oldItem == newItem
    }
}

// ─── Data Classes & Enums ─────────────────────────────────────────────────────

enum class MessageType { MESSAGE, DATE_SEPARATOR, UNREAD_SEPARATOR }

data class MessageItem(
    val messageId: Long,
    val senderId: Long,
    val senderName: String? = null,
    val senderAvatarFileId: String? = null,
    val text: String,
    val timestamp: Long,
    val attachments: List<Shared.MessageAttachment>,
    val readStatus: ReadStatus = ReadStatus.NONE,
    val type: MessageType = MessageType.MESSAGE,
    val dateText: String = ""
) {
    companion object {
        fun createDateSeparator(dateText: String) = MessageItem(
            messageId = 0, senderId = 0, text = "", timestamp = 0,
            attachments = emptyList(), type = MessageType.DATE_SEPARATOR, dateText = dateText
        )

        fun createUnreadSeparator() = MessageItem(
            messageId = -2, senderId = 0, text = "", timestamp = 0,
            attachments = emptyList(), type = MessageType.UNREAD_SEPARATOR,
            dateText = "Непрочитанные сообщения"
        )
    }
}

enum class ReadStatus { NONE, SENT, READ }
