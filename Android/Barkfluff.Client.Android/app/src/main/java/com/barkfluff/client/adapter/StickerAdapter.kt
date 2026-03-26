package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.ImageView
import androidx.recyclerview.widget.RecyclerView
import barkfluff.files.FilesApiOuterClass.StickerInfo
import com.barkfluff.client.R
import com.barkfluff.client.utils.ImageLoadHelper

class StickerAdapter(
    private val getFileUrl: suspend (String) -> String?,
    private val onStickerClick: (StickerInfo) -> Unit
) : RecyclerView.Adapter<StickerAdapter.StickerViewHolder>() {

    private var stickers: List<StickerInfo> = emptyList()

    fun setStickers(list: List<StickerInfo>) {
        stickers = list
        notifyDataSetChanged()
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): StickerViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_sticker, parent, false)
        return StickerViewHolder(view as ViewGroup)
    }

    override fun onBindViewHolder(holder: StickerViewHolder, position: Int) {
        holder.bind(stickers[position])
    }

    override fun getItemCount(): Int = stickers.size

    inner class StickerViewHolder(itemView: ViewGroup) : RecyclerView.ViewHolder(itemView) {
        private val imageView: ImageView = itemView.findViewById(R.id.stickerImageView)

        fun bind(sticker: StickerInfo) {
            val fileId = sticker.previewFileId.ifBlank { sticker.fileId }
            val directUrl = sticker.previewUrl.ifBlank { sticker.fileUrl }

            ImageLoadHelper.loadByFileId(
                imageView = imageView,
                fileId = fileId,
                getUrlCallback = {
                    directUrl.ifBlank { getFileUrl(fileId) }
                },
                size = 128
            )

            imageView.setOnClickListener {
                onStickerClick(sticker)
            }
        }
    }
}
