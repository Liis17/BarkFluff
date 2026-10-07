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
import com.barkfluff.client.audio.AudioPlayback
import com.barkfluff.client.audio.AudioTrack
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.utils.AudioWaveformExtractor
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.voice.VoicePlaybackSpeed
import com.barkfluff.client.utils.FileMediaUrl
import com.barkfluff.client.utils.ImageLoadHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Адаптер для отображения вложений в профиле чата.
 * VIEW_TYPE_MEDIA — квадратная сетка (3 колонки) для фото и видео.
 * VIEW_TYPE_FILE  — вертикальный список с иконкой, именем и размером для файлов.
 * VIEW_TYPE_AUDIO — строка голосового с плеем и длительностью (таб «Голосовые»).
 */
class AttachmentPreviewAdapter(
    private val playback: AudioPlayback,
    private val playbackChatId: String,
    private val playbackChatTitle: String,
    private val playbackIsGroupChat: Boolean = false,
    private val playbackOtherUserId: Long = 0L,
    private val resolveSender: suspend (Long) -> String? = { null },
    private val getFileUrl: suspend (String) -> String?,
    private val onAttachmentClick: (MessagesApiOuterClass.ChatAttachmentInfo) -> Unit,
    private val downloadToCache: (suspend (String) -> File?)? = null,
    private val scope: CoroutineScope? = null,
    private val autoDownloadViews: AttachmentAutoDownloadViews? = null,
) : ListAdapter<MessagesApiOuterClass.ChatAttachmentInfo, RecyclerView.ViewHolder>(DiffCallback()) {

    private val viewOperations = ViewBoundOperationController(scope)

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
        holder.itemView.findViewById<android.widget.ImageView>(R.id.previewImageView)?.dispose()
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
                            binding.galleryDownloadProgress.isIndeterminate = state.progress == 0
                            binding.root.stateDescription = binding.root.context.getString(R.string.cd_auto_download_progress, state.progress)
                        }
                        AttachmentDownloadState.Waiting, AttachmentDownloadState.Failed -> {
                            binding.galleryDownloadIcon.visibility = View.VISIBLE
                            binding.galleryDownloadProgress.visibility = View.GONE
                            binding.root.stateDescription = binding.root.context.getString(
                                if (state == AttachmentDownloadState.Failed) R.string.auto_download_failed else R.string.cd_download_file
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
                if (state is AttachmentDownloadState.Downloading) {
                    binding.galleryFileProgress.progress = state.progress
                    binding.galleryFileProgress.isIndeterminate = state.progress == 0
                    binding.galleryFileProgress.contentDescription = binding.root.context.getString(R.string.cd_auto_download_progress, state.progress)
                }
                binding.root.stateDescription = binding.root.context.getString(when (state) {
                    is AttachmentDownloadState.Cached -> R.string.cd_open_file
                    AttachmentDownloadState.Failed -> R.string.auto_download_failed
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
            val context = binding.root.context
            val fileId = item.attachment.fileId
            val voice = item.attachment.type == Shared.MessageAttachmentType.VOICE
            binding.root.tag = fileId
            var cachedDuration = 0L
            var downloadState: AttachmentDownloadState = AttachmentDownloadState.Waiting
            var manualDownloading = false
            binding.audioWaveform.resetAmplitudes()
            binding.voiceSpeed.visibility = if (voice) View.VISIBLE else View.GONE
            binding.voiceSpeed.setOnClickListener { playback.cycleSpeed() }
            binding.audioWaveform.onSeekRequested = { fraction ->
                val state = playback.state.value
                if (state.track?.fileId == fileId) playback.seekTo((state.durationMillis * fraction).toLong())
            }

            fun render(state: com.barkfluff.client.audio.PlaybackState) {
                val active = state.track?.fileId == fileId
                val playing = active && state.isPlaying
                val duration = if (active) state.durationMillis else cachedDuration
                val position = if (active) state.positionMillis else 0L
                val cached = FileCache.getFile(fileId) != null
                val downloading = manualDownloading || downloadState is AttachmentDownloadState.Downloading
                binding.galleryVoiceProgress.visibility = if (downloading) View.VISIBLE else View.GONE
                binding.galleryVoiceProgress.isIndeterminate = manualDownloading || (downloadState as? AttachmentDownloadState.Downloading)?.progress == 0
                if (downloadState is AttachmentDownloadState.Downloading && !manualDownloading) {
                    binding.galleryVoiceProgress.progress = (downloadState as AttachmentDownloadState.Downloading).progress
                    binding.galleryVoiceProgress.contentDescription = context.getString(R.string.cd_auto_download_progress, (downloadState as AttachmentDownloadState.Downloading).progress)
                }
                binding.playButton.isEnabled = !downloading
                binding.playIcon.visibility = if (downloading) View.INVISIBLE else View.VISIBLE
                binding.playIcon.setImageResource(when {
                    playing -> R.drawable.ic_pause
                    active || cached -> R.drawable.ic_play_arrow
                    else -> R.drawable.ic_download
                })
                binding.playButton.contentDescription = context.getString(when {
                    playing -> R.string.cd_pause
                    active || cached -> R.string.cd_play
                    else -> R.string.cd_download_file
                })
                binding.audioDuration.text = context.getString(R.string.audio_position, time(position), time(duration))
                if (!binding.audioWaveform.isPressed) binding.audioWaveform.setProgress(
                    if (duration > 0L) position.toFloat() / duration else 0f,
                )
                binding.audioWaveform.contentDescription = context.getString(R.string.cd_voice_seek, time(position), time(duration))
                val speed = VoicePlaybackSpeed.label(context, state.speed)
                binding.voiceSpeed.text = speed
                binding.voiceSpeed.contentDescription = context.getString(R.string.cd_voice_speed, speed)
            }

            fun loadWaveform(file: File) {
                viewOperations.launch(binding.audioWaveform) {
                    val result = withContext(Dispatchers.IO) {
                        val duration = android.media.MediaMetadataRetriever().let { retriever ->
                            try {
                                retriever.setDataSource(file.absolutePath)
                                retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0L
                            } catch (_: Exception) { 0L } finally { retriever.release() }
                        }
                        AudioWaveformExtractor.extract(file) to duration
                    }
                    binding.audioWaveform.setAmplitudes(result.first)
                    cachedDuration = result.second
                    render(playback.state.value)
                }
            }

            FileCache.getFile(fileId)?.let(::loadWaveform)
            autoDownloadViews?.bind(binding.root, item.attachment) { state ->
                downloadState = state
                if (state is AttachmentDownloadState.Cached) loadWaveform(state.file)
                binding.root.stateDescription = if (state == AttachmentDownloadState.Failed) context.getString(R.string.auto_download_failed) else null
                render(playback.state.value)
            }
            viewOperations.launch(binding.root) { playback.state.collect(::render) }
            binding.playButton.setOnClickListener {
                val state = playback.state.value
                if (state.track?.fileId == fileId) {
                    if (state.isPlaying) playback.pause() else playback.resume()
                    return@setOnClickListener
                }
                val download = downloadToCache ?: return@setOnClickListener
                manualDownloading = true
                autoDownloadViews?.setManualDownloading(binding.root, true)
                render(playback.state.value)
                viewOperations.launch(binding.playButton) {
                    try {
                        val file = withContext(Dispatchers.IO) { FileCache.getFile(fileId) ?: download(fileId) }
                        if (file == null) {
                            android.widget.Toast.makeText(context, R.string.profile_download_failed, android.widget.Toast.LENGTH_SHORT).show()
                            return@launch
                        }
                        loadWaveform(file)
                        val sender = if (item.senderId == GlobalParam(context).userId) context.getString(R.string.voice_sender_you)
                            else withTimeoutOrNull(1_000L) { resolveSender(item.senderId) }.orEmpty()
                                .ifBlank { context.getString(R.string.group_member_id, item.senderId) }
                        playback.play(AudioTrack(fileId, playbackChatId, playbackChatTitle, sender, item.messageId, voice,
                            playbackIsGroupChat, playbackOtherUserId), file)
                    } finally {
                        if (binding.root.tag == fileId) {
                            manualDownloading = false
                            autoDownloadViews?.setManualDownloading(binding.root, false)
                            render(playback.state.value)
                        }
                    }
                }
            }
            render(playback.state.value)
        }

        private fun time(ms: Long): String = binding.root.context.getString(
            R.string.voice_record_timer_format, ms / 60_000L, ms / 1_000L % 60,
        )
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
