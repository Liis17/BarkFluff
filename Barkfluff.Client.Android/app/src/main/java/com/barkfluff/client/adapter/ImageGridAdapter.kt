package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import androidx.recyclerview.widget.RecyclerView
import barkfluff.shared.Shared
import com.barkfluff.client.ImageViewerActivity
import com.barkfluff.client.MediaViewerActivity
import com.barkfluff.client.R
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.ImageLoadHelper

/**
 * Адаптер для медиа-сетки внутри сообщения (IMAGE, GIF, VIDEO).
 * Принимает cellSizePx — явный размер ячейки в пикселях,
 * чтобы обойти AT_MOST-измерения RecyclerView внутри wrap_content-цепочки.
 */
class ImageGridAdapter(
    private val cellSizePx: Int,
    private val getFileUrl: suspend (String) -> String?
) : RecyclerView.Adapter<ImageGridAdapter.MediaViewHolder>() {

    private var items: List<Shared.MessageAttachment> = emptyList()

    fun setImages(list: List<Shared.MessageAttachment>) {
        items = list
        notifyDataSetChanged()
    }

    override fun getItemCount() = items.size

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): MediaViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_attachment_media_cell, parent, false)
        view.layoutParams = RecyclerView.LayoutParams(cellSizePx, cellSizePx)
        return MediaViewHolder(view)
    }

    override fun onBindViewHolder(holder: MediaViewHolder, position: Int) {
        holder.bind(items[position], position)
    }

    inner class MediaViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val thumbnail: ImageView = itemView.findViewById(R.id.thumbnailImage)
        private val videoOverlay: View   = itemView.findViewById(R.id.videoOverlay)
        private val playIcon: ImageView  = itemView.findViewById(R.id.playIcon)

        fun bind(attachment: Shared.MessageAttachment, position: Int) {
            thumbnail.setImageDrawable(null)

            val isVideo = attachment.type == Shared.MessageAttachmentType.VIDEO
            videoOverlay.visibility = if (isVideo) View.VISIBLE else View.GONE
            playIcon.visibility     = if (isVideo) View.VISIBLE else View.GONE

            // Загружаем превью (previewFileId → fileId как fallback)
            val previewFileId = attachment.previewFileId.ifBlank { attachment.fileId }
            val previewUrl    = attachment.previewUrl

            val getUrl: suspend () -> String? = if (previewUrl.isNotBlank()) {
                { previewUrl }
            } else {
                { getFileUrl(previewFileId) }
            }

            ImageLoadHelper.loadByFileId(
                imageView = thumbnail,
                fileId = previewFileId,
                getUrlCallback = getUrl,
                onError = { thumbnail.setImageResource(R.drawable.ic_image_placeholder) }
            )

            // Обработка клика
            if (isVideo) {
                itemView.setOnClickListener {
                    val ctx = itemView.context
                    val cachedPath = FileCache.getFile(attachment.fileId)?.absolutePath
                    ctx.startActivity(
                        MediaViewerActivity.createIntent(
                            ctx,
                            attachment.fileId,
                            attachment.fileName.ifBlank { "video" },
                            cachedPath
                        )
                    )
                }
            } else {
                itemView.setOnClickListener {
                    val ctx = itemView.context
                    // Только IMAGE/GIF в ImageViewer
                    val imageItems = items.filter {
                        it.type == Shared.MessageAttachmentType.IMAGE ||
                        it.type == Shared.MessageAttachmentType.GIF
                    }
                    val clickedIndex = imageItems.indexOf(attachment).coerceAtLeast(0)
                    val allFileIds    = imageItems.map { it.fileId }
                    val allPreviewUrls = imageItems.map { it.previewUrl }
                    ctx.startActivity(
                        ImageViewerActivity.createIntent(ctx, allFileIds, allPreviewUrls, clickedIndex)
                    )
                }
            }
        }
    }
}
