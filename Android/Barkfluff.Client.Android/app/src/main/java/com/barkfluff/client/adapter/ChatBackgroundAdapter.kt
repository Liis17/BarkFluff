package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import com.barkfluff.client.databinding.ItemChatBackgroundBinding
import com.barkfluff.client.databinding.ItemChatBackgroundNoneBinding
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Адаптер сетки превью фоновых изображений чата.
 * Пропорция ячейки 2:3 (ширина:высота).
 * Первая ячейка всегда — "без фона" (fileId == "").
 * Поддерживает: выбор по клику, режим удаления по удержанию.
 */
class ChatBackgroundAdapter(
    private val scope: CoroutineScope,
    private val getFileUrl: suspend (String) -> String?,
    private val onSelect: (fileId: String) -> Unit,
    private val onDelete: (fileId: String) -> Unit
) : ListAdapter<ChatBackgroundItem, RecyclerView.ViewHolder>(DIFF) {

    /** FileId текущего выбранного фона (пустая строка = без фона) */
    var selectedFileId: String = ""
        set(value) {
            field = value
            notifyDataSetChanged()
        }

    /** FileId элемента в режиме удаления (пустая строка = нет) */
    private var deleteModeFileId: String = ""

    companion object {
        private const val VIEW_TYPE_NONE = 0
        private const val VIEW_TYPE_IMAGE = 1

        /** Sentinel fileId для ячейки "без фона" */
        const val FILE_ID_NONE = ""

        private val DIFF = object : DiffUtil.ItemCallback<ChatBackgroundItem>() {
            override fun areItemsTheSame(a: ChatBackgroundItem, b: ChatBackgroundItem) = a.fileId == b.fileId
            override fun areContentsTheSame(a: ChatBackgroundItem, b: ChatBackgroundItem) = a == b
        }
    }

    // ─── ViewHolder: ячейка "без фона" ────────────────────────────────────────

    inner class NoneViewHolder(val binding: ItemChatBackgroundNoneBinding) :
        RecyclerView.ViewHolder(binding.root) {

        fun bind() {
            val ctx = binding.root.context
            val isSelected = selectedFileId == FILE_ID_NONE
            binding.noneCard.strokeColor = if (isSelected)
                ctx.getColor(android.R.color.holo_blue_light)
            else
                ctx.obtainStyledAttributes(intArrayOf(com.google.android.material.R.attr.colorOutlineVariant))
                    .run { val c = getColor(0, 0); recycle(); c }
            binding.selectedCheck.visibility = if (isSelected) View.VISIBLE else View.GONE
            binding.root.setOnClickListener {
                if (deleteModeFileId.isNotEmpty()) {
                    deleteModeFileId = ""
                    notifyDataSetChanged()
                } else {
                    onSelect(FILE_ID_NONE)
                }
            }
        }
    }

    // ─── ViewHolder: ячейка с картинкой ───────────────────────────────────────

    inner class ImageViewHolder(val binding: ItemChatBackgroundBinding) :
        RecyclerView.ViewHolder(binding.root) {

        fun bind(item: ChatBackgroundItem) {
            val ctx = binding.root.context
            val isSelected = item.fileId == selectedFileId
            val isDeleteMode = item.fileId == deleteModeFileId

            // Загружаем изображение
            scope.launch {
                val cached = withContext(Dispatchers.IO) { FileCache.getFile(item.fileId) }
                if (cached != null && cached.exists()) {
                    withContext(Dispatchers.Main) {
                        binding.backgroundPreviewImage.load(cached, AvatarLoader.getImageLoader(ctx)) {
                            crossfade(true)
                        }
                    }
                    return@launch
                }
                val url = getFileUrl(item.fileId) ?: return@launch
                withContext(Dispatchers.Main) {
                    binding.backgroundPreviewImage.load(url, AvatarLoader.getImageLoader(ctx)) {
                        crossfade(true)
                    }
                }
            }

            // Выбранное состояние
            binding.selectedIndicator.visibility = if (isSelected) View.VISIBLE else View.GONE
            binding.selectedCheck.visibility = if (isSelected) View.VISIBLE else View.GONE
            binding.backgroundCard.strokeColor = if (isSelected)
                ctx.getColor(android.R.color.holo_blue_light)
            else
                ctx.obtainStyledAttributes(intArrayOf(com.google.android.material.R.attr.colorOutlineVariant))
                    .run { val c = getColor(0, 0); recycle(); c }

            // Режим удаления
            binding.deleteOverlay.visibility = if (isDeleteMode) View.VISIBLE else View.GONE

            // Клик: выбрать фон или выйти из режима удаления
            binding.root.setOnClickListener {
                if (deleteModeFileId.isNotEmpty()) {
                    deleteModeFileId = ""
                    notifyDataSetChanged()
                } else {
                    onSelect(item.fileId)
                }
            }

            // Кнопка удалить
            binding.buttonDelete.setOnClickListener {
                val toDelete = item.fileId
                deleteModeFileId = ""
                onDelete(toDelete)
            }

            // Долгое нажатие: режим удаления
            binding.root.setOnLongClickListener {
                if (deleteModeFileId != item.fileId) {
                    deleteModeFileId = item.fileId
                    notifyDataSetChanged()
                }
                true
            }
        }
    }

    override fun getItemViewType(position: Int): Int {
        return if (getItem(position).fileId == FILE_ID_NONE) VIEW_TYPE_NONE else VIEW_TYPE_IMAGE
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return if (viewType == VIEW_TYPE_NONE) {
            NoneViewHolder(
                ItemChatBackgroundNoneBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
        } else {
            ImageViewHolder(
                ItemChatBackgroundBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        when (holder) {
            is NoneViewHolder -> holder.bind()
            is ImageViewHolder -> holder.bind(getItem(position))
        }
    }

    /** Сбросить режим удаления (например, по кнопке «Отмена») */
    fun cancelDeleteMode() {
        if (deleteModeFileId.isNotEmpty()) {
            deleteModeFileId = ""
            notifyDataSetChanged()
        }
    }

    fun isInDeleteMode() = deleteModeFileId.isNotEmpty()
}

data class ChatBackgroundItem(val fileId: String)
