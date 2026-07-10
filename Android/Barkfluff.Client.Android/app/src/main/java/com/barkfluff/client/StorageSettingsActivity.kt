package com.barkfluff.client

import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.cache.ChatCacheRepository
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
        val label: String,
        val color: Int,
        val typeNames: Set<String>
    )

    private val categories = listOf(
        StorageCategory("Изображения", Color.parseColor("#4CAF50"), setOf("MESSAGE_ATTACHMENT_IMAGE")),
        StorageCategory("Видео", Color.parseColor("#2196F3"), setOf("MESSAGE_ATTACHMENT_VIDEO")),
        StorageCategory("Аудио", Color.parseColor("#FF9800"), setOf("MESSAGE_ATTACHMENT_AUDIO", "MESSAGE_ATTACHMENT_VOICE")),
        StorageCategory("Документы", Color.parseColor("#9C27B0"), setOf("MESSAGE_ATTACHMENT_DOCUMENT")),
        StorageCategory("GIF", Color.parseColor("#E91E63"), setOf("MESSAGE_ATTACHMENT_GIF")),
        StorageCategory("Стикеры", Color.parseColor("#00BCD4"), setOf("MESSAGE_ATTACHMENT_STICKER")),
        StorageCategory("Аватарки", Color.parseColor("#607D8B"), setOf("USER_AVATAR", "CHAT_PICTURE"))
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
                val usedMB = info.totalUsed / (1024.0 * 1024.0)
                val limitGB = info.limit / (1024.0 * 1024.0 * 1024.0)

                binding.textStorageUsage.text = "%.1f МБ из %.1f ГБ".format(usedMB, limitGB)

                populateStorageBar(info)
                populateStorageLegend(info)
            } else {
                Log.e(TAG, "Ошибка получения данных хранилища", result.exceptionOrNull())
                binding.textStorageUsage.text = "Не удалось загрузить"
            }
        }
    }

    private fun populateStorageBar(info: GrpcManager.StorageInfo) {
        binding.storageBarLayout.removeAllViews()

        if (info.limit <= 0) return

        val categoryBytes = categories.map { cat ->
            cat to cat.typeNames.sumOf { typeName -> info.byType[typeName] ?: 0L }
        }.filter { it.second > 0 }

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

        val categoryBytes = categories.map { cat ->
            cat to cat.typeNames.sumOf { typeName -> info.byType[typeName] ?: 0L }
        }.filter { it.second > 0 }

        val inflater = LayoutInflater.from(this)
        for ((cat, bytes) in categoryBytes) {
            val itemView = inflater.inflate(R.layout.item_storage_legend, binding.storageLegendLayout, false)

            val dot = itemView.findViewById<View>(R.id.legendDot)
            val bg = dot.background.mutate() as GradientDrawable
            bg.setColor(cat.color)

            itemView.findViewById<TextView>(R.id.legendLabel).text = cat.label
            itemView.findViewById<TextView>(R.id.legendSize).text = formatBytes(bytes)

            binding.storageLegendLayout.addView(itemView)
        }
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
            binding.textCacheSize.text = getString(
                R.string.storage_cache_size_with_chats,
                imageCacheSize / (1024.0 * 1024.0),
                chatCacheStats.chatCount,
                chatCacheStats.messageCount,
                formatBytes(chatCacheStats.sizeBytes)
            )
        }
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
            withContext(Dispatchers.IO) {
                // Очищаем все кеши через AvatarLoader
                AvatarLoader.clearAllCaches(this@StorageSettingsActivity)

                // Удаляем дополнительные папки кеша (если есть)
                val bitmapCacheDir = File(cacheDir, "bitmap_cache")
                bitmapCacheDir.deleteRecursively()
                chatCacheRepository.clearAll()
            }

            updateCacheSize()
            Snackbar.make(binding.root, "Кеш очищен", Snackbar.LENGTH_SHORT).show()
        }
    }
}
