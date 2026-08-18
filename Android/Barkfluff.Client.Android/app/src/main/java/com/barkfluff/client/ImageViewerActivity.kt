package com.barkfluff.client

import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.view.MotionEvent
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.FileProvider
import androidx.lifecycle.lifecycleScope
import androidx.viewpager2.widget.ViewPager2
import com.barkfluff.client.adapter.ImagePagerAdapter
import com.barkfluff.client.databinding.ActivityImageViewerBinding
import com.barkfluff.client.dialog.ForwardChatPickerBottomSheet
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.FileSaveUtils
import com.barkfluff.client.utils.SwipeToDismissHelper
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Просмотрщик изображений с поддержкой масштабирования (pinch-to-zoom),
 * свайпа между изображениями и свайпа вверх/вниз для закрытия.
 * Не полноэкранный — статус-бар остаётся видимым.
 */
class ImageViewerActivity : AppCompatActivity() {

    private lateinit var binding: ActivityImageViewerBinding
    private lateinit var chatRepository: ChatRepository

    private var fileIds: List<String> = emptyList()
    private var previewUrls: List<String> = emptyList()
    private var fileNames: List<String> = emptyList()
    private var sourceMessageIds: List<Long> = emptyList()
    private var startPosition: Int = 0

    private lateinit var swipeHelper: SwipeToDismissHelper

