package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import barkfluff.shared.Shared
import com.barkfluff.client.ImageViewerActivity
import com.barkfluff.client.R
import com.barkfluff.client.views.SquareImageView
import com.barkfluff.client.utils.ImageLoadHelper

/**
 * Адаптер для сетки изображений внутри сообщения.
 * Каждый элемент — квадратная ячейка (SquareImageView).
 * Клик — открытие ImageViewerActivity на выбранной позиции.
 */
class ImageGridAdapter(
    private val getFileUrl: suspend (String) -> String?
) : ListAdapter<Shared.MessageAttachment, ImageGridAdapter.ImageViewHolder>(DiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ImageViewHolder {
        val imageView = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_attachment_image_cell, parent, false) as SquareImageView
        return ImageViewHolder(imageView)
    }

    override fun onBindViewHolder(holder: ImageViewHolder, position: Int) {
        holder.bind(getItem(position), position)
    }

    inner class ImageViewHolder(
        private val imageView: SquareImageView
    ) : RecyclerView.ViewHolder(imageView) {

        fun bind(attachment: Shared.MessageAttachment, position: Int) {
            imageView.setImageDrawable(null)

            val previewFileId = attachment.previewFileId.ifBlank { attachment.fileId }
            val previewUrl = attachment.previewUrl

            val getUrl: suspend () -> String? = if (previewUrl.isNotBlank()) {
                { previewUrl }
            } else {
                { getFileUrl(previewFileId) }
            }

            ImageLoadHelper.loadByFileId(
                imageView = imageView,
                fileId = previewFileId,
                getUrlCallback = getUrl,
                size = 512,
                onError = {
                    imageView.setImageResource(R.drawable.ic_image_placeholder)
                }
            )

            imageView.setOnClickListener {
                val context = imageView.context
                val allFileIds = (0 until itemCount).map { getItem(it).fileId }
                val allPreviewUrls = (0 until itemCount).map { getItem(it).previewUrl }
                context.startActivity(
                    ImageViewerActivity.createIntent(context, allFileIds, allPreviewUrls, position)
                )
            }
        }
    }

    class DiffCallback : DiffUtil.ItemCallback<Shared.MessageAttachment>() {
        override fun areItemsTheSame(a: Shared.MessageAttachment, b: Shared.MessageAttachment) =
            a.id == b.id
        override fun areContentsTheSame(a: Shared.MessageAttachment, b: Shared.MessageAttachment) =
            a == b
    }
}
