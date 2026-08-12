package com.barkfluff.client.adapter

import android.net.Uri
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.decode.VideoFrameDecoder
import coil.load
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemCameraButtonBinding
import com.barkfluff.client.databinding.ItemFileButtonBinding
import com.barkfluff.client.databinding.ItemImagePickerBinding
import com.barkfluff.client.databinding.ItemSystemPickerButtonBinding
import com.barkfluff.client.editor.MediaEditCache

/**
 * Адаптер для отображения сетки медиа в ImagePickerBottomSheet.
 * Поддерживает:
 * - Три служебные плитки в начале списка: Камера, Системный пикер фото, Файлы
 * - Множественный выбор до maxSelection элементов
 * - Нумерацию выбранных в порядке выбора
 * - Раздельные зоны: галочка = выбор, превью = просмотр
 * - Превью видео с длительностью и иконкой play
 */
class ImagePickerAdapter(
    private val onCameraClick: () -> Unit,
    private val onSystemPickerClick: () -> Unit,
    private val onFileClick: () -> Unit,
    private val onCheckboxClick: (MediaItem) -> Unit,
    private val onMediaPreviewClick: (MediaItem) -> Unit,
    private val maxSelection: Int = 10
) : ListAdapter<ImagePickerAdapter.ListItem, RecyclerView.ViewHolder>(DiffCallback()) {

    private val selectedItems = mutableListOf<MediaItem>()
    private val selectedUris = mutableSetOf<Uri>()

    companion object {
        private const val VIEW_TYPE_CAMERA = 0
        private const val VIEW_TYPE_SYSTEM_PICKER = 1
        private const val VIEW_TYPE_FILE = 2
        private const val VIEW_TYPE_MEDIA = 3

        // Сколько служебных плиток впереди
        private const val LEADING_TILES = 3
    }

    sealed class ListItem {
        data object Camera : ListItem()
        data object SystemPicker : ListItem()
        data object File : ListItem()
        data class Media(val item: MediaItem) : ListItem()
    }

    override fun getItemViewType(position: Int): Int {
        return when (getItem(position)) {
            is ListItem.Camera -> VIEW_TYPE_CAMERA
            is ListItem.SystemPicker -> VIEW_TYPE_SYSTEM_PICKER
            is ListItem.File -> VIEW_TYPE_FILE
            is ListItem.Media -> VIEW_TYPE_MEDIA
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        val inflater = LayoutInflater.from(parent.context)
        return when (viewType) {
            VIEW_TYPE_CAMERA -> CameraViewHolder(
                ItemCameraButtonBinding.inflate(inflater, parent, false)
            )
            VIEW_TYPE_SYSTEM_PICKER -> SystemPickerViewHolder(
                ItemSystemPickerButtonBinding.inflate(inflater, parent, false)
            )
            VIEW_TYPE_FILE -> FileViewHolder(
                ItemFileButtonBinding.inflate(inflater, parent, false)
            )
            else -> MediaViewHolder(
                ItemImagePickerBinding.inflate(inflater, parent, false)
            )
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        when (val item = getItem(position)) {
            is ListItem.Camera -> (holder as CameraViewHolder).bind()
            is ListItem.SystemPicker -> (holder as SystemPickerViewHolder).bind()
            is ListItem.File -> (holder as FileViewHolder).bind()
            is ListItem.Media -> (holder as MediaViewHolder).bind(item.item)
        }
    }

    inner class CameraViewHolder(
        private val binding: ItemCameraButtonBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind() {
            binding.root.contentDescription = binding.root.context.getString(R.string.camera)
            binding.root.setOnClickListener { onCameraClick() }
        }
    }

    inner class SystemPickerViewHolder(
        private val binding: ItemSystemPickerButtonBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind() {
            binding.root.contentDescription = binding.root.context.getString(R.string.gallery_button)
            binding.root.setOnClickListener { onSystemPickerClick() }
        }
    }

    inner class FileViewHolder(
        private val binding: ItemFileButtonBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind() {
            binding.root.contentDescription = binding.root.context.getString(R.string.file_button)
            binding.root.setOnClickListener { onFileClick() }
        }
    }

    inner class MediaViewHolder(
        private val binding: ItemImagePickerBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MediaItem) {
            // Превью: для видео — кадр через VideoFrameDecoder, для фото — обычная загрузка
            binding.imageView.load(item.uri) {
                crossfade(true)
                placeholder(R.color.surface_container_high)
                error(R.color.surface_container_high)
                if (item.isVideo) {
                    decoderFactory(VideoFrameDecoder.Factory())
                }
            }

            // Видео-оверлей
            if (item.isVideo) {
                binding.playIcon.visibility = View.VISIBLE
                binding.durationLabel.visibility = View.VISIBLE
                binding.durationLabel.text = formatDuration(item.durationMs)
            } else {
                binding.playIcon.visibility = View.GONE
                binding.durationLabel.visibility = View.GONE
            }

            val isSelected = selectedUris.contains(item.uri)
            val selectionIndex = selectedItems.indexOfFirst { it.uri == item.uri }
            val typeLabel = binding.root.context.getString(
                if (item.isVideo) R.string.media_video_label else R.string.media_photo_label
            )
            val displayName = item.displayName.ifBlank { typeLabel }
            binding.cardView.contentDescription = binding.root.context.getString(
                if (item.isVideo) R.string.cd_media_preview_video else R.string.cd_media_preview_photo,
                displayName
            )
            binding.checkboxTouchTarget.contentDescription = binding.root.context.getString(
                if (isSelected) R.string.cd_media_deselect else R.string.cd_media_select,
                displayName
            )

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

            // Иконка ножниц для отредактированных картинок
            binding.editedIndicator.visibility =
                if (!item.isVideo && MediaEditCache.has(item.uri)) View.VISIBLE else View.GONE

            binding.cardView.setOnClickListener { onMediaPreviewClick(item) }
            binding.checkboxTouchTarget.setOnClickListener { toggleSelection(item) }
        }

        private fun toggleSelection(item: MediaItem) {
            if (selectedUris.contains(item.uri)) {
                selectedUris.remove(item.uri)
                selectedItems.removeIf { it.uri == item.uri }
            } else {
                if (selectedItems.size >= maxSelection) return
                selectedUris.add(item.uri)
                selectedItems.add(item)
            }
            notifySelectionChanged()
            onCheckboxClick(item)
        }
    }

    fun getSelectedUrisForSending(): List<Uri> = selectedItems.map { it.uri }

    fun getSelectedItems(): List<MediaItem> = selectedItems.toList()

    fun getSelectionCount(): Int = selectedItems.size

    fun clearSelection() {
        if (selectedItems.isEmpty()) return
        selectedItems.clear()
        selectedUris.clear()
        notifyItemRangeChanged(LEADING_TILES, currentList.size - LEADING_TILES)
    }

    /**
     * Восстанавливает выбор из заданного списка URI с сохранением порядка.
     * Используется при возврате из редактора медиа.
     */
    fun setSelectionFromUris(uris: List<Uri>) {
        selectedItems.clear()
        selectedUris.clear()
        if (uris.isEmpty()) {
            notifyItemRangeChanged(LEADING_TILES, currentList.size - LEADING_TILES)
            return
        }
        // Собираем MediaItem'ы из текущего списка по URI с сохранением порядка из uris
        val byUri = currentList
            .asSequence()
            .filterIsInstance<ListItem.Media>()
            .associate { it.item.uri to it.item }
        for (u in uris) {
            val mi = byUri[u] ?: continue
            if (selectedItems.size >= maxSelection) break
            selectedItems.add(mi)
            selectedUris.add(u)
        }
        notifyItemRangeChanged(LEADING_TILES, currentList.size - LEADING_TILES)
    }

    /**
     * Уведомляет адаптер о возможном изменении содержимого MediaEditCache —
     * нужно перерисовать иконки ножниц.
     */
    fun refreshEditedIndicators() {
        notifyItemRangeChanged(LEADING_TILES, currentList.size - LEADING_TILES)
    }

    /** Все URI картинок (не видео) в галерее в текущем порядке. */
    fun getAllImageUris(): List<Uri> = currentList
        .asSequence()
        .filterIsInstance<ListItem.Media>()
        .filter { !it.item.isVideo }
        .map { it.item.uri }
        .toList()

    /** Все URI видео. */
    fun getAllVideoUris(): List<Uri> = currentList
        .asSequence()
        .filterIsInstance<ListItem.Media>()
        .filter { it.item.isVideo }
        .map { it.item.uri }
        .toList()

    private fun notifySelectionChanged() {
        notifyItemRangeChanged(LEADING_TILES, currentList.size - LEADING_TILES)
    }

    /**
     * Устанавливает медиа в адаптер.
     * Список начинается со служебных плиток [Camera, SystemPicker, File], затем медиа.
     */
    fun setMedia(items: List<MediaItem>) {
        val list = mutableListOf<ListItem>(
            ListItem.Camera,
            ListItem.SystemPicker,
            ListItem.File
        )
        list.addAll(items.map { ListItem.Media(it) })
        submitList(list)
    }

    private fun formatDuration(durationMs: Long): String {
        val totalSeconds = (durationMs / 1000L).coerceAtLeast(0L)
        val hours = totalSeconds / 3600
        val minutes = (totalSeconds % 3600) / 60
        val seconds = totalSeconds % 60
        return if (hours > 0) {
            String.format("%d:%02d:%02d", hours, minutes, seconds)
        } else {
            String.format("%d:%02d", minutes, seconds)
        }
    }

    class DiffCallback : DiffUtil.ItemCallback<ListItem>() {
        override fun areItemsTheSame(oldItem: ListItem, newItem: ListItem): Boolean {
            return when {
                oldItem is ListItem.Camera && newItem is ListItem.Camera -> true
                oldItem is ListItem.SystemPicker && newItem is ListItem.SystemPicker -> true
                oldItem is ListItem.File && newItem is ListItem.File -> true
                oldItem is ListItem.Media && newItem is ListItem.Media ->
                    oldItem.item.uri == newItem.item.uri
                else -> false
            }
        }

        override fun areContentsTheSame(oldItem: ListItem, newItem: ListItem): Boolean {
            return oldItem == newItem
        }
    }
}

/**
 * Модель медиа-элемента (фото или видео) для пикера.
 */
data class MediaItem(
    val uri: Uri,
    val id: Long,
    val dateAdded: Long,
    val displayName: String = "",
    val isVideo: Boolean = false,
    val durationMs: Long = 0L,
    val mimeType: String? = null
)
