package com.barkfluff.client.picker

import android.Manifest
import android.content.ContentUris
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.MediaStore
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.PopupMenu
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import com.barkfluff.client.PreviewImageActivity
import com.barkfluff.client.PreviewVideoActivity
import com.barkfluff.client.R
import com.barkfluff.client.adapter.ImagePickerAdapter
import com.barkfluff.client.adapter.MediaItem
import com.barkfluff.client.databinding.BottomSheetImagePickerBinding
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Полноэкранный BottomSheet для выбора фото и видео.
 *
 * Особенности:
 * - Сразу разворачивается на весь экран (STATE_EXPANDED, height=MATCH_PARENT)
 * - Сетка 3 колонки: первые 3 ячейки — Камера, Системный photo picker, Файлы; далее фото и видео из MediaStore
 * - Видео-плитки имеют первый кадр + иконку play и длительность
 * - Множественный выбор до 10 элементов с пронумерованным селектором
 * - Тап по превью (вне чекбокса) — открывает PreviewImage/VideoActivity
 * - Внизу всегда строка ввода подписи + кнопка отправки (отправка возможна только при наличии выбранных)
 * - Меню «3 точки»: Отправить без сжатия / Отправить без группировки
 */
class ImagePickerBottomSheet : BottomSheetDialogFragment() {

    private var _binding: BottomSheetImagePickerBinding? = null
    private val binding get() = _binding!!

    private lateinit var adapter: ImagePickerAdapter

    private var sendAsFile: Boolean = false
    private var sendSeparately: Boolean = false

    private var onResult: ((ImagePickerResult) -> Unit)? = null

    // Pending camera URI для full-res capture (FileProvider)
    private var pendingCameraUri: Uri? = null

    private val cameraLauncher = registerForActivityResult(
        ActivityResultContracts.TakePicture()
    ) { success ->
        val uri = pendingCameraUri
        pendingCameraUri = null
        if (success && uri != null) {
            // Открываем простой просмотр снимка; в будущем здесь будет редактирование/отправка.
            startActivity(PreviewImageActivity.createIntent(requireContext(), uri))
            dismiss()
        }
    }

