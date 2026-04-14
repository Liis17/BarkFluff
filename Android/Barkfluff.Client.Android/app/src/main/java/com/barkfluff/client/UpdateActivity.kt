package com.barkfluff.client

import android.app.DownloadManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.FileProvider
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityUpdateBinding
import com.barkfluff.client.utils.AppVersion
import com.barkfluff.client.utils.ChannelVersionInfo
import com.barkfluff.client.utils.UpdateChecker
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone

class UpdateActivity : AppCompatActivity() {

    private lateinit var binding: ActivityUpdateBinding

    private var releaseInfo: ChannelVersionInfo? = null
    private var betaInfo: ChannelVersionInfo? = null
    private var currentVersion: AppVersion? = null

    private var pendingDownloadChannel: String? = null
    private var downloadId: Long = -1
    private var downloadFileName: String? = null
    private var installTriggered = false

    companion object {
        private const val TAG = "UpdateActivity"
    }

    private val installPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) {
        if (packageManager.canRequestPackageInstalls()) {
            pendingDownloadChannel?.let { startDownload(it) }
        } else {
            Toast.makeText(this, "Разрешение на установку не предоставлено", Toast.LENGTH_SHORT).show()
        }
        pendingDownloadChannel = null
    }

    private val downloadReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            val id = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1)
            if (id == downloadId) {
                // Резервный путь — если корутина трекинга не поймала завершение
                installDownloadedApk()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityUpdateBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setupToolbar()
        setupCurrentVersion()
        setupClickListeners()
        checkUpdates()

        registerReceiver(
            downloadReceiver,
            IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE),
            RECEIVER_NOT_EXPORTED
        )
    }

    override fun onDestroy() {
        super.onDestroy()
        try {
            unregisterReceiver(downloadReceiver)
        } catch (_: Exception) {}
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun setupCurrentVersion() {
        val versionName = GlobalParam.getAppVersion(this)
        currentVersion = AppVersion.parse(versionName)
        binding.textCurrentVersion.text = versionName
        binding.textCurrentChannel.text = if (currentVersion?.isBeta == true) "Beta-канал" else "Release-канал"
    }

    private fun setupClickListeners() {
        binding.buttonCheckUpdates.setOnClickListener {
            checkUpdates()
        }
        binding.buttonUpdateRelease.setOnClickListener {
            requestDownload("release")
        }
        binding.buttonUpdateBeta.setOnClickListener {
            requestDownload("beta")
        }
        binding.buttonOpenWebsite.setOnClickListener {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse("https://barkfluff.com"))
            startActivity(intent)
        }
    }

    private fun checkUpdates() {
        binding.buttonCheckUpdates.isEnabled = false
        binding.buttonCheckUpdates.text = "Проверка..."
        binding.textReleaseVersion.text = "Загрузка..."
        binding.textBetaVersion.text = "Загрузка..."
        binding.buttonUpdateRelease.visibility = View.GONE
        binding.buttonUpdateBeta.visibility = View.GONE
        binding.textReleaseDate.visibility = View.GONE
        binding.textBetaDate.visibility = View.GONE
        binding.textReleaseStatus.text = ""
        binding.textBetaStatus.text = ""

        lifecycleScope.launch {
            releaseInfo = UpdateChecker.getVersionInfo("release")
            betaInfo = UpdateChecker.getVersionInfo("beta")

            updateChannelUI(
                info = releaseInfo,
                textVersion = binding.textReleaseVersion,
                textDate = binding.textReleaseDate,
                textStatus = binding.textReleaseStatus,
                button = binding.buttonUpdateRelease
            )

            updateChannelUI(
                info = betaInfo,
                textVersion = binding.textBetaVersion,
                textDate = binding.textBetaDate,
                textStatus = binding.textBetaStatus,
                button = binding.buttonUpdateBeta
            )

            binding.buttonCheckUpdates.isEnabled = true
            binding.buttonCheckUpdates.text = "Проверить обновления"
        }
    }

    private fun updateChannelUI(
        info: ChannelVersionInfo?,
        textVersion: android.widget.TextView,
        textDate: android.widget.TextView,
        textStatus: android.widget.TextView,
        button: com.google.android.material.button.MaterialButton
    ) {
        if (info == null) {
            textVersion.text = "Недоступно"
            return
        }

        val version = info.version
        if (version.isNullOrBlank()) {
            textVersion.text = "Нет данных"
            return
        }

        textVersion.text = "Версия: $version"

        info.uploadedAt?.let { dateStr ->
            val formatted = formatDate(dateStr)
            if (formatted != null) {
                textDate.text = "Обновлено: $formatted"
                textDate.visibility = View.VISIBLE
            }
        }

        val remoteVersion = AppVersion.parse(version)
        if (remoteVersion != null && currentVersion != null) {
            when {
                remoteVersion > currentVersion!! -> {
                    button.visibility = View.VISIBLE
                    textStatus.text = "Доступно обновление"
                    textStatus.setTextColor(getColor(android.R.color.holo_green_dark))
                }
                remoteVersion == currentVersion -> {
                    textStatus.text = "Установлено"
                }
                else -> {
                    textStatus.text = "Ниже текущей"
                }
            }
        }
    }

    private fun formatDate(isoDate: String): String? {
        return try {
            val inputFormat = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.getDefault())
            inputFormat.timeZone = TimeZone.getTimeZone("UTC")
            val date = inputFormat.parse(isoDate.substringBefore('.').substringBefore('Z')) ?: return null
            val outputFormat = SimpleDateFormat("d MMMM yyyy, HH:mm", Locale("ru"))
            outputFormat.format(date)
        } catch (e: Exception) {
            Log.w(TAG, "Failed to parse date: $isoDate", e)
            null
        }
    }

    private fun requestDownload(channel: String) {
        if (!packageManager.canRequestPackageInstalls()) {
            pendingDownloadChannel = channel
            val intent = Intent(
                Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:$packageName")
            )
            installPermissionLauncher.launch(intent)
            return
        }
        startDownload(channel)
    }

    private fun startDownload(channel: String) {
        val url = UpdateChecker.getDownloadUrl(channel)
        val fileName = "barkfluff_${channel}_update.apk"
        downloadFileName = fileName
        installTriggered = false

        // Удаляем старый файл если есть
        val file = File(
            Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
            fileName
        )
        if (file.exists()) file.delete()

        binding.cardProgress.visibility = View.VISIBLE
        binding.textDownloadStatus.text = "Загрузка обновления..."
        binding.progressDownload.isIndeterminate = false
        binding.progressDownload.progress = 0
        binding.textDownloadPercent.text = "0%"
        binding.buttonUpdateRelease.isEnabled = false
        binding.buttonUpdateBeta.isEnabled = false

        val request = DownloadManager.Request(Uri.parse(url))
            .setTitle("Обновление BarkFluff")
            .setDescription("Загрузка $channel версии")
            .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, fileName)
            .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE)
            .setMimeType("application/vnd.android.package-archive")

        val dm = getSystemService(DOWNLOAD_SERVICE) as DownloadManager
        downloadId = dm.enqueue(request)

        trackDownloadProgress(dm)
    }

    private fun trackDownloadProgress(dm: DownloadManager) {
        lifecycleScope.launch {
            var finalStatus = -1
            while (isActive) {
                val query = DownloadManager.Query().setFilterById(downloadId)
                val cursor = dm.query(query)
                if (cursor != null && cursor.moveToFirst()) {
                    val bytesDownloaded = cursor.getLong(
                        cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_BYTES_DOWNLOADED_SO_FAR)
                    )
                    val bytesTotal = cursor.getLong(
                        cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_TOTAL_SIZE_BYTES)
                    )
                    val status = cursor.getInt(
                        cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_STATUS)
                    )
                    cursor.close()

                    if (bytesTotal > 0) {
                        val percent = (bytesDownloaded * 100 / bytesTotal).toInt()
                        binding.progressDownload.progress = percent
                        binding.textDownloadPercent.text = "$percent% (${formatSize(bytesDownloaded)} / ${formatSize(bytesTotal)})"
                    }

                    if (status == DownloadManager.STATUS_SUCCESSFUL ||
                        status == DownloadManager.STATUS_FAILED
                    ) {
                        finalStatus = status
                        break
                    }
                } else {
                    cursor?.close()
                    break
                }
                delay(300)
            }

            // Обрабатываем завершение прямо здесь
            binding.buttonUpdateRelease.isEnabled = true
            binding.buttonUpdateBeta.isEnabled = true

            if (finalStatus == DownloadManager.STATUS_SUCCESSFUL) {
                binding.textDownloadStatus.text = "Загрузка завершена"
                binding.progressDownload.progress = 100
                installDownloadedApk()
            } else {
                binding.textDownloadStatus.text = "Ошибка загрузки"
                Toast.makeText(this@UpdateActivity, "Ошибка загрузки обновления", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun scheduleInstallErrorHint() {
        lifecycleScope.launch {
            delay(5_000)
            // Если мы здесь — значит процесс не был убит установщиком, скорее всего ошибка
            binding.cardInstallError.visibility = View.VISIBLE
        }
    }

    private fun formatSize(bytes: Long): String {
        return when {
            bytes >= 1_048_576 -> String.format("%.1f МБ", bytes / 1_048_576.0)
            bytes >= 1024 -> String.format("%.0f КБ", bytes / 1024.0)
            else -> "$bytes Б"
        }
    }

    private fun installDownloadedApk() {
        // Защита от двойного вызова (корутина + BroadcastReceiver)
        if (installTriggered) return
        installTriggered = true

        try {
            val dm = getSystemService(DOWNLOAD_SERVICE) as DownloadManager

            // Копируем APK из Downloads в internal cache через DownloadManager.openDownloadedFile().
            // На Android 11+ (API 30+) requestLegacyExternalStorage игнорируется, и прямое чтение
            // файла из публичного Downloads через FileInputStream может не сработать из-за scoped
            // storage. openDownloadedFile() всегда работает, т.к. приложение владеет загрузкой.
            val destFile = File(cacheDir, "update_pending.apk")
            try {
                dm.openDownloadedFile(downloadId).use { pfd ->
                    java.io.FileInputStream(pfd.fileDescriptor).use { input ->
                        destFile.outputStream().use { output ->
                            input.copyTo(output)
                        }
                    }
                }
            } catch (e: Exception) {
                Log.w(TAG, "openDownloadedFile failed, falling back to direct path", e)
                // Запасной вариант: прямое чтение из Downloads (работает на Android 10-)
                val fileName = downloadFileName ?: run {
                    Toast.makeText(this, "Файл обновления не найден", Toast.LENGTH_SHORT).show()
                    installTriggered = false
                    return
                }
                val srcFile = File(
                    Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                    fileName
                )
                if (!srcFile.exists()) {
                    Log.e(TAG, "APK not found at: ${srcFile.absolutePath}")
                    Toast.makeText(this, "Файл обновления не найден", Toast.LENGTH_SHORT).show()
                    installTriggered = false
                    return
                }
                srcFile.copyTo(destFile, overwrite = true)
            }

            if (!destFile.exists() || destFile.length() < 100_000L) {
                Log.e(TAG, "APK invalid: size=${destFile.length()} — возможно сервер вернул не APK")
                Toast.makeText(this, "Ошибка: загруженный файл повреждён", Toast.LENGTH_SHORT).show()
                installTriggered = false
                return
            }

            val contentUri = FileProvider.getUriForFile(
                this,
                "${packageName}.fileprovider",
                destFile
            )

            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(contentUri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            startActivity(intent)
            // Если установка прошла успешно — процесс будет убит и корутина не выполнится.
            // Если приложение продолжает работать (ошибка установки) — через 5 сек покажем подсказку.
            scheduleInstallErrorHint()
        } catch (e: Exception) {
            Log.e(TAG, "Error installing APK", e)
            Toast.makeText(this, "Ошибка установки: ${e.message}", Toast.LENGTH_SHORT).show()
            installTriggered = false
        }
    }
}
