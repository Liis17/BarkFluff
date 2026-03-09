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
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * BottomSheet для выбора изображений с устройства.
 *
 * Особенности:
 * - Анимация выезда снизу с интерполятором FastOutSlowIn
 * - Сетка изображений 3 колонки
 * - Множественный выбор до 10 изображений
 * - Кнопка камеры в начале сетки
 * - Раздельные зоны: галочка = выбор, превью = кропер
 * - Поле для подписи + кнопка отправки внизу
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

    // Pending crop item
    private var pendingCropItem: ImageItem? = null

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

    // Лаунчер для UCrop
    private val uCropLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        val item = pendingCropItem ?: return@registerForActivityResult
        pendingCropItem = null

        if (result.resultCode == Activity.RESULT_OK && result.data != null) {
            val croppedUri = UCrop.getOutput(result.data!!)
            if (croppedUri != null) {
                adapter.setCroppedUri(item.uri, croppedUri)
                adapter.selectItem(item)
                updateSelectionUI()
            }
        } else if (result.resultCode == UCrop.RESULT_ERROR && result.data != null) {
            val error = UCrop.getError(result.data!!)
            Log.e(TAG, "UCrop error", error)
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

        cleanOldCropFiles()
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
            onCheckboxClick = { updateSelectionUI() },
            onImagePreviewClick = { item -> openCropper(item) },
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
        binding.inputBar.visibility = View.GONE
    }

    private fun loadImages() {
        binding.permissionDeniedLayout.visibility = View.GONE
        binding.loadingProgress.visibility = View.VISIBLE
        binding.imagesRecyclerView.visibility = View.GONE
        binding.emptyStateLayout.visibility = View.GONE
        binding.inputBar.visibility = View.GONE

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
        binding.inputBar.visibility = View.VISIBLE
        adapter.setImages(images)
    }

    private fun showEmptyState() {
        binding.loadingProgress.visibility = View.GONE
        binding.imagesRecyclerView.visibility = View.GONE
        binding.emptyStateLayout.visibility = View.VISIBLE
        binding.inputBar.visibility = View.GONE
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

    private fun openCropper(item: ImageItem) {
        pendingCropItem = item

        val destinationUri = Uri.fromFile(
            File(requireContext().cacheDir, "crop_${item.id}_${System.currentTimeMillis()}.jpg")
        )

        val options = UCrop.Options().apply {
            setCompressionFormat(android.graphics.Bitmap.CompressFormat.JPEG)
            setCompressionQuality(95)
            setFreeStyleCropEnabled(true)
            setToolbarColor(requireContext().getColor(android.R.color.white))
            setStatusBarColor(requireContext().getColor(android.R.color.black))
            setActiveControlsWidgetColor(requireContext().getColor(android.R.color.black))
        }

        val uCrop = UCrop.of(item.uri, destinationUri)
            .withOptions(options)

        uCropLauncher.launch(uCrop.getIntent(requireContext()))
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
        val count = adapter.getSelectionCount()
        Log.d(TAG, "sendSelected: $count items selected")
        if (count == 0) return

        val uris = adapter.getSelectedUrisForSending()
        val captionText = binding.captionEditText.text.toString().trim()
        Log.d(TAG, "sendSelected: uris=$uris, sendAsFile=$sendAsFile, sendSeparately=$sendSeparately, caption=$captionText")

        onResult?.invoke(
            ImagePickerResult(
                uris = uris,
                sendAsFile = sendAsFile,
                sendSeparately = sendSeparately,
                fromCamera = false,
                captionText = captionText
            )
        )
        dismiss()
    }

    /**
     * Удаляет старые crop-файлы (старше 1 часа) при открытии пикера.
     */
    private fun cleanOldCropFiles() {
        try {
            val cacheDir = requireContext().cacheDir
            val oneHourAgo = System.currentTimeMillis() - 3600_000
            cacheDir.listFiles()?.filter {
                it.name.startsWith("crop_") && it.lastModified() < oneHourAgo
            }?.forEach { it.delete() }
        } catch (e: Exception) {
            Log.e(TAG, "Error cleaning old crop files", e)
        }
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
    val fromCamera: Boolean = false,
    val captionText: String = ""
)
