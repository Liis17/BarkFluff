package com.barkfluff.client.picker

import android.Manifest
import android.app.Activity
import android.content.ContentUris
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.MediaStore
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.view.animation.PathInterpolator
import android.widget.PopupMenu
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import com.barkfluff.client.R
import com.barkfluff.client.adapter.ImageItem
import com.barkfluff.client.adapter.ImagePickerAdapter
import com.barkfluff.client.databinding.BottomSheetImagePickerBinding
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * BottomSheet для выбора изображений с устройства.
 *
 * Особенности:
 * - Анимация выезда снизу с интерполятором FastOutSlowIn
 * - Сетка изображений 3 колонки
 * - Множественный выбор до 10 изображений
 * - Кнопка камеры в начале сетки
 * - Меню с опциями: "Отправить как файл", "Отправить по отдельности"
 */
class ImagePickerBottomSheet : BottomSheetDialogFragment() {

    private var _binding: BottomSheetImagePickerBinding? = null
    private val binding get() = _binding!!

    private lateinit var adapter: ImagePickerAdapter

    // Опции отправки
    private var sendAsFile: Boolean = false
    private var sendSeparately: Boolean = false

    // Callback для возврата результата
    private var onResult: ((ImagePickerResult) -> Unit)? = null

    // Лаунчер для камеры
    private val cameraLauncher = registerForActivityResult(
        ActivityResultContracts.TakePicturePreview()
    ) { bitmap ->
        if (bitmap != null) {
            // Сохраняем bitmap во временный файл и возвращаем как Uri
            lifecycleScope.launch {
                val tempUri = saveBitmapToTempFile(bitmap)
                if (tempUri != null) {
                    onResult?.invoke(
                        ImagePickerResult(
                            uris = listOf(tempUri),
                            sendAsFile = false,
                            sendSeparately = false,
                            fromCamera = true
                        )
                    )
                    dismiss()
                }
            }
        }
    }

