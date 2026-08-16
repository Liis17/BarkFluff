package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import barkfluff.messages.MessagesApiOuterClass
import barkfluff.shared.Shared
import coil.load
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemAttachmentFileBinding
import com.barkfluff.client.databinding.ItemAttachmentPreviewBinding
import com.barkfluff.client.databinding.ItemProfileVoiceBinding
import com.barkfluff.client.utils.AudioCallbacks
import com.barkfluff.client.utils.AudioPlayerHelper
import com.barkfluff.client.utils.FileMediaUrl
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Адаптер для отображения вложений в профиле чата.
 * VIEW_TYPE_MEDIA — квадратная сетка (3 колонки) для фото и видео.
 * VIEW_TYPE_FILE  — вертикальный список с иконкой, именем и размером для файлов.
 * VIEW_TYPE_AUDIO — строка голосового с плеем и длительностью (таб «Голосовые»).
 */
class AttachmentPreviewAdapter(
    private val getFileUrl: suspend (String) -> String?,
    private val onAttachmentClick: (MessagesApiOuterClass.ChatAttachmentInfo) -> Unit,
    private val downloadToCache: (suspend (String) -> File?)? = null,
    private val scope: CoroutineScope? = null
) : ListAdapter<MessagesApiOuterClass.ChatAttachmentInfo, RecyclerView.ViewHolder>(DiffCallback()) {

    companion object {
        private const val VIEW_TYPE_MEDIA = 0
        private const val VIEW_TYPE_FILE = 1
        private const val VIEW_TYPE_AUDIO = 2
    }

    override fun getItemViewType(position: Int): Int {
        return when (getItem(position).attachment.type) {
            Shared.MessageAttachmentType.IMAGE,
            Shared.MessageAttachmentType.GIF,
            Shared.MessageAttachmentType.VIDEO,
            Shared.MessageAttachmentType.STICKER -> VIEW_TYPE_MEDIA
            Shared.MessageAttachmentType.AUDIO,
            Shared.MessageAttachmentType.VOICE -> VIEW_TYPE_AUDIO
            else -> VIEW_TYPE_FILE
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return when (viewType) {
            VIEW_TYPE_FILE -> {
                val binding = ItemAttachmentFileBinding.inflate(
                    LayoutInflater.from(parent.context), parent, false
                )
                FileViewHolder(binding)
            }
            VIEW_TYPE_AUDIO -> {
                val binding = ItemProfileVoiceBinding.inflate(
                    LayoutInflater.from(parent.context), parent, false
                )
                AudioViewHolder(binding)
            }
            else -> {
                val binding = ItemAttachmentPreviewBinding.inflate(
                    LayoutInflater.from(parent.context), parent, false
                )
                MediaViewHolder(binding)
            }
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        val item = getItem(position)
        when (holder) {
            is MediaViewHolder -> holder.bind(item)
            is FileViewHolder -> holder.bind(item)
            is AudioViewHolder -> holder.bind(item)
        }
    }

    // ── Квадратная карточка для фото / видео ──────────────────────────────────
    inner class MediaViewHolder(
        private val binding: ItemAttachmentPreviewBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessagesApiOuterClass.ChatAttachmentInfo) {
            val attachment = item.attachment

            when (attachment.type) {
                Shared.MessageAttachmentType.IMAGE,
                Shared.MessageAttachmentType.GIF,
                Shared.MessageAttachmentType.STICKER -> {
                    binding.previewImageView.visibility = View.VISIBLE
                    binding.fileIconContainer.visibility = View.GONE
                    binding.videoIndicator.visibility = View.GONE

                    // Используем previewUrl напрямую если он уже есть в ответе сервера,
                    // иначе запрашиваем через previewFileId (или fileId как фолбек)
                    val directUrl = FileMediaUrl.rewrite(binding.root.context, attachment.previewUrl)
                    val fallbackFileId = if (attachment.previewFileId.isNotBlank())
                        attachment.previewFileId else attachment.fileId

                    val scope = CoroutineScope(Dispatchers.Main)
                    scope.launch {
                        val url = if (directUrl.isNotBlank()) {
                            directUrl
                        } else {
                            withContext(Dispatchers.IO) { getFileUrl(fallbackFileId) }
                        }
                        if (url != null) {
                            binding.previewImageView.load(url) {
                                crossfade(true)
                                placeholder(R.color.surface_container_high)
                                error(R.color.surface_container_high)
                            }
                        }
                    }
                }

                Shared.MessageAttachmentType.VIDEO -> {
                    val directUrl = FileMediaUrl.rewrite(binding.root.context, attachment.previewUrl)
                    val hasPreview = directUrl.isNotBlank() || attachment.previewFileId.isNotBlank()

                    if (hasPreview) {
                        binding.previewImageView.visibility = View.VISIBLE
                        binding.fileIconContainer.visibility = View.GONE
                        binding.videoIndicator.visibility = View.VISIBLE

                        val scope = CoroutineScope(Dispatchers.Main)
                        scope.launch {
                            val url = if (directUrl.isNotBlank()) {
                                directUrl
                            } else {
                                withContext(Dispatchers.IO) { getFileUrl(attachment.previewFileId) }
                            }
                            if (url != null) {
                                binding.previewImageView.load(url) {
                                    crossfade(true)
                                    placeholder(R.color.surface_container_high)
                                    error(R.color.surface_container_high)
                                }
                            }
                        }
                    } else {
                        binding.previewImageView.visibility = View.GONE
                        binding.fileIconContainer.visibility = View.VISIBLE
                        binding.videoIndicator.visibility = View.GONE
                        binding.fileNameTextView.text = binding.root.context.getString(R.string.attachment_video)
                    }
                }

                else -> {
                    binding.previewImageView.visibility = View.GONE
                    binding.fileIconContainer.visibility = View.VISIBLE
                    binding.videoIndicator.visibility = View.GONE
                    binding.fileNameTextView.text = attachment.fileName.ifBlank {
                        binding.root.context.getString(R.string.attachment_file)
                    }
                }
            }

            binding.root.setOnClickListener { onAttachmentClick(item) }
        }
    }

    // ── Строка списка для файлов/документов ───────────────────────────────────
    inner class FileViewHolder(
        private val binding: ItemAttachmentFileBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessagesApiOuterClass.ChatAttachmentInfo) {
            val attachment = item.attachment

            binding.fileNameTextView.text = attachment.fileName.ifBlank {
                binding.root.context.getString(R.string.attachment_file)
            }

            val sizeBytes = attachment.attachmentSize
            if (sizeBytes > 0) {
                binding.fileSizeTextView.text = formatFileSize(binding.root.context, sizeBytes)
                binding.fileSizeTextView.visibility = View.VISIBLE
            } else {
                binding.fileSizeTextView.visibility = View.GONE
            }

            binding.root.setOnClickListener { onAttachmentClick(item) }
        }

        private fun formatFileSize(context: android.content.Context, bytes: Long): String = when {
            bytes < 1024 -> context.getString(R.string.file_size_bytes, bytes)
            bytes < 1024 * 1024 -> context.getString(R.string.file_size_kilobytes, bytes / 1024.0)
            bytes < 1024L * 1024 * 1024 -> context.getString(
                R.string.file_size_megabytes,
                bytes / (1024.0 * 1024.0)
            )
            else -> context.getString(
                R.string.file_size_gigabytes,
                bytes / (1024.0 * 1024.0 * 1024.0)
            )
        }
    }

    // ── Строка голосового сообщения ──────────────────────────────────────────
    inner class AudioViewHolder(
        private val binding: ItemProfileVoiceBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessagesApiOuterClass.ChatAttachmentInfo) {
            val fileId = item.attachment.fileId
            val playing = AudioPlayerHelper.isActiveFile(fileId) && AudioPlayerHelper.isPlaying()
            binding.playIcon.setImageResource(
                if (playing) R.drawable.ic_pause else R.drawable.ic_play_arrow
            )
            binding.playButton.contentDescription = binding.root.context.getString(
                if (playing) R.string.cd_pause else R.string.cd_play
            )
            binding.audioDuration.text = ""

            binding.playButton.setOnClickListener {
                val dl = downloadToCache
                val sc = scope
                if (dl == null || sc == null) return@setOnClickListener

                if (AudioPlayerHelper.isActiveFile(fileId) && AudioPlayerHelper.isPlaying()) {
                    AudioPlayerHelper.pause()
                    binding.playIcon.setImageResource(R.drawable.ic_play_arrow)
                    binding.playButton.contentDescription = binding.root.context.getString(R.string.cd_play)
                    return@setOnClickListener
                }

                sc.launch {
                    val file = withContext(Dispatchers.IO) { dl(fileId) } ?: return@launch
                    AudioPlayerHelper.play(fileId, file, object : AudioCallbacks {
                        override fun onProgress(positionMs: Int, durationMs: Int) {
                            binding.audioDuration.text = formatDuration(positionMs)
                        }
                        override fun onStateChanged(isPlaying: Boolean) {
                            binding.playIcon.setImageResource(
                                if (isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow
                            )
                            binding.playButton.contentDescription = binding.root.context.getString(
                                if (isPlaying) R.string.cd_pause else R.string.cd_play
                            )
                        }
                        override fun onError() {
                            binding.playIcon.setImageResource(R.drawable.ic_play_arrow)
                            binding.playButton.contentDescription = binding.root.context.getString(R.string.cd_play)
                        }
                        override fun onComplete() {
                            binding.playIcon.setImageResource(R.drawable.ic_play_arrow)
                            binding.playButton.contentDescription = binding.root.context.getString(R.string.cd_play)
                            binding.audioDuration.text = ""
                        }
                    })
                }
            }
        }

        private fun formatDuration(ms: Int): String {
            val totalSec = ms / 1000
            return "%d:%02d".format(totalSec / 60, totalSec % 60)
        }
    }

    class DiffCallback : DiffUtil.ItemCallback<MessagesApiOuterClass.ChatAttachmentInfo>() {
        override fun areItemsTheSame(
            oldItem: MessagesApiOuterClass.ChatAttachmentInfo,
            newItem: MessagesApiOuterClass.ChatAttachmentInfo
        ): Boolean = oldItem.attachmentId == newItem.attachmentId

        override fun areContentsTheSame(
            oldItem: MessagesApiOuterClass.ChatAttachmentInfo,
            newItem: MessagesApiOuterClass.ChatAttachmentInfo
        ): Boolean = oldItem == newItem
    }
}
