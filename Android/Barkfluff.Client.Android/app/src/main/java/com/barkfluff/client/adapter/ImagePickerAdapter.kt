package com.barkfluff.client.adapter

import android.net.Uri
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemCameraButtonBinding
import com.barkfluff.client.databinding.ItemImagePickerBinding

/**
 * Адаптер для отображения сетки изображений в ImagePickerBottomSheet.
 * Поддерживает:
 * - Кнопку камеры в начале списка
 * - Множественный выбор до maxSelection изображений
 * - Нумерацию выбранных элементов в порядке выбора
 */
class ImagePickerAdapter(
    private val onCameraClick: () -> Unit,
    private val onImageClick: (ImageItem) -> Unit,
    private val maxSelection: Int = 10
) : ListAdapter<ImagePickerAdapter.ListItem, RecyclerView.ViewHolder>(DiffCallback()) {

    // Выбранные изображения в порядке выбора
    private val selectedItems = mutableListOf<ImageItem>()
    private val selectedUris = mutableSetOf<Uri>()

    companion object {
        private const val VIEW_TYPE_CAMERA = 0
        private const val VIEW_TYPE_IMAGE = 1
    }

    /**
     * Элемент списка (камера или изображение)
     */
    sealed class ListItem {
        data object Camera : ListItem()
        data class Image(val imageItem: ImageItem) : ListItem()
    }

    override fun getItemViewType(position: Int): Int {
        return when (getItem(position)) {
            is ListItem.Camera -> VIEW_TYPE_CAMERA
            is ListItem.Image -> VIEW_TYPE_IMAGE
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return when (viewType) {
            VIEW_TYPE_CAMERA -> {
                val binding = ItemCameraButtonBinding.inflate(
                    LayoutInflater.from(parent.context),
                    parent,
                    false
                )
                CameraViewHolder(binding)
            }
            else -> {
                val binding = ItemImagePickerBinding.inflate(
                    LayoutInflater.from(parent.context),
                    parent,
                    false
                )
                ImageViewHolder(binding)
            }
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        when (val item = getItem(position)) {
            is ListItem.Camera -> (holder as CameraViewHolder).bind()
            is ListItem.Image -> (holder as ImageViewHolder).bind(item.imageItem)
        }
    }

    inner class CameraViewHolder(
        private val binding: ItemCameraButtonBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind() {
            binding.cardView.setOnClickListener {
                onCameraClick()
            }
        }
    }

    inner class ImageViewHolder(
        private val binding: ItemImagePickerBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: ImageItem) {
            // Загрузка изображения через Coil
            binding.imageView.load(item.uri) {
                crossfade(true)
                placeholder(R.color.surface_container_high)
                error(R.color.surface_container_high)
            }

            val isSelected = selectedUris.contains(item.uri)
            val selectionIndex = selectedItems.indexOfFirst { it.uri == item.uri }

            // Состояние выбора
            if (isSelected && selectionIndex >= 0) {
                binding.selectionOverlay.visibility = View.VISIBLE
                binding.selectionIndicator.background =
                    binding.root.context.getDrawable(R.drawable.selection_indicator_selected_background)
                binding.checkIcon.visibility = View.GONE
                binding.selectionNumber.visibility = View.VISIBLE
                binding.selectionNumber.text = (selectionIndex + 1).toString()
            } else {
                binding.selectionOverlay.visibility = View.GONE
                binding.selectionIndicator.background =
                    binding.root.context.getDrawable(R.drawable.selection_indicator_background)
                binding.checkIcon.visibility = View.GONE
                binding.selectionNumber.visibility = View.GONE
            }

            // Клик по изображению
            binding.cardView.setOnClickListener {
                toggleSelection(item)
            }
        }

        private fun toggleSelection(item: ImageItem) {
            if (selectedUris.contains(item.uri)) {
                // Снимаем выделение
                selectedUris.remove(item.uri)
                selectedItems.removeIf { it.uri == item.uri }
            } else {
                // Проверяем лимит
                if (selectedItems.size >= maxSelection) {
                    return
                }
                // Добавляем выделение
                selectedUris.add(item.uri)
                selectedItems.add(item)
            }

            // Уведомляем об изменении для обновления нумерации
            notifySelectionChanged()
            onImageClick(item)
        }
    }

    /**
     * Уведомляет об изменении выделения.
     * Обновляем все видимые элементы для корректной нумерации.
     */
    private fun notifySelectionChanged() {
        notifyItemRangeChanged(1, currentList.size - 1)
    }

    /**
     * Возвращает список выбранных изображений в порядке выбора.
     */
    fun getSelectedItems(): List<ImageItem> = selectedItems.toList()

    /**
     * Очищает выделение.
     */
    fun clearSelection() {
        selectedItems.clear()
        selectedUris.clear()
        notifyItemRangeChanged(1, currentList.size - 1)
    }

    /**
     * Количество выбранных элементов.
     */
    fun getSelectionCount(): Int = selectedItems.size

    /**
     * Устанавливает изображения в адаптер.
     */
    fun setImages(images: List<ImageItem>) {
        val items = mutableListOf<ListItem>(ListItem.Camera)
        items.addAll(images.map { ListItem.Image(it) })
        submitList(items)
    }

    class DiffCallback : DiffUtil.ItemCallback<ListItem>() {
        override fun areItemsTheSame(oldItem: ListItem, newItem: ListItem): Boolean {
            return when {
                oldItem is ListItem.Camera && newItem is ListItem.Camera -> true
                oldItem is ListItem.Image && newItem is ListItem.Image ->
                    oldItem.imageItem.uri == newItem.imageItem.uri
                else -> false
            }
        }

        override fun areContentsTheSame(oldItem: ListItem, newItem: ListItem): Boolean {
            return oldItem == newItem
        }
    }
}

/**
 * Модель изображения для пикера.
 */
data class ImageItem(
    val uri: Uri,
    val id: Long,
    val dateAdded: Long,
    val displayName: String = ""
)
