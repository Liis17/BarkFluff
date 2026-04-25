package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.graphics.Bitmap
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import com.barkfluff.client.adapter.ChatBackgroundAdapter
import com.barkfluff.client.adapter.ChatBackgroundItem
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityPersonalizationSettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import barkfluff.files.FilesApiOuterClass
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream

/**
 * Экран настроек персонализации:
 * - Слайдер закругления пузырей сообщений (0..30 dp)
 * - Сетка фоновых изображений чата с возможностью добавления и удаления
 * - Тогл размытия фона
 */
class PersonalizationSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityPersonalizationSettingsBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var backgroundAdapter: ChatBackgroundAdapter

    /** Локальный список fileId фонов (синхронизируется с сервером) */
    private val backgroundFileIds = mutableListOf<String>()

    companion object {
        private const val TAG = "PersonalizationActivity"
    }

    // Лаунчер выбора одной картинки из галереи
    private val pickImageLauncher = registerForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        if (uri != null) uploadBackgroundImage(uri)
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
        setupCornerRadiusSlider()
        setupBackgroundsGrid()
        setupBlurToggle()
        loadPersonalizationFromServer()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener { finish() }
    }

    // ─── Слайдер закругления ──────────────────────────────────────────────────

    private fun setupCornerRadiusSlider() {
        val saved = globalParam.chatMessageCornerRadius
        binding.cornerRadiusSlider.value = saved.toFloat()
        binding.cornerRadiusValue.text = saved.toString()

        updatePreviewCorners(saved.toFloat())

        binding.cornerRadiusSlider.addOnChangeListener { _, value, fromUser ->
            binding.cornerRadiusValue.text = value.toInt().toString()
            updatePreviewCorners(value)
            if (fromUser) {
                globalParam.chatMessageCornerRadius = value.toInt()
            }
        }
    }

    private fun updatePreviewCorners(dp: Float) {
        val px = dp * resources.displayMetrics.density
        binding.previewCardReceived.radius = px
        binding.previewCardSent.radius = px
    }

    // ─── Тогл размытия ────────────────────────────────────────────────────────

    private fun setupBlurToggle() {
        binding.switchBlurBackground.isChecked = globalParam.chatBackgroundBlur
        binding.switchBlurBackground.setOnCheckedChangeListener { _, isChecked ->
            globalParam.chatBackgroundBlur = isChecked
        }
    }

    // ─── Сетка фонов ─────────────────────────────────────────────────────────

    private fun setupBackgroundsGrid() {
        backgroundAdapter = ChatBackgroundAdapter(
            scope = lifecycleScope,
            getFileUrl = { fileId ->
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            },
            onSelect = { fileId ->
                globalParam.chatBackgroundFileId = fileId
                backgroundAdapter.selectedFileId = fileId
            },
            onDelete = { fileId ->
                deleteBackground(fileId)
            }
        )
        backgroundAdapter.selectedFileId = globalParam.chatBackgroundFileId

        binding.backgroundsRecyclerView.apply {
            layoutManager = GridLayoutManager(this@PersonalizationSettingsActivity, 3)
            adapter = backgroundAdapter
            // Блокируем вложенный скролл чтобы не сломать NestedScrollView
            isNestedScrollingEnabled = false
        }

        binding.buttonAddBackground.setOnClickListener {
            // Если в режиме удаления — сначала выходим
            if (backgroundAdapter.isInDeleteMode()) {
                backgroundAdapter.cancelDeleteMode()
            } else {
                pickImageLauncher.launch("image/*")
            }
        }
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
        backgroundAdapter.submitList(backgroundFileIds.map { ChatBackgroundItem(it) })
    }

    // ─── Удаление фона ────────────────────────────────────────────────────────

    private fun deleteBackground(fileId: String) {
        backgroundFileIds.remove(fileId)
        // Если удалённый был выбран — сбрасываем
        if (globalParam.chatBackgroundFileId == fileId) {
            globalParam.chatBackgroundFileId = ""
            backgroundAdapter.selectedFileId = ""
        }
        refreshBackgroundList()
        syncPersonalizationToServer()
    }

    // ─── Загрузка нового фона ────────────────────────────────────────────────

    private fun uploadBackgroundImage(uri: Uri) {
        lifecycleScope.launch {
            try {
                binding.buttonAddBackground.isEnabled = false
                binding.buttonAddBackground.text = "Загрузка..."

                // Сжать до JPEG 85%
                val bytes = withContext(Dispatchers.IO) {
                    compressToJpeg85(uri)
                } ?: run {
                    Toast.makeText(this@PersonalizationSettingsActivity,
                        "Не удалось обработать изображение", Toast.LENGTH_SHORT).show()
                    return@launch
                }

                // Загрузить на сервер как MESSAGE_ATTACHMENT_IMAGE
                val uploadResult = chatRepository.uploadFile(
                    bytes,
                    FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                )

                if (uploadResult.isSuccess) {
                    val fileId = uploadResult.getOrNull()!!
                    backgroundFileIds.add(fileId)
                    refreshBackgroundList()
                    syncPersonalizationToServer()
                } else {
                    Toast.makeText(this@PersonalizationSettingsActivity,
                        "Ошибка загрузки фона", Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка при загрузке фона", e)
                Toast.makeText(this@PersonalizationSettingsActivity,
                    "Ошибка: ${e.message}", Toast.LENGTH_SHORT).show()
            } finally {
                binding.buttonAddBackground.isEnabled = true
                binding.buttonAddBackground.text = "Добавить фон"
            }
        }
    }

    private fun compressToJpeg85(uri: Uri): ByteArray? {
        return try {
            val inputStream = contentResolver.openInputStream(uri) ?: return null
            val originalBitmap = android.graphics.BitmapFactory.decodeStream(inputStream)
            inputStream.close()
            val out = ByteArrayOutputStream()
            originalBitmap.compress(Bitmap.CompressFormat.JPEG, 85, out)
            originalBitmap.recycle()
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
                grpcManager.updatePersonalizationBackgrounds(backgroundFileIds.toList())
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка синхронизации персонализации", e)
            }
        }
    }
}
