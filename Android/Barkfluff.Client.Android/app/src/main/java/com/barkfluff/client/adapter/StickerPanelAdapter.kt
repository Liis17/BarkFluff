package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import barkfluff.files.FilesApiOuterClass.StickerInfo
import com.barkfluff.client.R
import com.barkfluff.client.utils.ImageLoadHelper

class StickerPanelAdapter(
    private val getFileUrl: suspend (String) -> String?,
    private val onStickerClick: (StickerInfo) -> Unit
) : ListAdapter<StickerPanelItem, RecyclerView.ViewHolder>(DiffCallback()) {

    companion object {
        const val VIEW_TYPE_PACK_HEADER = 0
        const val VIEW_TYPE_STICKER = 1
        const val VIEW_TYPE_LOADING = 2
        const val VIEW_TYPE_EMPTY = 3
    }

    override fun getItemViewType(position: Int): Int {
        return when (getItem(position)) {
            is StickerPanelItem.PackHeader -> VIEW_TYPE_PACK_HEADER
            is StickerPanelItem.Sticker -> VIEW_TYPE_STICKER
            is StickerPanelItem.Loading -> VIEW_TYPE_LOADING
            is StickerPanelItem.Empty -> VIEW_TYPE_EMPTY
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        val inflater = LayoutInflater.from(parent.context)
        return when (viewType) {
            VIEW_TYPE_PACK_HEADER -> PackHeaderViewHolder(
                inflater.inflate(R.layout.item_sticker_pack_header, parent, false)
            )
            VIEW_TYPE_STICKER -> StickerViewHolder(
                inflater.inflate(R.layout.item_sticker, parent, false)
            )
            VIEW_TYPE_LOADING -> LoadingViewHolder(
                inflater.inflate(R.layout.item_sticker_loading, parent, false)
            )
            else -> EmptyViewHolder(
                inflater.inflate(R.layout.item_sticker_empty, parent, false)
            )
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        when (val item = getItem(position)) {
            is StickerPanelItem.PackHeader -> (holder as PackHeaderViewHolder).bind(item)
            is StickerPanelItem.Sticker -> (holder as StickerViewHolder).bind(item)
            is StickerPanelItem.Loading -> {}
            is StickerPanelItem.Empty -> {}
        }
    }

    inner class PackHeaderViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val coverImage: ImageView = itemView.findViewById(R.id.packCoverImage)
        private val nameText: TextView = itemView.findViewById(R.id.packNameText)
        private val countText: TextView = itemView.findViewById(R.id.packCountText)

        fun bind(header: StickerPanelItem.PackHeader) {
            nameText.text = header.packName
            val countStr = when {
                header.stickerCount % 10 == 1 && header.stickerCount % 100 != 11 -> "${header.stickerCount} стикер"
                header.stickerCount % 10 in 2..4 && header.stickerCount % 100 !in 12..14 -> "${header.stickerCount} стикера"
                else -> "${header.stickerCount} стикеров"
            }
            countText.text = countStr

            // coverStickerId — это ID стикера (не файла), ищем файл первого стикера пака
            val coverFileId = findCoverFileId(header)
            if (coverFileId != null) {
                coverImage.visibility = View.VISIBLE
                ImageLoadHelper.loadByFileId(
                    imageView = coverImage,
                    fileId = coverFileId,
                    getUrlCallback = { getFileUrl(coverFileId) },
                    size = 64
                )
            } else {
                coverImage.visibility = View.GONE
            }
        }

        private fun findCoverFileId(header: StickerPanelItem.PackHeader): String? {
            val items = currentList
            for (item in items) {
                if (item is StickerPanelItem.Sticker && item.packId == header.packId) {
                    val sticker = item.stickerInfo
                    // Если coverStickerId совпадает с ID стикера — используем его файл
                    if (header.coverStickerId.isNotBlank() && sticker.id == header.coverStickerId) {
                        return sticker.previewFileId.ifBlank { sticker.fileId }
                    }
                }
            }
            // Fallback: первый стикер пака
            for (item in items) {
                if (item is StickerPanelItem.Sticker && item.packId == header.packId) {
                    return item.stickerInfo.previewFileId.ifBlank { item.stickerInfo.fileId }
                }
            }
            return null
        }
    }

    inner class StickerViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val imageView: ImageView = itemView.findViewById(R.id.stickerImageView)

        fun bind(item: StickerPanelItem.Sticker) {
            val sticker = item.stickerInfo
            val fileId = sticker.previewFileId.ifBlank { sticker.fileId }

            ImageLoadHelper.loadByFileId(
                imageView = imageView,
                fileId = fileId,
                getUrlCallback = { getFileUrl(fileId) },
                size = 128
            )

            imageView.setOnClickListener {
                onStickerClick(sticker)
            }
        }
    }

    class LoadingViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView)
    class EmptyViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView)

    private class DiffCallback : DiffUtil.ItemCallback<StickerPanelItem>() {
        override fun areItemsTheSame(oldItem: StickerPanelItem, newItem: StickerPanelItem): Boolean {
            return when {
                oldItem is StickerPanelItem.PackHeader && newItem is StickerPanelItem.PackHeader ->
                    oldItem.packId == newItem.packId
                oldItem is StickerPanelItem.Sticker && newItem is StickerPanelItem.Sticker ->
                    oldItem.stickerInfo.id == newItem.stickerInfo.id
                oldItem is StickerPanelItem.Loading && newItem is StickerPanelItem.Loading -> true
                oldItem is StickerPanelItem.Empty && newItem is StickerPanelItem.Empty -> true
                else -> false
            }
        }

        override fun areContentsTheSame(oldItem: StickerPanelItem, newItem: StickerPanelItem): Boolean {
            return oldItem == newItem
        }
    }
}