    companion object {

        private const val EXTRA_FILE_IDS = "file_ids"
        private const val EXTRA_PREVIEW_URLS = "preview_urls"
        private const val EXTRA_START_POSITION = "start_position"
        private const val EXTRA_FILE_NAMES = "file_names"
        private const val EXTRA_SOURCE_MESSAGE_IDS = "source_message_ids"

        fun createIntent(
            context: Context,
            fileIds: List<String>,
            previewUrls: List<String> = emptyList(),
            startPosition: Int = 0,
            fileNames: List<String> = emptyList(),
            sourceMessageIds: List<Long> = emptyList()
        ): Intent {
            return Intent(context, ImageViewerActivity::class.java).apply {
                putStringArrayListExtra(EXTRA_FILE_IDS, ArrayList(fileIds))
                putStringArrayListExtra(EXTRA_PREVIEW_URLS, ArrayList(previewUrls))
                putExtra(EXTRA_START_POSITION, startPosition)
                putStringArrayListExtra(EXTRA_FILE_NAMES, ArrayList(fileNames))
                putExtra(EXTRA_SOURCE_MESSAGE_IDS, sourceMessageIds.toLongArray())
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityImageViewerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        chatRepository = ChatRepository(this, (application as BarkFluffApplication).grpcManager)

        fileIds = intent.getStringArrayListExtra(EXTRA_FILE_IDS) ?: emptyList()
        previewUrls = intent.getStringArrayListExtra(EXTRA_PREVIEW_URLS) ?: emptyList()
        fileNames = intent.getStringArrayListExtra(EXTRA_FILE_NAMES) ?: emptyList()
        sourceMessageIds = intent.getLongArrayExtra(EXTRA_SOURCE_MESSAGE_IDS)?.toList() ?: emptyList()
        startPosition = intent.getIntExtra(EXTRA_START_POSITION, 0)

        if (fileIds.isEmpty()) {
            finish()
            return
        }

        swipeHelper = SwipeToDismissHelper(this, binding.root) { finish() }

        setupViewPager()
        setupButtons()
    }

    override fun dispatchTouchEvent(ev: MotionEvent): Boolean {
        if (swipeHelper.onDispatchTouchEvent(ev)) return true
        return super.dispatchTouchEvent(ev)
    }

    private fun setupViewPager() {
        val adapter = ImagePagerAdapter(fileIds, previewUrls) { fileId ->
            chatRepository.getFileDownloadUrl(fileId).getOrNull()
        }
        binding.viewPager.adapter = adapter
        binding.viewPager.setCurrentItem(startPosition, false)
        updateCounter(startPosition)

        binding.viewPager.registerOnPageChangeCallback(object : ViewPager2.OnPageChangeCallback() {
            override fun onPageSelected(position: Int) {
                updateCounter(position)
                updateForwardAvailability(position)
            }
        })

        updateForwardAvailability(startPosition)
    }

    private fun updateCounter(position: Int) {
        if (fileIds.size > 1) {
            binding.counterTextView.text = getString(R.string.media_counter, position + 1, fileIds.size)
            binding.counterTextView.visibility = View.VISIBLE
        } else {
            binding.counterTextView.visibility = View.GONE
        }
    }

    private fun setupButtons() {
        binding.closeButton.setOnClickListener { swipeHelper.dismiss() }
        binding.saveButton.setOnClickListener { saveCurrentImage() }
        binding.copyButton.setOnClickListener { copyCurrentImageToClipboard() }
        binding.forwardButton.setOnClickListener { forwardCurrentImage() }
    }

    private fun copyCurrentImageToClipboard() {
        val currentPosition = binding.viewPager.currentItem
        lifecycleScope.launch {
            val source = getImageFile(fileIds[currentPosition])
            if (source == null) {
                Toast.makeText(this@ImageViewerActivity, R.string.message_image_download_failed, Toast.LENGTH_SHORT).show()
                return@launch
            }

            try {
                val fileName = getFileName(currentPosition)
                val ext = fileName.substringAfterLast('.', "").lowercase().ifBlank { "jpg" }
                val tempFile = withContext(Dispatchers.IO) {
                    val tempDir = File(cacheDir, "clipboard").apply { if (!exists()) mkdirs() }
                    File(tempDir, "img_${System.currentTimeMillis()}.$ext").also { target ->
                        source.inputStream().use { input ->
                            target.outputStream().use { input.copyTo(it) }
                        }
                    }
                }
                val uri = FileProvider.getUriForFile(
                    this@ImageViewerActivity,
                    "${packageName}.fileprovider",
                    tempFile
                )
                val mime = FileSaveUtils.getMimeType("image.$ext").takeIf { it.startsWith("image/") }
                    ?: "image/jpeg"
                val clipData = ClipData(
                    ClipDescription("BarkFluff image", arrayOf(mime)),
                    ClipData.Item(uri)
                )
                val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                clipboard.setPrimaryClip(clipData)
                Toast.makeText(this@ImageViewerActivity, R.string.message_image_copied, Toast.LENGTH_SHORT).show()
            } catch (e: Exception) {
                Toast.makeText(this@ImageViewerActivity, R.string.message_copy_failed, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun saveCurrentImage() {
        val currentPosition = binding.viewPager.currentItem
        val fileId = fileIds[currentPosition]
        lifecycleScope.launch {
            val source = getImageFile(fileId)
            if (source == null) {
                Toast.makeText(this@ImageViewerActivity, R.string.message_image_download_failed, Toast.LENGTH_SHORT).show()
                return@launch
            }

            val ok = withContext(Dispatchers.IO) {
                FileSaveUtils.saveImageToGallery(this@ImageViewerActivity, source, getFileName(currentPosition))
            }
            Toast.makeText(
                this@ImageViewerActivity,
                if (ok) R.string.image_saved_to_gallery else R.string.save_failed,
                Toast.LENGTH_SHORT
            ).show()
        }
    }

    private fun forwardCurrentImage() {
        val messageId = sourceMessageIds.getOrNull(binding.viewPager.currentItem) ?: return
        if (messageId == 0L) return

        ForwardChatPickerBottomSheet.newInstance(messageId)
            .show(supportFragmentManager, "forward_image")
    }

    private suspend fun getImageFile(fileId: String): File? {
        return FileCache.getFile(fileId) ?: chatRepository.downloadFile(fileId)
    }

    private fun getFileName(position: Int): String {
        return fileNames.getOrNull(position).orEmpty()
            .ifBlank { "image_${fileIds[position].take(8)}.jpg" }
    }

    private fun updateForwardAvailability(position: Int) {
        binding.forwardButton.isEnabled = sourceMessageIds.getOrNull(position)?.let { it != 0L } == true
    }

    override fun onDestroy() {
        super.onDestroy()
        chatRepository.close()
    }
}
