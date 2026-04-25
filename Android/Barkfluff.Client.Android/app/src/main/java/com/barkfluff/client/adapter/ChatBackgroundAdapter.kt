package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import com.barkfluff.client.databinding.ItemChatBackgroundBinding
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Адаптер сетки превью фоновых изображений чата.
 * Пропорция ячейки 2:3 (ширина:высота).
 * Поддерживает: выбор по клику, режим удаления по удержанию.
 */
class ChatBackgroundAdapter(
    private val scope: CoroutineScope,
    private val getFileUrl: suspend (String) -> String?,
    private val onSelect: (fileId: String) -> Unit,
    private val onDelete: (fileId: String) -> Unit
) : ListAdapter<ChatBackgroundItem, ChatBackgroundAdapter.ViewHolder>(DIFF) {

    /** FileId текущего выбранного фона */
    var selectedFileId: String = ""
        set(value) {
            field = value
            notifyDataSetChanged()
        }

    /** FileId элемента в режиме удаления (пустая строка = нет) */
    private var deleteModeFileId: String = ""

    companion object {
        private val DIFF = object : DiffUtil.ItemCallback<ChatBackgroundItem>() {
            override fun areItemsTheSame(a: ChatBackgroundItem, b: ChatBackgroundItem) = a.fileId == b.fileId
            override fun areContentsTheSame(a: ChatBackgroundItem, b: ChatBackgroundItem) = a == b
        }
    }

    inner class ViewHolder(val binding: ItemChatBackgroundBinding) :
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

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemChatBackgroundBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
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
