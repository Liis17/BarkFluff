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
import coil.dispose
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemAttachmentFileBinding
import com.barkfluff.client.databinding.ItemAttachmentPreviewBinding
import com.barkfluff.client.databinding.ItemProfileVoiceBinding
import com.barkfluff.client.utils.AudioCallbacks
import com.barkfluff.client.utils.AudioPlayerHelper
import com.barkfluff.client.utils.FileMediaUrl
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.ImageLoadHelper
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
    private val scope: CoroutineScope? = null,
    private val autoDownloadViews: AttachmentAutoDownloadViews? = null,
) : ListAdapter<MessagesApiOuterClass.ChatAttachmentInfo, RecyclerView.ViewHolder>(DiffCallback()) {

    private val viewOperations = ViewBoundOperationController()

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
        autoDownloadViews?.recycleTree(holder.itemView)
        viewOperations.cancelTree(holder.itemView)
        val item = getItem(position)
        when (holder) {
            is MediaViewHolder -> holder.bind(item)
            is FileViewHolder -> holder.bind(item)
            is AudioViewHolder -> holder.bind(item)
        }
    }

    override fun onAttachedToRecyclerView(recyclerView: RecyclerView) {
        super.onAttachedToRecyclerView(recyclerView)
        autoDownloadViews?.attach(recyclerView)
    }

    override fun onDetachedFromRecyclerView(recyclerView: RecyclerView) {
        autoDownloadViews?.detach(recyclerView)
        viewOperations.cancelAll()
        super.onDetachedFromRecyclerView(recyclerView)
    }

    override fun onViewRecycled(holder: RecyclerView.ViewHolder) {
        autoDownloadViews?.recycleTree(holder.itemView)
        viewOperations.cancelTree(holder.itemView)
        holder.itemView.findViewById<android.widget.ImageView>(R.id.previewImageView)?.let {
            it.dispose()
            it.tag = null
        }
        super.onViewRecycled(holder)
    }

    // ── Квадратная карточка для фото / видео ──────────────────────────────────
    inner class MediaViewHolder(
        private val binding: ItemAttachmentPreviewBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MessagesApiOuterClass.ChatAttachmentInfo) {
            val attachment = item.attachment
            val isVideo = attachment.type == Shared.MessageAttachmentType.VIDEO
            binding.previewImageView.visibility = View.VISIBLE
            binding.previewImageView.setImageResource(R.drawable.ic_image_placeholder)
            binding.fileIconContainer.visibility = View.GONE
            binding.videoIndicator.visibility = if (isVideo) View.VISIBLE else View.GONE
            binding.galleryDownloadIcon.visibility = View.VISIBLE
            binding.galleryDownloadProgress.visibility = View.GONE
            binding.root.stateDescription = binding.root.context.getString(R.string.cd_download_file)

            if (attachment.type == Shared.MessageAttachmentType.STICKER) {
                binding.galleryDownloadIcon.visibility = View.GONE
                viewOperations.launch(binding.previewImageView) {
                    val directUrl = FileMediaUrl.rewrite(binding.root.context, attachment.previewUrl)
                    val url = directUrl.ifBlank {
                        withContext(Dispatchers.IO) { getFileUrl(attachment.previewFileId.ifBlank { attachment.fileId }) }.orEmpty()
                    }
                    if (url.isNotBlank()) binding.previewImageView.load(url)
                }
            } else {
                viewOperations.launch(binding.previewImageView) {
                    if (ImageLoadHelper.loadCached(binding.previewImageView, attachment)) {
                        binding.galleryDownloadIcon.visibility = View.GONE
                    }
                }
                autoDownloadViews?.bind(binding.root, attachment) { state ->
                    when (state) {
                        is AttachmentDownloadState.Cached -> {
                            viewOperations.cancel(binding.previewImageView)
                            ImageLoadHelper.loadLocal(binding.previewImageView, attachment, state.file)
                            binding.galleryDownloadIcon.visibility = View.GONE
                            binding.galleryDownloadProgress.visibility = View.GONE
                            binding.root.stateDescription = binding.root.context.getString(R.string.cd_open_file)
                        }
                        is AttachmentDownloadState.Downloading -> {
                            binding.galleryDownloadIcon.visibility = View.GONE
                            binding.galleryDownloadProgress.visibility = View.VISIBLE
                            binding.galleryDownloadProgress.progress = state.progress
                        }
                        AttachmentDownloadState.Waiting, AttachmentDownloadState.Failed -> {
                            binding.galleryDownloadIcon.visibility = View.VISIBLE
                            binding.galleryDownloadProgress.visibility = View.GONE
                            binding.root.stateDescription = binding.root.context.getString(
                                if (state == AttachmentDownloadState.Failed) R.string.profile_download_failed else R.string.cd_download_file
                            )
                        }
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

            autoDownloadViews?.bind(binding.root, attachment) { state ->
                binding.galleryFileProgress.visibility = if (state is AttachmentDownloadState.Downloading) View.VISIBLE else View.GONE
                binding.galleryFileDownloadIcon.visibility = if (state == AttachmentDownloadState.Waiting || state == AttachmentDownloadState.Failed) View.VISIBLE else View.GONE
                if (state is AttachmentDownloadState.Downloading) binding.galleryFileProgress.progress = state.progress
                binding.root.stateDescription = binding.root.context.getString(when (state) {
                    is AttachmentDownloadState.Cached -> R.string.cd_open_file
                    AttachmentDownloadState.Failed -> R.string.profile_download_failed
                    else -> R.string.cd_download_file
                })
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
            binding.root.tag = fileId
            val playing = AudioPlayerHelper.isActiveFile(fileId) && AudioPlayerHelper.isPlaying()
            binding.playIcon.setImageResource(
                if (playing) R.drawable.ic_pause else R.drawable.ic_play_arrow
            )
            binding.playButton.contentDescription = binding.root.context.getString(
                if (playing) R.string.cd_pause else R.string.cd_play
            )
            binding.audioDuration.text = ""

            autoDownloadViews?.bind(binding.root, item.attachment) { state ->
                val downloading = state is AttachmentDownloadState.Downloading
                binding.galleryVoiceProgress.visibility = if (downloading) View.VISIBLE else View.GONE
                binding.galleryVoiceProgress.isIndeterminate = false
                if (state is AttachmentDownloadState.Downloading) binding.galleryVoiceProgress.progress = state.progress
                binding.playButton.isEnabled = !downloading
                binding.playIcon.visibility = if (downloading) View.INVISIBLE else View.VISIBLE
                val cached = state is AttachmentDownloadState.Cached
                val isPlaying = cached && AudioPlayerHelper.isActiveFile(fileId) && AudioPlayerHelper.isPlaying()
                binding.playIcon.setImageResource(when {
                    isPlaying -> R.drawable.ic_pause
                    cached -> R.drawable.ic_play_arrow
                    else -> R.drawable.ic_download
                })
                binding.playButton.contentDescription = binding.root.context.getString(when {
                    isPlaying -> R.string.cd_pause
                    cached -> R.string.cd_play
                    else -> R.string.cd_download_file
                })
                binding.root.stateDescription = if (state == AttachmentDownloadState.Failed) binding.root.context.getString(R.string.profile_download_failed) else null
            }

            binding.playButton.setOnClickListener {
                val dl = downloadToCache
                if (dl == null || scope == null) return@setOnClickListener

                if (AudioPlayerHelper.isActiveFile(fileId) && AudioPlayerHelper.isPlaying()) {
                    AudioPlayerHelper.pause()
                    binding.playIcon.setImageResource(R.drawable.ic_play_arrow)
                    binding.playButton.contentDescription = binding.root.context.getString(R.string.cd_play)
                    return@setOnClickListener
                }

                autoDownloadViews?.setManualDownloading(binding.root, true)
                binding.playButton.isEnabled = false
                binding.galleryVoiceProgress.isIndeterminate = true
                binding.galleryVoiceProgress.visibility = View.VISIBLE
                viewOperations.launch(binding.playButton) {
                    try {
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
                    } finally {
                        if (binding.root.tag == fileId) {
                            binding.galleryVoiceProgress.visibility = View.GONE
                            binding.playButton.isEnabled = true
                            autoDownloadViews?.setManualDownloading(binding.root, false)
                        }
                    }
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
