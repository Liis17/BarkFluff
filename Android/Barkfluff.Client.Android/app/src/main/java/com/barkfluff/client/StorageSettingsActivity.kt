package com.barkfluff.client

import android.graphics.Color
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.Space
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.cache.ChatCacheStats
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.databinding.ActivityStorageSettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

class StorageSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityStorageSettingsBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatCacheRepository: ChatCacheRepository

    companion object {
        private const val TAG = "StorageSettings"
    }

    private data class StorageCategory(
        val labelRes: Int,
        val color: Int,
        val typeNames: Set<String>,
        val iconRes: Int
    )

    private val categories = listOf(
        StorageCategory(R.string.storage_category_images, Color.parseColor("#3AA655"), setOf("MESSAGE_ATTACHMENT_IMAGE", "MESSAGE_ATTACHMENT_GIF"), R.drawable.ic_image_placeholder),
        StorageCategory(R.string.storage_category_videos, Color.parseColor("#2F7DE1"), setOf("MESSAGE_ATTACHMENT_VIDEO"), R.drawable.ic_video),
        StorageCategory(R.string.storage_category_audio, Color.parseColor("#F39C12"), setOf("MESSAGE_ATTACHMENT_AUDIO", "MESSAGE_ATTACHMENT_VOICE"), R.drawable.ic_file),
        StorageCategory(R.string.storage_category_documents, Color.parseColor("#9B30C9"), setOf("MESSAGE_ATTACHMENT_DOCUMENT"), R.drawable.ic_file),
        StorageCategory(R.string.storage_category_stickers, Color.parseColor("#16B8C4"), setOf("MESSAGE_ATTACHMENT_STICKER"), R.drawable.ic_sticker),
        StorageCategory(R.string.storage_category_avatars, Color.parseColor("#5F7D8C"), setOf("USER_AVATAR", "CHAT_PICTURE"), R.drawable.ic_account)
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityStorageSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager
        chatCacheRepository = app.chatCacheRepository

        setupToolbar()
        setupClickListeners()
        loadStorageInfo()
        updateCacheSize()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun setupClickListeners() {
        binding.buttonClearCache.setOnClickListener {
            clearCache()
        }
    }

    private fun loadStorageInfo() {
        lifecycleScope.launch {
            val result = grpcManager.getUserStorageInfo()
            if (result.isSuccess) {
                val info = result.getOrNull()!!
                binding.textStorageUsage.text = formatBytes(info.totalUsed)
                binding.textStorageLimit.text = getString(
                    R.string.storage_of_limit,
                    formatBytes(info.limit)
                )

                populateStorageBar(info)
                populateStorageLegend(info)
            } else {
                Log.e(TAG, "Ошибка получения данных хранилища", result.exceptionOrNull())
                binding.textStorageUsage.text = getString(R.string.storage_load_error)
                binding.textStorageLimit.text = ""
            }
        }
    }

    private fun populateStorageBar(info: GrpcManager.StorageInfo) {
        binding.storageBarLayout.removeAllViews()

        if (info.limit <= 0) return

        val categoryBytes = storageCategoryBytes(info)

        for ((cat, bytes) in categoryBytes) {
            val segment = View(this)
            segment.layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, bytes.toFloat())
            segment.setBackgroundColor(cat.color)
            binding.storageBarLayout.addView(segment)
        }

        // Свободное место
        val freeBytes = (info.limit - info.totalUsed).coerceAtLeast(0)
        if (freeBytes > 0) {
            val freeSegment = View(this)
            freeSegment.layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, freeBytes.toFloat())
            freeSegment.setBackgroundColor(Color.TRANSPARENT)
            binding.storageBarLayout.addView(freeSegment)
        }
    }

    private fun populateStorageLegend(info: GrpcManager.StorageInfo) {
        binding.storageLegendLayout.removeAllViews()

        val categoryBytes = storageCategoryBytes(info)

        val inflater = LayoutInflater.from(this)
        for ((index, entry) in categoryBytes.withIndex()) {
            val (cat, bytes) = entry
            val itemView = inflater.inflate(R.layout.item_storage_legend, binding.storageLegendLayout, false)

            itemView.findViewById<ImageView>(R.id.legendIcon).apply {
                setImageResource(cat.iconRes)
                setColorFilter(cat.color)
            }
            itemView.background = getDrawable(
                when (index) {
                    0 -> R.drawable.bg_settings_item_top
                    categoryBytes.lastIndex -> R.drawable.bg_settings_item_bottom
                    else -> R.drawable.bg_settings_item_middle
                }
            )

            itemView.findViewById<TextView>(R.id.legendLabel).text = getString(cat.labelRes)
            itemView.findViewById<TextView>(R.id.legendSize).text = formatBytes(bytes)

            binding.storageLegendLayout.addView(itemView)
            if (index != categoryBytes.lastIndex) {
                binding.storageLegendLayout.addView(Space(this).apply {
                    layoutParams = LinearLayout.LayoutParams(
                        LinearLayout.LayoutParams.MATCH_PARENT,
                        resources.getDimensionPixelSize(R.dimen.settings_split_gap)
                    )
                })
            }
        }
    }

    private fun storageCategoryBytes(info: GrpcManager.StorageInfo): List<Pair<StorageCategory, Long>> =
        categories.map { category ->
            category to category.typeNames.sumOf { typeName -> info.byType[typeName] ?: 0L }
        }

    private fun formatBytes(bytes: Long): String {
        return when {
            bytes < 1024 -> "$bytes Б"
            bytes < 1024 * 1024 -> "%.1f КБ".format(bytes / 1024.0)
            bytes < 1024L * 1024 * 1024 -> "%.1f МБ".format(bytes / (1024.0 * 1024.0))
            else -> "%.2f ГБ".format(bytes / (1024.0 * 1024.0 * 1024.0))
        }
    }

    private fun updateCacheSize() {
        lifecycleScope.launch {
            val (imageCacheSize, chatCacheStats) = withContext(Dispatchers.IO) {
                calculateCacheSize() to chatCacheRepository.stats()
            }
            renderCacheSize(imageCacheSize, chatCacheStats)
        }
    }

    private fun renderCacheSize(imageCacheSize: Long, chatCacheStats: ChatCacheStats) {
        val chatCacheSize = chatCacheStats.sizeBytes
        val totalSize = imageCacheSize + chatCacheSize
        binding.textLocalCacheUsage.text = formatBytes(totalSize)
        binding.textLocalCacheSummary.text = getString(
            R.string.storage_cache_summary,
            chatCacheStats.chatCount,
            chatCacheStats.messageCount
        )
        binding.textImageCacheSize.text = formatBytes(imageCacheSize)
        binding.textChatCacheSize.text = formatBytes(chatCacheSize)
        binding.buttonClearCache.text = getString(
            R.string.storage_clear_cache_with_size,
            formatBytes(totalSize)
        )

        binding.localCacheBarLayout.removeAllViews()
        if (totalSize > 0) {
            addCacheSegment(imageCacheSize, Color.parseColor("#3AA655"))
            addCacheSegment(chatCacheSize, Color.parseColor("#F39C12"))
        }
    }

    private fun addCacheSegment(size: Long, color: Int) {
        if (size <= 0L) return
        binding.localCacheBarLayout.addView(View(this).apply {
            layoutParams = LinearLayout.LayoutParams(
                0,
                LinearLayout.LayoutParams.MATCH_PARENT,
                size.toFloat()
            )
            setBackgroundColor(color)
        })
    }

    private fun calculateCacheSize(): Long {
        var totalSize = 0L
        val imageCacheDir = File(cacheDir, "image_cache")
        val bitmapCacheDir = File(cacheDir, "bitmap_cache")

        totalSize += getDirSize(imageCacheDir)
        totalSize += getDirSize(bitmapCacheDir)

        return totalSize
    }

    private fun getDirSize(dir: File): Long {
        if (!dir.exists()) return 0
        var size = 0L
        dir.walkTopDown().forEach { file ->
            if (file.isFile) {
                size += file.length()
            }
        }
        return size
    }

    private fun clearCache() {
        lifecycleScope.launch {
            val (imageCacheSize, chatCacheStats) = withContext(Dispatchers.IO) {
                // Очищаем все кеши через AvatarLoader
                AvatarLoader.clearAllCaches(this@StorageSettingsActivity)

                // Удаляем дополнительные папки кеша (если есть)
                val bitmapCacheDir = File(cacheDir, "bitmap_cache")
                bitmapCacheDir.deleteRecursively()
                chatCacheRepository.clearAll()
                calculateCacheSize() to ChatCacheStats(0, 0, 0)
            }

            renderCacheSize(imageCacheSize, chatCacheStats)
            Snackbar.make(binding.root, "Кеш очищен", Snackbar.LENGTH_SHORT).show()
        }
    }
}