    // Лаунчер для запроса разрешений на хранилище
    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.entries.all { it.value }
        if (allGranted) {
            loadImages()
        } else {
            showPermissionDenied()
        }
    }

    // Лаунчер для запроса разрешения на камеру
    private val cameraPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        if (isGranted) {
            doOpenCamera()
        } else {
            Toast.makeText(requireContext(), "Разрешение на камеру отклонено", Toast.LENGTH_SHORT).show()
        }
    }

    companion object {
        private const val TAG = "ImagePickerBottomSheet"
        const val MAX_SELECTION = 10

        /**
         * Создаёт новый экземпляр BottomSheet с callback.
         */
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

        // Проверяем разрешения и загружаем изображения
        checkPermissionsAndLoad()
    }

    override fun onStart() {
        super.onStart()
        // Устанавливаем высоту bottom sheet на 50% от высоты экрана
        val dialog = dialog as? com.google.android.material.bottomsheet.BottomSheetDialog ?: return
        val bottomSheet = dialog.findViewById<View>(com.google.android.material.R.id.design_bottom_sheet) ?: return
        val behavior = BottomSheetBehavior.from(bottomSheet)

        // Устанавливаем соотношение для полураскрытого состояния (50% экрана)
        behavior.halfExpandedRatio = 0.5f
        behavior.state = BottomSheetBehavior.STATE_HALF_EXPANDED

        // Запрещаем полное раскрытие
        behavior.isFitToContents = true
    }

    private fun setupRecyclerView() {
        adapter = ImagePickerAdapter(
            onCameraClick = { openCamera() },
            onImageClick = { updateSelectionUI() },
            maxSelection = MAX_SELECTION
        )

        binding.imagesRecyclerView.apply {
            layoutManager = GridLayoutManager(requireContext(), 3)
            adapter = this@ImagePickerBottomSheet.adapter
        }
    }

    private fun setupButtons() {
        // Меню с опциями
        binding.menuButton.setOnClickListener { showOptionsMenu() }

        // Кнопка отправки
        binding.sendButton.setOnClickListener { sendSelected() }

        // Кнопка запроса разрешения
        binding.requestPermissionButton.setOnClickListener {
            requestPermissions()
        }
    }

    private fun setupAnimation() {
        // FastOutSlowIn интерполятор для плавной анимации
        dialog?.window?.attributes?.windowAnimations = R.style.ImagePickerAnimation
    }

    private fun checkPermissionsAndLoad() {
        if (hasStoragePermission()) {
            loadImages()
        } else {
            requestPermissions()
        }
    }

    private fun hasStoragePermission(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            ContextCompat.checkSelfPermission(
                requireContext(),
                Manifest.permission.READ_MEDIA_IMAGES
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
            arrayOf(Manifest.permission.READ_MEDIA_IMAGES)
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

    private fun loadImages() {
        binding.permissionDeniedLayout.visibility = View.GONE
        binding.loadingProgress.visibility = View.VISIBLE
        binding.imagesRecyclerView.visibility = View.GONE
        binding.emptyStateLayout.visibility = View.GONE

        lifecycleScope.launch {
            try {
                val images = loadImagesFromMediaStore()

                if (images.isEmpty()) {
                    showEmptyState()
                } else {
                    showImages(images)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading images", e)
                showEmptyState()
            }
        }
    }

    private suspend fun loadImagesFromMediaStore(): List<ImageItem> = withContext(Dispatchers.IO) {
        val images = mutableListOf<ImageItem>()
        val uri = MediaStore.Images.Media.EXTERNAL_CONTENT_URI
        val projection = arrayOf(
            MediaStore.Images.Media._ID,
            MediaStore.Images.Media.DATE_ADDED,
            MediaStore.Images.Media.DISPLAY_NAME
        )
        val sortOrder = "${MediaStore.Images.Media.DATE_ADDED} DESC"

        requireContext().contentResolver.query(uri, projection, null, null, sortOrder)?.use { cursor ->
            val idColumn = cursor.getColumnIndexOrThrow(MediaStore.Images.Media._ID)
            val dateColumn = cursor.getColumnIndexOrThrow(MediaStore.Images.Media.DATE_ADDED)
            val nameColumn = cursor.getColumnIndexOrThrow(MediaStore.Images.Media.DISPLAY_NAME)

            while (cursor.moveToNext()) {
                val id = cursor.getLong(idColumn)
                val dateAdded = cursor.getLong(dateColumn) * 1000 // Конвертируем в миллисекунды
                val displayName = cursor.getString(nameColumn) ?: ""

                val contentUri = ContentUris.withAppendedId(uri, id)

                images.add(
                    ImageItem(
                        uri = contentUri,
                        id = id,
                        dateAdded = dateAdded,
                        displayName = displayName
                    )
                )
            }
        }

        images
    }

    private fun showImages(images: List<ImageItem>) {
        binding.loadingProgress.visibility = View.GONE
        binding.imagesRecyclerView.visibility = View.VISIBLE
        binding.emptyStateLayout.visibility = View.GONE
        adapter.setImages(images)
    }

    private fun showEmptyState() {
        binding.loadingProgress.visibility = View.GONE
        binding.imagesRecyclerView.visibility = View.GONE
        binding.emptyStateLayout.visibility = View.VISIBLE
    }

    private fun openCamera() {
        // Проверяем разрешение на камеру
        if (ContextCompat.checkSelfPermission(
                requireContext(),
                Manifest.permission.CAMERA
            ) == PackageManager.PERMISSION_GRANTED
        ) {
            doOpenCamera()
        } else {
            // Запрашиваем разрешение
            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
        }
    }

    private fun doOpenCamera() {
        try {
            cameraLauncher.launch(null)
        } catch (e: Exception) {
            Log.e(TAG, "Error opening camera", e)
            Toast.makeText(requireContext(), "Не удалось открыть камеру", Toast.LENGTH_SHORT).show()
        }
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
            binding.sendButton.visibility = View.VISIBLE
            binding.titleTextView.text = resources.getQuantityString(
                R.plurals.photos_selected,
                count,
                count
            )
        } else {
            binding.selectionCountTextView.visibility = View.GONE
            binding.menuButton.visibility = View.GONE
            binding.sendButton.visibility = View.GONE
            binding.titleTextView.setText(R.string.select_photos)
        }
    }

    private fun showOptionsMenu() {
        val popup = PopupMenu(requireContext(), binding.menuButton)
        popup.menuInflater.inflate(R.menu.image_picker_menu, popup.menu)

        // Устанавливаем текущие состояния
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
        val selectedItems = adapter.getSelectedItems()
        Log.d(TAG, "sendSelected: ${selectedItems.size} items selected")
        if (selectedItems.isEmpty()) return

        val uris = selectedItems.map { it.uri }
        Log.d(TAG, "sendSelected: uris=$uris, sendAsFile=$sendAsFile, sendSeparately=$sendSeparately")

        onResult?.invoke(
            ImagePickerResult(
                uris = uris,
                sendAsFile = sendAsFile,
                sendSeparately = sendSeparately,
                fromCamera = false
            )
        )
        dismiss()
    }

    private suspend fun saveBitmapToTempFile(bitmap: android.graphics.Bitmap): Uri? {
        return withContext(Dispatchers.IO) {
            try {
                val tempFile = java.io.File.createTempFile(
                    "camera_${System.currentTimeMillis()}",
                    ".jpg",
                    requireContext().cacheDir
                )
                java.io.FileOutputStream(tempFile).use { out ->
                    bitmap.compress(android.graphics.Bitmap.CompressFormat.JPEG, 90, out)
                }
                androidx.core.content.FileProvider.getUriForFile(
                    requireContext(),
                    "${requireContext().packageName}.fileprovider",
                    tempFile
                )
            } catch (e: Exception) {
                Log.e(TAG, "Error saving bitmap to temp file", e)
                null
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}

/**
 * Результат выбора изображений.
 */
data class ImagePickerResult(
    val uris: List<Uri>,
    val sendAsFile: Boolean,
    val sendSeparately: Boolean,
    val fromCamera: Boolean = false
)