    // Системный photo picker — фото и видео в одном диалоге
    private val systemPickerLauncher = registerForActivityResult(
        ActivityResultContracts.PickMultipleVisualMedia(MAX_SELECTION)
    ) { uris ->
        if (uris.isNotEmpty()) {
            onResult?.invoke(
                ImagePickerResult(
                    uris = uris,
                    sendAsFile = sendAsFile,
                    sendSeparately = sendSeparately,
                    fromCamera = false,
                    captionText = "",
                    isDocuments = false
                )
            )
            dismiss()
        }
    }

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.entries.all { it.value }
        if (allGranted) {
            loadMedia()
        } else {
            showPermissionDenied()
        }
    }

    private val cameraPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        if (isGranted) {
            doOpenCamera()
        } else {
            Toast.makeText(requireContext(), "Разрешение на камеру отклонено", Toast.LENGTH_SHORT).show()
        }
    }

    private val filePickerLauncher = registerForActivityResult(
        ActivityResultContracts.OpenMultipleDocuments()
    ) { uris ->
        if (uris.isNotEmpty()) {
            onResult?.invoke(
                ImagePickerResult(
                    uris = uris,
                    sendAsFile = false,
                    sendSeparately = false,
                    fromCamera = false,
                    captionText = "",
                    isDocuments = true
                )
            )
            dismiss()
        }
    }

    companion object {
        private const val TAG = "ImagePickerBottomSheet"
        const val MAX_SELECTION = 10

        fun newInstance(onResult: (ImagePickerResult) -> Unit): ImagePickerBottomSheet {
            return ImagePickerBottomSheet().apply {
                this.onResult = onResult
            }
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View {
        _binding = BottomSheetImagePickerBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        setupRecyclerView()
        setupButtons()
        setupAnimation()
        updateSelectionUI()

        checkPermissionsAndLoad()
    }

    override fun onStart() {
        super.onStart()
        val dialog = dialog as? com.google.android.material.bottomsheet.BottomSheetDialog ?: return
        val bottomSheet = dialog.findViewById<View>(com.google.android.material.R.id.design_bottom_sheet) ?: return

        // Фулскрин
        bottomSheet.layoutParams.height = ViewGroup.LayoutParams.MATCH_PARENT
        bottomSheet.requestLayout()

        val behavior = BottomSheetBehavior.from(bottomSheet)
        behavior.skipCollapsed = true
        behavior.state = BottomSheetBehavior.STATE_EXPANDED
    }

    private fun setupRecyclerView() {
        adapter = ImagePickerAdapter(
            onCameraClick = {
                // По запуску камеры сбрасываем текущее выделение в пикере
                adapter.clearSelection()
                updateSelectionUI()
                openCamera()
            },
            onSystemPickerClick = { openSystemPicker() },
            onFileClick = { openFilePicker() },
            onCheckboxClick = { updateSelectionUI() },
            onMediaPreviewClick = { item -> openPreview(item) },
            maxSelection = MAX_SELECTION
        )

        binding.imagesRecyclerView.apply {
            layoutManager = GridLayoutManager(requireContext(), 3)
            adapter = this@ImagePickerBottomSheet.adapter
        }
    }

    private fun setupButtons() {
        binding.menuButton.setOnClickListener { showOptionsMenu() }
        binding.sendButton.setOnClickListener { sendSelected() }
        binding.requestPermissionButton.setOnClickListener { requestPermissions() }
    }

    private fun setupAnimation() {
        dialog?.window?.attributes?.windowAnimations = R.style.ImagePickerAnimation
    }

    private fun checkPermissionsAndLoad() {
        if (hasMediaPermissions()) {
            loadMedia()
        } else {
            requestPermissions()
        }
    }

    private fun hasMediaPermissions(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            ContextCompat.checkSelfPermission(
                requireContext(),
                Manifest.permission.READ_MEDIA_IMAGES
            ) == PackageManager.PERMISSION_GRANTED &&
            ContextCompat.checkSelfPermission(
                requireContext(),
                Manifest.permission.READ_MEDIA_VIDEO
            ) == PackageManager.PERMISSION_GRANTED
        } else {
            ContextCompat.checkSelfPermission(
                requireContext(),
                Manifest.permission.READ_EXTERNAL_STORAGE
            ) == PackageManager.PERMISSION_GRANTED
        }
    }

    private fun requestPermissions() {
        val permissions = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            arrayOf(
                Manifest.permission.READ_MEDIA_IMAGES,
                Manifest.permission.READ_MEDIA_VIDEO
            )
        } else {
            arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
        }
        permissionLauncher.launch(permissions)
    }

    private fun showPermissionDenied() {
        binding.permissionDeniedLayout.visibility = View.VISIBLE
        binding.imagesRecyclerView.visibility = View.GONE
        binding.loadingProgress.visibility = View.GONE
        binding.emptyStateLayout.visibility = View.GONE
    }

    private fun loadMedia() {
        binding.permissionDeniedLayout.visibility = View.GONE
        binding.loadingProgress.visibility = View.VISIBLE
        binding.imagesRecyclerView.visibility = View.GONE
        binding.emptyStateLayout.visibility = View.GONE

        lifecycleScope.launch {
            try {
                val items = loadMediaFromMediaStore()
                showMedia(items)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading media", e)
                showEmptyState()
            }
        }
    }

    private suspend fun loadMediaFromMediaStore(): List<MediaItem> = withContext(Dispatchers.IO) {
        val results = mutableListOf<MediaItem>()
        val collectionUri = MediaStore.Files.getContentUri("external")

        val projection = arrayOf(
            MediaStore.Files.FileColumns._ID,
            MediaStore.Files.FileColumns.DATE_ADDED,
            MediaStore.Files.FileColumns.DISPLAY_NAME,
            MediaStore.Files.FileColumns.MEDIA_TYPE,
            MediaStore.Files.FileColumns.MIME_TYPE,
            MediaStore.Files.FileColumns.DURATION
        )
        val selection = "${MediaStore.Files.FileColumns.MEDIA_TYPE} IN (?, ?)"
        val selectionArgs = arrayOf(
            MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE.toString(),
            MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO.toString()
        )
        val sortOrder = "${MediaStore.Files.FileColumns.DATE_ADDED} DESC"

        requireContext().contentResolver.query(
            collectionUri, projection, selection, selectionArgs, sortOrder
        )?.use { cursor ->
            val idCol = cursor.getColumnIndexOrThrow(MediaStore.Files.FileColumns._ID)
            val dateCol = cursor.getColumnIndexOrThrow(MediaStore.Files.FileColumns.DATE_ADDED)
            val nameCol = cursor.getColumnIndexOrThrow(MediaStore.Files.FileColumns.DISPLAY_NAME)
            val typeCol = cursor.getColumnIndexOrThrow(MediaStore.Files.FileColumns.MEDIA_TYPE)
            val mimeCol = cursor.getColumnIndexOrThrow(MediaStore.Files.FileColumns.MIME_TYPE)
            val durationCol = cursor.getColumnIndexOrThrow(MediaStore.Files.FileColumns.DURATION)

            while (cursor.moveToNext()) {
                val id = cursor.getLong(idCol)
                val dateAdded = cursor.getLong(dateCol) * 1000L
                val name = cursor.getString(nameCol) ?: ""
                val mediaType = cursor.getInt(typeCol)
                val mime = cursor.getString(mimeCol)
                val duration = cursor.getLong(durationCol)
                val isVideo = mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO

                val itemUri = if (isVideo) {
                    ContentUris.withAppendedId(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, id)
                } else {
                    ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id)
                }

                results.add(
                    MediaItem(
                        uri = itemUri,
                        id = id,
                        dateAdded = dateAdded,
                        displayName = name,
                        isVideo = isVideo,
                        durationMs = if (isVideo) duration else 0L,
                        mimeType = mime
                    )
                )
            }
        }
        results
    }

    private fun showMedia(items: List<MediaItem>) {
        binding.loadingProgress.visibility = View.GONE
        binding.imagesRecyclerView.visibility = View.VISIBLE
        binding.emptyStateLayout.visibility = View.GONE
        adapter.setMedia(items)
    }

    private fun showEmptyState() {
        binding.loadingProgress.visibility = View.GONE
        binding.imagesRecyclerView.visibility = View.GONE
        // Даже на пустом MediaStore оставляем сетку с тремя служебными плитками
        binding.imagesRecyclerView.visibility = View.VISIBLE
        adapter.setMedia(emptyList())
    }

    private fun openCamera() {
        if (ContextCompat.checkSelfPermission(
                requireContext(),
                Manifest.permission.CAMERA
            ) == PackageManager.PERMISSION_GRANTED
        ) {
            doOpenCamera()
        } else {
            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
        }
    }

    private fun doOpenCamera() {
        try {
            val tempFile = File.createTempFile(
                "camera_${System.currentTimeMillis()}",
                ".jpg",
                requireContext().cacheDir
            )
            pendingCameraUri = androidx.core.content.FileProvider.getUriForFile(
                requireContext(),
                "${requireContext().packageName}.fileprovider",
                tempFile
            )
            cameraLauncher.launch(pendingCameraUri)
        } catch (e: Exception) {
            Log.e(TAG, "Error opening camera", e)
            Toast.makeText(requireContext(), "Не удалось открыть камеру", Toast.LENGTH_SHORT).show()
        }
    }

    private fun openSystemPicker() {
        try {
            systemPickerLauncher.launch(
                PickVisualMediaRequest(
                    ActivityResultContracts.PickVisualMedia.ImageAndVideo
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Error opening system photo picker", e)
            Toast.makeText(requireContext(), "Не удалось открыть галерею", Toast.LENGTH_SHORT).show()
        }
    }

    private fun openFilePicker() {
        try {
            filePickerLauncher.launch(arrayOf("*/*"))
        } catch (e: Exception) {
            Log.e(TAG, "Error opening file picker", e)
            Toast.makeText(requireContext(), "Не удалось открыть выбор файлов", Toast.LENGTH_SHORT).show()
        }
    }

    private fun openPreview(item: MediaItem) {
        val intent = if (item.isVideo) {
            PreviewVideoActivity.createIntent(requireContext(), item.uri)
        } else {
            PreviewImageActivity.createIntent(requireContext(), item.uri)
        }
        startActivity(intent)
    }

    private fun updateSelectionUI() {
        val count = adapter.getSelectionCount()

        if (count > 0) {
            binding.selectionCountTextView.visibility = View.VISIBLE
            binding.selectionCountTextView.text = getString(
                R.string.selected_count,
                count,
                MAX_SELECTION
            )
            binding.menuButton.visibility = View.VISIBLE
            binding.sendButton.isEnabled = true
            binding.sendButton.alpha = 1.0f
            binding.titleTextView.text = resources.getQuantityString(
                R.plurals.photos_selected,
                count,
                count
            )
        } else {
            binding.selectionCountTextView.visibility = View.GONE
            binding.menuButton.visibility = View.GONE
            binding.sendButton.isEnabled = false
            binding.sendButton.alpha = 0.5f
            binding.titleTextView.setText(R.string.select_media)
        }
    }

    private fun showOptionsMenu() {
        val popup = PopupMenu(requireContext(), binding.menuButton)
        popup.menuInflater.inflate(R.menu.image_picker_menu, popup.menu)

        popup.menu.findItem(R.id.action_send_as_file)?.isChecked = sendAsFile
        popup.menu.findItem(R.id.action_send_separately)?.isChecked = sendSeparately

        popup.setOnMenuItemClickListener { menuItem ->
            when (menuItem.itemId) {
                R.id.action_send_as_file -> {
                    sendAsFile = !sendAsFile
                    menuItem.isChecked = sendAsFile
                    true
                }
                R.id.action_send_separately -> {
                    sendSeparately = !sendSeparately
                    menuItem.isChecked = sendSeparately
                    true
                }
                else -> false
            }
        }
        popup.show()
    }

    private fun sendSelected() {
        val count = adapter.getSelectionCount()
        if (count == 0) return

        val uris = adapter.getSelectedUrisForSending()
        val captionText = binding.captionEditText.text.toString().trim()
        val selectedItems = adapter.getSelectedItems()
        val hasVideo = selectedItems.any { it.isVideo }

        onResult?.invoke(
            ImagePickerResult(
                uris = uris,
                sendAsFile = sendAsFile,
                sendSeparately = sendSeparately,
                fromCamera = false,
                captionText = captionText,
                isDocuments = false,
                hasVideo = hasVideo
            )
        )
        dismiss()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}

/**
 * Результат выбора медиа.
 */
data class ImagePickerResult(
    val uris: List<Uri>,
    val sendAsFile: Boolean,
    val sendSeparately: Boolean,
    val fromCamera: Boolean = false,
    val captionText: String = "",
    val isDocuments: Boolean = false,
    val hasVideo: Boolean = false
)
