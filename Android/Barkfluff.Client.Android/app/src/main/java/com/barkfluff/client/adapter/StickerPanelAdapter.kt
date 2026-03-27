package com.barkfluff.client.adapter

import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import barkfluff.files.FilesApiOuterClass.StickerInfo
import coil.load
import coil.request.ErrorResult
import coil.request.SuccessResult
import com.barkfluff.client.R
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class StickerPanelAdapter(
    private val getFileUrl: suspend (String) -> String?,
    private val onStickerClick: (StickerInfo) -> Unit
) : ListAdapter<StickerPanelItem, RecyclerView.ViewHolder>(DiffCallback()) {

    companion object {
        private const val TAG = "StickerPanelAdapter"
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

    /**
     * Загружает изображение в ImageView, пробуя URL-ы по приоритету:
     * 1. directUrl (если не пустой)
     * 2. gRPC GetTempDownloadUrl через getFileUrl
     * Использует Coil напрямую с target(imageView) для максимальной надёжности.
     */
    private fun loadStickerImage(imageView: ImageView, fileId: String, directUrl: String, size: Int) {
        if (fileId.isBlank()) {
            Log.w(TAG, "loadStickerImage: fileId is blank, skipping")
            return
        }

        val imageLoader = AvatarLoader.getImageLoader(imageView.context)

        // Попробовать directUrl сначала
        if (directUrl.isNotBlank()) {
            Log.d(TAG, "loadStickerImage: trying directUrl for $fileId: $directUrl")
            imageView.load(directUrl, imageLoader) {
                memoryCacheKey(fileId)
                diskCacheKey(fileId)
                crossfade(200)
                size(size)
                listener(
                    onSuccess = { _, _ ->
                        Log.d(TAG, "loadStickerImage: SUCCESS via directUrl for $fileId")
                    },
                    onError = { _, result ->
                        Log.e(TAG, "loadStickerImage: FAILED directUrl for $fileId: ${result.throwable.message}, trying gRPC...")
                        loadViaGrpc(imageView, fileId, size)
                    }
                )
            }
        } else {
            Log.d(TAG, "loadStickerImage: no directUrl for $fileId, going to gRPC")
            loadViaGrpc(imageView, fileId, size)
        }
    }

    private fun loadViaGrpc(imageView: ImageView, fileId: String, size: Int) {
        imageView.tag = fileId
        MainScope().launch {
            val url = withContext(Dispatchers.IO) { getFileUrl(fileId) }
            Log.d(TAG, "loadViaGrpc: fileId=$fileId, url=$url")
            if (url.isNullOrBlank()) {
                Log.e(TAG, "loadViaGrpc: gRPC returned null/blank URL for $fileId")
                return@launch
            }
            withContext(Dispatchers.Main) {
                if (imageView.tag != fileId) return@withContext
                val imageLoader = AvatarLoader.getImageLoader(imageView.context)
                imageView.load(url, imageLoader) {
                    memoryCacheKey(fileId)
                    diskCacheKey(fileId)
                    crossfade(200)
                    size(size)
                    listener(
                        onSuccess = { _, _ ->
                            Log.d(TAG, "loadViaGrpc: SUCCESS for $fileId")
                        },
                        onError = { _, result ->
                            Log.e(TAG, "loadViaGrpc: FAILED for $fileId url=$url: ${result.throwable.message}")
                        }
                    )
                }
            }
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

            val coverFileId = findCoverFileId(header)
            if (coverFileId != null) {
                coverImage.visibility = View.VISIBLE
                loadStickerImage(coverImage, coverFileId, "", 64)
            } else {
                coverImage.visibility = View.GONE
            }
        }

        private fun findCoverFileId(header: StickerPanelItem.PackHeader): String? {
            val items = currentList
            for (item in items) {
                if (item is StickerPanelItem.Sticker && item.packId == header.packId) {
                    val sticker = item.stickerInfo
                    if (header.coverStickerId.isNotBlank() && sticker.id == header.coverStickerId) {
                        return sticker.fileId
                    }
                }
            }
            for (item in items) {
                if (item is StickerPanelItem.Sticker && item.packId == header.packId) {
                    return item.stickerInfo.fileId
                }
            }
            return null
        }
    }

    inner class StickerViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val imageView: ImageView = itemView.findViewById(R.id.stickerImageView)

        fun bind(item: StickerPanelItem.Sticker) {
            val sticker = item.stickerInfo
            // Используем оригинальный fileId — превью не имеют записи в БД и не доступны
            val fileId = sticker.fileId

            Log.d(TAG, "StickerViewHolder.bind: id=${sticker.id}, fileId=$fileId")

            loadStickerImage(imageView, fileId, "", 128)

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
