package com.barkfluff.client

import android.content.Intent
import android.net.Uri
import android.os.Bundle
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
import com.barkfluff.client.utils.UpdateServerTls
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone
import java.util.concurrent.TimeUnit

class UpdateActivity : AppCompatActivity() {

    private lateinit var binding: ActivityUpdateBinding

    private var currentVersion: AppVersion? = null

    private var pendingDownloadChannel: String? = null
    private var installTriggered = false

    companion object {
        private const val TAG = "UpdateActivity"

        /** Тот же файл чистит BarkFluffApplication.cleanupPendingUpdate() при старте. */
        private const val UPDATE_FILE_NAME = "update_pending.apk"
        private const val MIN_APK_SIZE_BYTES = 100_000L
        private const val DOWNLOAD_BUFFER_BYTES = 64 * 1024
        private const val PROGRESS_INTERVAL_MS = 100L
        private const val CHANNEL_RELEASE = "release"
        private const val CHANNEL_DEV = "dev"
        private const val CHANNEL_NIGHTLY = "nightly"
    }

    private val installPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) {
        if (packageManager.canRequestPackageInstalls()) {
            pendingDownloadChannel?.let { startDownload(it) }
        } else {
            Toast.makeText(this, R.string.update_install_permission_denied, Toast.LENGTH_SHORT).show()
        }
        pendingDownloadChannel = null
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityUpdateBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setupToolbar()
        setupCurrentVersion()
        setupClickListeners()
        checkUpdates()
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
        binding.textCurrentChannel.text = getString(
            when (BuildConfig.UPDATE_CHANNEL) {
                CHANNEL_DEV -> R.string.update_current_dev_channel
                CHANNEL_NIGHTLY -> R.string.update_current_nightly_channel
                else -> R.string.update_current_release_channel
            }
        )
    }

    private fun setupClickListeners() {
        binding.buttonCheckUpdates.setOnClickListener {
            checkUpdates()
        }
        binding.buttonUpdateRelease.setOnClickListener {
            requestDownload(CHANNEL_RELEASE)
        }
        binding.buttonUpdateDev.setOnClickListener {
            requestDownload(CHANNEL_DEV)
        }
        binding.buttonUpdateNightly.setOnClickListener {
            requestDownload(CHANNEL_NIGHTLY)
        }
        binding.buttonOpenWebsite.setOnClickListener {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse("https://barkfluff.com"))
            startActivity(intent)
        }
    }

    private fun checkUpdates() {
        binding.buttonCheckUpdates.isEnabled = false
        binding.buttonCheckUpdates.text = getString(R.string.update_checking)

        val channels = listOf(
            ChannelViews(
                channel = CHANNEL_RELEASE,
                textVersion = binding.textReleaseVersion,
                textDate = binding.textReleaseDate,
                textStatus = binding.textReleaseStatus,
                button = binding.buttonUpdateRelease
            ),
            ChannelViews(
                channel = CHANNEL_DEV,
                textVersion = binding.textDevVersion,
                textDate = binding.textDevDate,
                textStatus = binding.textDevStatus,
                button = binding.buttonUpdateDev
            ),
            ChannelViews(
                channel = CHANNEL_NIGHTLY,
                textVersion = binding.textNightlyVersion,
                textDate = binding.textNightlyDate,
                textStatus = binding.textNightlyStatus,
                button = binding.buttonUpdateNightly
            )
        )

        channels.forEach { views ->
            views.textVersion.text = getString(R.string.loading_dots)
            views.textDate.visibility = View.GONE
            views.textStatus.text = ""
            views.button.visibility = View.GONE
        }

        lifecycleScope.launch {
            channels.forEach { views ->
                updateChannelUI(views, UpdateChecker.getVersionInfo(views.channel))
            }

            binding.buttonCheckUpdates.isEnabled = true
            binding.buttonCheckUpdates.text = getString(R.string.update_check)
        }
    }

    private data class ChannelViews(
        val channel: String,
        val textVersion: android.widget.TextView,
        val textDate: android.widget.TextView,
        val textStatus: android.widget.TextView,
        val button: com.google.android.material.button.MaterialButton
    )

    private fun updateChannelUI(views: ChannelViews, info: ChannelVersionInfo?) {
        val textVersion = views.textVersion
        val textDate = views.textDate
        val textStatus = views.textStatus
        val button = views.button

        if (info == null) {
            textVersion.text = getString(R.string.update_unavailable)
            return
        }

        val version = info.version
        if (version.isNullOrBlank()) {
            textVersion.text = getString(R.string.update_no_data)
            return
        }

        textVersion.text = getString(R.string.update_version, version)

        info.uploadedAt?.let { dateStr ->
            val formatted = formatDate(dateStr)
            if (formatted != null) {
                textDate.text = getString(R.string.update_updated, formatted)
                textDate.visibility = View.VISIBLE
            }
        }

        val remoteVersion = AppVersion.parse(version)
        if (remoteVersion != null && currentVersion != null) {
            when {
                remoteVersion > currentVersion!! -> {
                    // Обновляться можно только по своему каналу: у чужих каналов другой
                    // applicationId, их APK встал бы вторым приложением, а не обновлением.
                    if (views.channel == BuildConfig.UPDATE_CHANNEL) {
                        button.visibility = View.VISIBLE
                    }
                    textStatus.text = getString(R.string.update_available)
                    textStatus.setTextColor(getColor(android.R.color.holo_green_dark))
                }
                remoteVersion == currentVersion -> {
                    textStatus.text = getString(R.string.update_installed)
                }
                else -> {
                    textStatus.text = getString(R.string.update_below_current)
                }
            }
        }
    }

    private fun setChannelButtonsEnabled(enabled: Boolean) {
        binding.buttonUpdateRelease.isEnabled = enabled
        binding.buttonUpdateDev.isEnabled = enabled
        binding.buttonUpdateNightly.isEnabled = enabled
    }

    private fun formatDate(isoDate: String): String? {
        return try {
            val inputFormat = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.getDefault())
            inputFormat.timeZone = TimeZone.getTimeZone("UTC")
            val date = inputFormat.parse(isoDate.substringBefore('.').substringBefore('Z')) ?: return null
            val outputFormat = SimpleDateFormat(
                "d MMMM yyyy, HH:mm",
                resources.configuration.locales[0]
            )
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

    /**
     * Качаем APK сами, а не системным DownloadManager: тот работает в отдельном системном
     * процессе, в него нельзя подставить SSLSocketFactory, поэтому CA сервера обновлений
     * (см. [UpdateServerTls]) для него недоступен и TLS-рукопожатие не проходит.
     */
    private fun startDownload(channel: String) {
        val url = UpdateChecker.getDownloadUrl(channel).toHttpUrlOrNull()
        if (url == null) {
            Toast.makeText(this, R.string.update_invalid_url, Toast.LENGTH_SHORT).show()
            return
        }

        installTriggered = false
        val destFile = File(cacheDir, UPDATE_FILE_NAME)

        binding.cardProgress.visibility = View.VISIBLE
        binding.textDownloadStatus.text = getString(R.string.update_downloading)
        binding.progressDownload.isIndeterminate = false
        binding.progressDownload.progress = 0
        binding.textDownloadPercent.text = getString(R.string.update_percent, 0)
        setChannelButtonsEnabled(false)

        lifecycleScope.launch {
            val result = runCatching { downloadApk(url, destFile) }
            // runCatching глотает и отмену — пробрасываем её обратно, чтобы не трогать UI
            // уничтоженной активити.
            val error = result.exceptionOrNull()
            if (error is CancellationException) throw error

            setChannelButtonsEnabled(true)

            result
                .onSuccess {
                    binding.textDownloadStatus.text = getString(R.string.update_download_complete)
                    binding.progressDownload.isIndeterminate = false
                    binding.progressDownload.progress = 100
                    installDownloadedApk(destFile)
                }
                .onFailure { cause ->
                    Log.e(TAG, "Ошибка загрузки обновления", cause)
                    runCatching { destFile.delete() }
                    binding.textDownloadStatus.text = getString(R.string.update_download_error)
                    Toast.makeText(this@UpdateActivity, R.string.update_download_failed, Toast.LENGTH_SHORT).show()
                }
        }
    }

    private suspend fun downloadApk(url: HttpUrl, destFile: File) = withContext(Dispatchers.IO) {
        val builder = OkHttpClient.Builder()
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(60, TimeUnit.SECONDS)
            // APK крупный — общий таймаут на вызов выключен, ограничиваемся паузами в чтении.
            .callTimeout(0, TimeUnit.MILLISECONDS)
        UpdateServerTls.trust?.let { trust ->
            builder.sslSocketFactory(trust.socketFactory, trust.trustManager)
        }
        val client = builder.build()

        val request = Request.Builder().url(url).get().build()
        client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) throw IOException("Сервер обновлений вернул HTTP ${response.code}")
            val body = response.body ?: throw IOException("Пустой ответ сервера обновлений")

            val total = body.contentLength()
            withContext(Dispatchers.Main) { binding.progressDownload.isIndeterminate = total <= 0 }

            body.byteStream().use { input ->
                destFile.outputStream().use { output ->
                    copyWithProgress(input, output, total)
                }
            }
        }
    }

    private suspend fun copyWithProgress(input: InputStream, output: OutputStream, total: Long) {
        val buffer = ByteArray(DOWNLOAD_BUFFER_BYTES)
        var downloaded = 0L
        var lastReportAt = 0L

        while (true) {
            currentCoroutineContext().ensureActive()
            val read = input.read(buffer)
            if (read == -1) break
            output.write(buffer, 0, read)
            downloaded += read

            val now = System.currentTimeMillis()
            if (now - lastReportAt >= PROGRESS_INTERVAL_MS) {
                lastReportAt = now
                withContext(Dispatchers.Main) { showProgress(downloaded, total) }
            }
        }

        withContext(Dispatchers.Main) { showProgress(downloaded, total) }
    }

    private fun showProgress(downloaded: Long, total: Long) {
        if (total > 0) {
            val percent = (downloaded * 100 / total).toInt()
            binding.progressDownload.progress = percent
            binding.textDownloadPercent.text = getString(
                R.string.update_download_progress,
                percent,
                formatSize(downloaded),
                formatSize(total)
            )
        } else {
            binding.textDownloadPercent.text = getString(R.string.update_downloaded_size, formatSize(downloaded))
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
            bytes >= 1_048_576 -> getString(R.string.update_size_megabytes, bytes / 1_048_576.0)
            bytes >= 1024 -> getString(R.string.update_size_kilobytes, bytes / 1024.0)
            else -> getString(R.string.update_size_bytes, bytes)
        }
    }

    private fun installDownloadedApk(destFile: File) {
        // Защита от двойного вызова
        if (installTriggered) return
        installTriggered = true

        try {
            if (!destFile.exists() || destFile.length() < MIN_APK_SIZE_BYTES) {
                Log.e(TAG, "APK invalid: size=${destFile.length()} — возможно сервер вернул не APK")
                Toast.makeText(this, R.string.update_corrupt_file, Toast.LENGTH_SHORT).show()
                installTriggered = false
                return
            }

            val contentUri = FileProvider.getUriForFile(
                this,
                "${packageName}.fileprovider",
                destFile
            )

            // Чистить APK после установки не нужно: он лежит в cacheDir, и
            // BarkFluffApplication.cleanupPendingUpdate() удаляет его при следующем старте.

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
            Toast.makeText(this, getString(R.string.update_install_error, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
            installTriggered = false
        }
    }

}
