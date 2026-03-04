package com.barkfluff.client

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.drawable.BitmapDrawable
import android.os.Bundle
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import android.view.MotionEvent
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.viewpager2.widget.ViewPager2
import com.barkfluff.client.adapter.ImagePagerAdapter
import com.barkfluff.client.databinding.ActivityImageViewerBinding
import com.barkfluff.client.repository.ChatRepository
import com.github.chrisbanes.photoview.PhotoView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Просмотрщик изображений с поддержкой масштабирования (pinch-to-zoom),
 * свайпа между изображениями и свайпа вниз для закрытия.
 * Не полноэкранный — статус-бар остаётся видимым.
 */
class ImageViewerActivity : AppCompatActivity() {

    private lateinit var binding: ActivityImageViewerBinding
    private lateinit var chatRepository: ChatRepository

    private var fileIds: List<String> = emptyList()
    private var previewUrls: List<String> = emptyList()
    private var startPosition: Int = 0

    private var swipeTouchStartY = 0f

    companion object {
        private const val TAG = "ImageViewerActivity"
        private const val EXTRA_FILE_IDS = "file_ids"
        private const val EXTRA_PREVIEW_URLS = "preview_urls"
        private const val EXTRA_START_POSITION = "start_position"

        fun createIntent(
            context: Context,
            fileIds: List<String>,
            previewUrls: List<String> = emptyList(),
            startPosition: Int = 0
        ): Intent {
            return Intent(context, ImageViewerActivity::class.java).apply {
                putStringArrayListExtra(EXTRA_FILE_IDS, ArrayList(fileIds))
                putStringArrayListExtra(EXTRA_PREVIEW_URLS, ArrayList(previewUrls))
                putExtra(EXTRA_START_POSITION, startPosition)
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
        startPosition = intent.getIntExtra(EXTRA_START_POSITION, 0)

        if (fileIds.isEmpty()) {
            finish()
            return
        }

        setupViewPager()
        setupButtons()
        setupSwipeDismiss()
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
            }
        })
    }

    private fun updateCounter(position: Int) {
        if (fileIds.size > 1) {
            binding.counterTextView.text = "${position + 1} / ${fileIds.size}"
            binding.counterTextView.visibility = View.VISIBLE
        } else {
            binding.counterTextView.visibility = View.GONE
        }
    }

    private fun setupButtons() {
        binding.closeButton.setOnClickListener { finishWithAnimation() }
        binding.downloadButton.setOnClickListener { saveCurrentImage() }
    }

    private fun setupSwipeDismiss() {
        var isSwipingDown = false

        binding.root.setOnTouchListener { v, event ->
            when (event.action) {
                MotionEvent.ACTION_DOWN -> {
                    swipeTouchStartY = event.rawY
                    isSwipingDown = false
                    false
                }
                MotionEvent.ACTION_MOVE -> {
                    val deltaY = event.rawY - swipeTouchStartY
                    val deltaX = event.x  // approximate, just to check direction

                    if (!isSwipingDown && deltaY > 20f) {
                        isSwipingDown = true
                    }

                    if (isSwipingDown && deltaY > 0) {
                        v.translationY = deltaY
                        val screenH = resources.displayMetrics.heightPixels.toFloat()
                        v.alpha = 1f - (deltaY / (screenH * 0.5f)).coerceIn(0f, 1f)
                        true
                    } else false
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    val deltaY = event.rawY - swipeTouchStartY
                    val screenH = resources.displayMetrics.heightPixels.toFloat()
                    if (isSwipingDown && deltaY > screenH * 0.25f) {
                        finishWithAnimation()
                    } else {
                        v.animate().translationY(0f).alpha(1f).setDuration(200).start()
                        isSwipingDown = false
                    }
                    false
                }
                else -> false
            }
        }
    }

    private fun finishWithAnimation() {
        val screenH = resources.displayMetrics.heightPixels.toFloat()
        binding.root.animate()
            .translationY(screenH)
            .alpha(0f)
            .setDuration(250)
            .withEndAction { finish() }
            .start()
    }

    private fun saveCurrentImage() {
        val currentPosition = binding.viewPager.currentItem
        val fileId = fileIds[currentPosition]

        val recyclerView = binding.viewPager.getChildAt(0) as? androidx.recyclerview.widget.RecyclerView
        val viewHolder = recyclerView?.findViewHolderForAdapterPosition(currentPosition)
        val photoView = (viewHolder?.itemView as? android.view.ViewGroup)?.getChildAt(0) as? PhotoView
        val drawable = photoView?.drawable

        if (drawable is BitmapDrawable && drawable.bitmap != null) {
            lifecycleScope.launch {
                saveToGallery(drawable.bitmap, fileId)
            }
        } else {
            Toast.makeText(this, "Изображение ещё загружается", Toast.LENGTH_SHORT).show()
        }
    }

    private suspend fun saveToGallery(bitmap: Bitmap, fileId: String) {
        withContext(Dispatchers.IO) {
            try {
                val filename = "BarkFluff_${fileId.take(8)}_${System.currentTimeMillis()}.jpg"
                val contentValues = ContentValues().apply {
                    put(MediaStore.Images.Media.DISPLAY_NAME, filename)
                    put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg")
                    put(MediaStore.Images.Media.RELATIVE_PATH, "${Environment.DIRECTORY_PICTURES}/BarkFluff")
                    put(MediaStore.Images.Media.IS_PENDING, 1)
                }
                val uri = contentResolver.insert(
                    MediaStore.Images.Media.EXTERNAL_CONTENT_URI, contentValues
                )
                if (uri != null) {
                    contentResolver.openOutputStream(uri)?.use { stream ->
                        bitmap.compress(Bitmap.CompressFormat.JPEG, 95, stream)
                    }
                    contentValues.clear()
                    contentValues.put(MediaStore.Images.Media.IS_PENDING, 0)
                    contentResolver.update(uri, contentValues, null, null)
                    withContext(Dispatchers.Main) {
                        Toast.makeText(this@ImageViewerActivity, "Сохранено в Картинки/BarkFluff", Toast.LENGTH_SHORT).show()
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error saving image", e)
                withContext(Dispatchers.Main) {
                    Toast.makeText(this@ImageViewerActivity, "Ошибка сохранения: ${e.message}", Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        chatRepository.close()
    }
}
