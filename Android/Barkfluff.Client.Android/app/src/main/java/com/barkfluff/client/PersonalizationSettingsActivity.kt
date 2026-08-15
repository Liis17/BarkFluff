package com.barkfluff.client

import android.graphics.Bitmap
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.graphics.RenderEffect
import android.graphics.Shader
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import coil.load
import com.barkfluff.client.adapter.ChatBackgroundAdapter
import com.barkfluff.client.adapter.ChatBackgroundItem
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityPersonalizationSettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import barkfluff.files.FilesApiOuterClass
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File

/**
 * Экран настроек персонализации (редизайн).
 *
 * Структура (сверху вниз):
 *  1. Блок превью — 260dp, 5 пузырей, фон + затемнение + блюр
 *  2. Блок настроек отображения:
 *       - Слайдер закругления (0..30 dp)
 *       - Тогл размытия + слайдер силы (1..25, скрыт если тогл выкл)
 *       - Слайдер затемнения (0..100 %)
 *  3. Блок фонов чата — сетка 3 колонки + кнопка добавить
 */
class PersonalizationSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityPersonalizationSettingsBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var backgroundAdapter: ChatBackgroundAdapter

    /** Список всех пузырей превью для пакетного обновления радиуса */
    private val previewBubbles get() = listOf(
        binding.previewMsg1,
        binding.previewMsg2,
        binding.previewMsg3,
        binding.previewMsg4,
        binding.previewMsg5
    )

    /** Локальный список fileId фонов (синхронизируется с сервером) */
    private val backgroundFileIds = mutableListOf<String>()
    private var backgroundsExpanded = false

    private var currentPosterFileId: String = ""

    companion object {
        private const val TAG = "PersonalizationActivity"
        private const val MAX_COLLAPSED_BACKGROUNDS = 9
    }

    private val pickImageLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        if (uri != null) uploadBackgroundImage(uri)
    }

    private val pickPosterLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        if (uri != null) startPosterCrop(uri)
    }

    private val ucropPosterLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            val uri = UCrop.getOutput(result.data!!)
            if (uri != null) uploadPosterFromUri(uri)
        } else if (result.resultCode == UCrop.RESULT_ERROR) {
            val error = UCrop.getError(result.data!!)
            Toast.makeText(this, getString(R.string.settings_error_detail, error?.message.orEmpty()), Toast.LENGTH_SHORT).show()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPersonalizationSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        globalParam = GlobalParam(this)
        grpcManager = app.grpcManager
        chatRepository = ChatRepository(this, grpcManager)

        setupToolbar()
        setupPoster()
        setupCornerRadiusSlider()
        setupBlurToggle()
        setupDimSlider()
        setupFolderSettings()
        setupMainTabSettings()
        setupOnlineTimeSettings()
        setupStickerSizeSlider()
        setupBackgroundsGrid()
        loadPersonalizationFromServer()
    }

    private fun setupFolderSettings() {
        binding.switchCompactFolders.isChecked = globalParam.compactFolders
        binding.switchCompactFolders.setOnCheckedChangeListener { _, isChecked ->
            globalParam.compactFolders = isChecked
        }
        binding.switchFolderTabsNoOutline.isChecked = globalParam.folderTabsNoOutline
        binding.switchFolderTabsNoOutline.setOnCheckedChangeListener { _, isChecked ->
            globalParam.folderTabsNoOutline = isChecked
        }
        binding.switchExcludeFromAll.isChecked = globalParam.excludeFolderChatsFromAll
        binding.switchExcludeFromAll.setOnCheckedChangeListener { _, isChecked ->
            globalParam.excludeFolderChatsFromAll = isChecked
        }
    }

    private fun setupMainTabSettings() {
        globalParam.mainTabChatsVisible = true
        globalParam.mainTabProfileVisible = true

        binding.switchMainTabChats.isChecked = true
        binding.switchMainTabCalls.isChecked = globalParam.mainTabCallsVisible
        binding.switchMainTabProfile.isChecked = true

        binding.switchMainTabChats.isEnabled = false
        binding.switchMainTabProfile.isEnabled = false

        binding.switchMainTabCalls.setOnCheckedChangeListener { _, isChecked ->
            globalParam.mainTabCallsVisible = isChecked
        }
    }

    private fun setupOnlineTimeSettings() {
        binding.switchRelativeOnlineTime.isChecked = globalParam.relativeOnlineTime
        binding.switchRelativeOnlineTime.setOnCheckedChangeListener { _, isChecked ->
            globalParam.relativeOnlineTime = isChecked
        }
    }

    private fun setupStickerSizeSlider() {
        val saved = globalParam.chatStickerSizeDp
        binding.stickerSizeSlider.value = saved.toFloat()
        updateStickerSizePreview(saved)

        binding.stickerSizeSlider.addOnChangeListener { _, value, fromUser ->
            val size = value.toInt()
            updateStickerSizePreview(size)
            if (fromUser) globalParam.chatStickerSizeDp = size
        }
    }

    private fun updateStickerSizePreview(sizeDp: Int) {
        binding.stickerSizeValue.text = getString(R.string.personalization_sticker_size_value, sizeDp)
        val sizePx = (sizeDp * resources.displayMetrics.density + 0.5f).toInt()
        val params = binding.stickerSizePreviewImage.layoutParams
        params.width = sizePx
        params.height = sizePx
        binding.stickerSizePreviewImage.layoutParams = params
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener { finish() }
    }

    // ─── Блок 0: постер профиля ──────────────────────────────────────────

    private fun setupPoster() {
        binding.buttonSetPoster.setOnClickListener {
            pickPosterLauncher.launch("image/*")
        }
        loadCurrentUserForPoster()
    }

    private fun loadCurrentUserForPoster() {
        lifecycleScope.launch {
            try {
                val result = (application as BarkFluffApplication).grpcManager.getCurrentUserData()
                if (result.isSuccess) {
                    val userData = result.getOrNull() ?: return@launch
                    val globalParam = GlobalParam(this@PersonalizationSettingsActivity)
                    val displayName = "${userData.firstName} ${userData.lastName}".trim()
                    currentPosterFileId = userData.profilePosterFileId

                    binding.profilePreviewFullName.text = displayName.ifEmpty { getString(R.string.profile_user_placeholder) }
                    binding.profilePreviewUsername.text =
                        if (userData.username.isNotEmpty()) {
                            getString(R.string.register_username_format, userData.username)
                        } else ""

                    val avatarUrl = userData.profilePictureUrl
                    if (avatarUrl.isNotBlank()) {
                        AvatarLoader.load(
                            imageView = binding.profilePreviewAvatarImage,
                            placeholderView = binding.profilePreviewAvatarPlaceholder,
                            avatarUrl = avatarUrl,
                            displayName = displayName,
                            userId = userData.userId
                        )
                    } else if (userData.profilePictureFileId.isNotEmpty()) {
                        val fileId = userData.profilePictureFileId
                        AvatarLoader.loadByFileId(
                            binding.profilePreviewAvatarImage,
                            binding.profilePreviewAvatarPlaceholder,
                            fileId, displayName, userData.userId, size = 192
                        ) {
                            grpcManager.getFileDownloadUrl(fileId).getOrNull()
                        }
                    } else {
                        AvatarLoader.showPlaceholder(
                            binding.profilePreviewAvatarPlaceholder, displayName, userData.userId
                        )
                        binding.profilePreviewAvatarImage.visibility = View.GONE
                    }

                    loadPosterPreview()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка загрузки данных пользователя", e)
            }
        }
    }

    private fun loadPosterPreview() {
        if (currentPosterFileId.isEmpty()) {
            binding.profilePosterPreviewImage.visibility = View.GONE
            binding.profilePosterPreviewPlaceholder.visibility = View.VISIBLE
            return
        }

        // Сначала проверяем кэш URL
        val cachedUrl = AvatarLoader.urlCache[currentPosterFileId]
            ?: AvatarLoader.getUrlFromCache(currentPosterFileId)

        if (cachedUrl != null) {
            binding.profilePosterPreviewPlaceholder.visibility = View.GONE
            binding.profilePosterPreviewImage.visibility = View.VISIBLE
            binding.profilePosterPreviewImage.load(
                cachedUrl, AvatarLoader.getImageLoader(this)
            ) { crossfade(true) }
            return
        }

        lifecycleScope.launch {
            try {
                val urlResult = grpcManager.getFileDownloadUrl(currentPosterFileId)
                if (urlResult.isSuccess) {
                    val url = urlResult.getOrNull() ?: return@launch
                    // Кэшируем URL
                    AvatarLoader.urlCache[currentPosterFileId] = url
                    AvatarLoader.putUrlInCache(currentPosterFileId, url)

                    binding.profilePosterPreviewPlaceholder.visibility = View.GONE
                    binding.profilePosterPreviewImage.visibility = View.VISIBLE
                    binding.profilePosterPreviewImage.load(
                        url, AvatarLoader.getImageLoader(this@PersonalizationSettingsActivity)
                    ) { crossfade(true) }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка загрузки постера", e)
            }
        }
    }

    private fun startPosterCrop(uri: Uri) {
        val destUri = Uri.fromFile(File(cacheDir, "cropped_poster_${System.currentTimeMillis()}.jpg"))
        val options = UCrop.Options().apply {
            setCompressionFormat(Bitmap.CompressFormat.JPEG)
            setCompressionQuality(90)
            setToolbarColor(getColor(android.R.color.white))
            setStatusBarColor(getColor(android.R.color.black))
            setFreeStyleCropEnabled(false)
        }
        UCrop.of(uri, destUri)
            .withAspectRatio(3f, 1f)
            .withMaxResultSize(2400, 800)
            .withOptions(options)
            .let { ucropPosterLauncher.launch(it.getIntent(this)) }
    }

    private fun uploadPosterFromUri(uri: Uri) {
        lifecycleScope.launch {
            try {
                binding.buttonSetPoster.isEnabled = false
                binding.buttonSetPoster.text = getString(R.string.loading_dots)

                // UCrop уже сохранил обрезанное изображение как JPEG 90%, читаем байты напрямую
                val jpegBytes = withContext(Dispatchers.IO) {
                    contentResolver.openInputStream(uri)?.use { it.readBytes() }
                } ?: run {
                    Toast.makeText(this@PersonalizationSettingsActivity, R.string.account_image_read_error, Toast.LENGTH_SHORT).show()
                    return@launch
                }

                val uploadResult = grpcManager.uploadProfilePoster(jpegBytes)
                if (uploadResult.isFailure) {
                    Toast.makeText(this@PersonalizationSettingsActivity, R.string.personalization_poster_upload_failed, Toast.LENGTH_SHORT).show()
                    return@launch
                }

                val fileId = uploadResult.getOrNull()!!
                val setResult = grpcManager.setProfilePoster(fileId)
                if (setResult.isSuccess) {
                    currentPosterFileId = fileId
                    loadPosterPreview()
                    Toast.makeText(this@PersonalizationSettingsActivity, R.string.personalization_poster_installed, Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this@PersonalizationSettingsActivity, R.string.personalization_poster_set_failed, Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка загрузки постера", e)
                Toast.makeText(this@PersonalizationSettingsActivity, getString(R.string.settings_error_detail, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
            } finally {
                binding.buttonSetPoster.isEnabled = true
                binding.buttonSetPoster.text = getString(R.string.personalization_set_new_poster)
            }
        }
    }

    // ─── Блок 1: обновление превью ──────────────────────────────────────

    /** Устанавливает радиус у всех 5 пузырей превью */
    private fun updatePreviewCorners(dp: Float) {
        val px = dp * resources.displayMetrics.density
        previewBubbles.forEach { it.radius = px }
    }

    /** Загружает фоновую картинку в превью, потом применяет блюр */
    private fun updatePreviewBackground(fileId: String) {
        if (fileId.isEmpty()) {
            binding.previewBackgroundImage.visibility = View.GONE
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                binding.previewBackgroundImage.setRenderEffect(null)
            }
            updatePreviewDim()
            return
        }
        binding.previewBackgroundImage.visibility = View.VISIBLE
        lifecycleScope.launch {
            val cached = withContext(Dispatchers.IO) { FileCache.getFile(fileId) }
            if (cached != null && cached.exists()) {
                binding.previewBackgroundImage.load(
                    cached, AvatarLoader.getImageLoader(this@PersonalizationSettingsActivity)
                ) { crossfade(true) }
            } else {
                val url = chatRepository.getFileDownloadUrl(fileId).getOrNull()
                if (url != null) {
                    binding.previewBackgroundImage.load(
                        url, AvatarLoader.getImageLoader(this@PersonalizationSettingsActivity)
                    ) { crossfade(true) }
                }
            }
            updatePreviewBlur()
            updatePreviewDim()
        }
    }

    /** Применяет RenderEffect blur (API 31+) к фоновому изображению превью */
    private fun updatePreviewBlur() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val visible = binding.previewBackgroundImage.visibility == View.VISIBLE
            if (globalParam.chatBackgroundBlur && visible) {
                val r = globalParam.chatBackgroundBlurRadius.toFloat()
                binding.previewBackgroundImage.setRenderEffect(
                    RenderEffect.createBlurEffect(r, r, Shader.TileMode.CLAMP)
                )
            } else {
                binding.previewBackgroundImage.setRenderEffect(null)
            }
        }
    }

    /** Обновляет alpha затемняющего оверлея исходя из текущего значения dim (0–100%) */
    private fun updatePreviewDim() {
        val pct = globalParam.chatBackgroundDim
        if (pct == 0) {
            binding.previewDimOverlay.background = null
        } else {
            val alpha = (pct / 100f * 255).toInt().coerceIn(0, 255)
            // Используем цвет фона окна из темы (светлый/тёмный в зависимости от темы)
            val typedValue = android.util.TypedValue()
            theme.resolveAttribute(android.R.attr.colorBackground, typedValue, true)
            val bgColor = typedValue.data
            val dimColor = android.graphics.Color.argb(
                alpha,
                android.graphics.Color.red(bgColor),
                android.graphics.Color.green(bgColor),
                android.graphics.Color.blue(bgColor)
            )
            binding.previewDimOverlay.setBackgroundColor(dimColor)
        }
    }

    // ─── Блок 2: слайдер закругления ─────────────────────────────────────────

    private fun setupCornerRadiusSlider() {
        val saved = globalParam.chatMessageCornerRadius
        binding.cornerRadiusSlider.value = saved.toFloat()
        binding.cornerRadiusValue.text = saved.toString()
        updatePreviewCorners(saved.toFloat())

        binding.cornerRadiusSlider.addOnChangeListener { _, value, fromUser ->
            binding.cornerRadiusValue.text = value.toInt().toString()
            updatePreviewCorners(value)
            if (fromUser) globalParam.chatMessageCornerRadius = value.toInt()
        }
    }

    // ─── Блок 2: тогл размытия + слайдер силы ────────────────────────────────

    private fun setupBlurToggle() {
        val blurEnabled = globalParam.chatBackgroundBlur
        val blurRadius = globalParam.chatBackgroundBlurRadius

        binding.switchBlurBackground.isChecked = blurEnabled
        binding.blurRadiusSection.visibility = if (blurEnabled) View.VISIBLE else View.GONE
        binding.blurRadiusSlider.value = blurRadius.toFloat()
        binding.blurRadiusValue.text = blurRadius.toString()

        binding.switchBlurBackground.setOnCheckedChangeListener { _, isChecked ->
            globalParam.chatBackgroundBlur = isChecked
            binding.blurRadiusSection.visibility = if (isChecked) View.VISIBLE else View.GONE
            updatePreviewBlur()
        }

        binding.blurRadiusSlider.addOnChangeListener { _, value, fromUser ->
            binding.blurRadiusValue.text = value.toInt().toString()
            if (fromUser) globalParam.chatBackgroundBlurRadius = value.toInt()
            updatePreviewBlur()
        }
    }

    // ─── Блок 2: слайдер затемнения ───────────────────────────────────────────

    private fun setupDimSlider() {
        val saved = globalParam.chatBackgroundDim
        binding.dimSlider.value = saved.toFloat()
        binding.dimValue.text = getString(R.string.personalization_percentage, saved)
        updatePreviewDim()

        binding.dimSlider.addOnChangeListener { _, value, fromUser ->
            val pct = value.toInt()
            binding.dimValue.text = getString(R.string.personalization_percentage, pct)
            if (fromUser) globalParam.chatBackgroundDim = pct
            updatePreviewDim()
        }
    }

    // ─── Блок 3: сетка фонов ──────────────────────────────────────────────────

    private fun setupBackgroundsGrid() {
        backgroundAdapter = ChatBackgroundAdapter(
            scope = lifecycleScope,
            getFileUrl = { fileId ->
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            },
            onSelect = { fileId ->
                lifecycleScope.launch {
                    val result = grpcManager.setGlobalChatBackground(fileId)
                    if (result.isSuccess) {
                        globalParam.chatBackgroundFileId = fileId
                        backgroundAdapter.selectedFileId = fileId
                        updatePreviewBackground(fileId)
                    } else {
                        Toast.makeText(this@PersonalizationSettingsActivity, R.string.profile_background_set_error, Toast.LENGTH_SHORT).show()
                    }
                }
            },
            onDelete = { fileId ->
                deleteBackground(fileId)
            }
        )
        backgroundAdapter.selectedFileId = globalParam.chatBackgroundFileId

        binding.backgroundsRecyclerView.apply {
            layoutManager = GridLayoutManager(this@PersonalizationSettingsActivity, 3)
            adapter = backgroundAdapter
            isNestedScrollingEnabled = false
        }

        binding.buttonAddBackground.setOnClickListener {
            if (backgroundAdapter.isInDeleteMode()) {
                backgroundAdapter.cancelDeleteMode()
            } else {
                pickImageLauncher.launch("image/*")
            }
        }
        binding.buttonToggleBackgrounds.setOnClickListener {
            backgroundsExpanded = !backgroundsExpanded
            refreshBackgroundList()
        }

        // Показываем превью сохранённого фона сразу при открытии
        updatePreviewBackground(globalParam.chatBackgroundFileId)
    }

    // ─── Загрузка с сервера ───────────────────────────────────────────────────

    private fun loadPersonalizationFromServer() {
        lifecycleScope.launch {
            try {
                val result = grpcManager.getPersonalization()
                if (result.isSuccess) {
                    val ids = result.getOrNull() ?: emptyList()
                    backgroundFileIds.clear()
                    backgroundFileIds.addAll(ids)
                    refreshBackgroundList()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка загрузки персонализации", e)
            }
        }
    }

    private fun refreshBackgroundList() {
        val allItems = listOf(ChatBackgroundItem("")) + backgroundFileIds.map { ChatBackgroundItem(it) }
        val hasHiddenItems = allItems.size > MAX_COLLAPSED_BACKGROUNDS
        val visibleItems = if (hasHiddenItems && !backgroundsExpanded) {
            allItems.take(MAX_COLLAPSED_BACKGROUNDS)
        } else {
            allItems
        }

        backgroundAdapter.submitList(visibleItems)
        binding.buttonToggleBackgrounds.visibility = if (hasHiddenItems) View.VISIBLE else View.GONE
        binding.buttonToggleBackgrounds.text = getString(
            if (backgroundsExpanded) R.string.personalization_collapse_backgrounds
            else R.string.personalization_expand_backgrounds
        )
    }

    // ─── Удаление фона ────────────────────────────────────────────────────────

    private fun deleteBackground(fileId: String) {
        backgroundFileIds.remove(fileId)
        if (globalParam.chatBackgroundFileId == fileId) {
            globalParam.chatBackgroundFileId = ""
            backgroundAdapter.selectedFileId = ""
            updatePreviewBackground("")
        }
        refreshBackgroundList()
        syncPersonalizationToServer()
    }

    // ─── Загрузка нового фона ────────────────────────────────────────────────

    private fun uploadBackgroundImage(uri: Uri) {
        lifecycleScope.launch {
            try {
                binding.buttonAddBackground.isEnabled = false
                binding.buttonAddBackground.text = getString(R.string.loading_dots)

                val bytes = withContext(Dispatchers.IO) {
                    compressToJpeg85(uri)
                } ?: run {
                    Toast.makeText(
                        this@PersonalizationSettingsActivity,
                        R.string.account_image_read_error,
                        Toast.LENGTH_SHORT
                    ).show()
                    return@launch
                }

                val uploadResult = chatRepository.uploadFile(
                    bytes,
                    FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                )

                if (uploadResult.isSuccess) {
                    val fileId = uploadResult.getOrNull()!!
                    backgroundFileIds.add(fileId)
                    if (backgroundFileIds.size + 1 > MAX_COLLAPSED_BACKGROUNDS) {
                        backgroundsExpanded = true
                    }
                    refreshBackgroundList()
                    syncPersonalizationToServer()
                } else {
                    Toast.makeText(
                        this@PersonalizationSettingsActivity,
                        R.string.personalization_background_upload_failed,
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка при загрузке фона", e)
                Toast.makeText(
                    this@PersonalizationSettingsActivity,
                    getString(R.string.settings_error_detail, e.message.orEmpty()),
                    Toast.LENGTH_SHORT
                ).show()
            } finally {
                binding.buttonAddBackground.isEnabled = true
                binding.buttonAddBackground.text = getString(R.string.personalization_add_background)
            }
        }
    }

    private fun compressToJpeg85(uri: Uri): ByteArray? {
        return try {
            val inputStream = contentResolver.openInputStream(uri) ?: return null
            val bitmap = android.graphics.BitmapFactory.decodeStream(inputStream)
            inputStream.close()
            val out = ByteArrayOutputStream()
            bitmap.compress(Bitmap.CompressFormat.JPEG, 85, out)
            bitmap.recycle()
            out.toByteArray()
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка сжатия изображения", e)
            null
        }
    }

    // ─── Синхронизация с сервером ─────────────────────────────────────────────

    private fun syncPersonalizationToServer() {
        lifecycleScope.launch {
            try {
                val result = grpcManager.updatePersonalizationBackgrounds(backgroundFileIds.toList())
                if (result.isSuccess) {
                    grpcManager.getUserSettings().onSuccess { settings ->
                        globalParam.applyChatBackgroundSettings(
                            settings.globalChatBackgroundFileId,
                            settings.chatBackgroundFileIds
                        )
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка синхронизации персонализации", e)
            }
        }
    }
}
