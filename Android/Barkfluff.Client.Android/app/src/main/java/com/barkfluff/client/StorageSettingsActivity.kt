package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import androidx.appcompat.app.AppCompatActivity
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

    companion object {
        private const val TAG = "StorageSettings"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityStorageSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

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

                val progress = if (info.limit > 0) {
                    ((info.totalUsed.toDouble() / info.limit) * 100).toInt()
                } else 0

                binding.storageProgress.progress = progress
                binding.textStorageUsage.text = "%.1f МБ из %.1f ГБ".format(usedMB, limitGB)
            } else {
                Log.e(TAG, "Ошибка получения данных хранилища", result.exceptionOrNull())
                binding.textStorageUsage.text = "Не удалось загрузить"
            }
        }
    }

    private fun updateCacheSize() {
        lifecycleScope.launch {
            val size = withContext(Dispatchers.IO) {
                calculateCacheSize()
            }
            val sizeMB = size / (1024.0 * 1024.0)
            binding.textCacheSize.text = "Кеш изображений: %.1f МБ".format(sizeMB)
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
            }

            updateCacheSize()
            Snackbar.make(binding.root, "Кеш очищен", Snackbar.LENGTH_SHORT).show()
        }
    }
}
