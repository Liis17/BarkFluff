package com.barkfluff.client

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import android.view.MotionEvent
import android.view.View
import android.widget.PopupMenu
import android.widget.SeekBar
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.media3.common.MediaItem
import androidx.media3.common.Player
import androidx.media3.exoplayer.ExoPlayer
import com.barkfluff.client.databinding.ActivityMediaViewerBinding
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.FileCache
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Активити для просмотра видеофайлов с ExoPlayer.
 * Не полноэкранный — статус-бар остаётся видимым.
 * Поддерживает свайп вниз для закрытия с анимацией.
 */
class MediaViewerActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMediaViewerBinding
    private lateinit var chatRepository: ChatRepository
    private var player: ExoPlayer? = null

    private var fileId: String = ""
    private var fileName: String = ""
    private var cachedPath: String? = null

    private var swipeTouchStartY = 0f
    private var swipeTouchStartTranslation = 0f

    companion object {
        private const val TAG = "MediaViewerActivity"
        const val RESULT_CACHE_DELETED = 100
        const val EXTRA_FILE_ID = "file_id"
        private const val EXTRA_FILE_NAME = "file_name"
        private const val EXTRA_CACHED_PATH = "cached_path"

        fun createIntent(
            context: Context,
            fileId: String,
            fileName: String,
            cachedPath: String? = null
        ): Intent = Intent(context, MediaViewerActivity::class.java).apply {
            putExtra(EXTRA_FILE_ID, fileId)
            putExtra(EXTRA_FILE_NAME, fileName)
            putExtra(EXTRA_CACHED_PATH, cachedPath)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMediaViewerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        chatRepository = ChatRepository(this, (application as BarkFluffApplication).grpcManager)

        fileId = intent.getStringExtra(EXTRA_FILE_ID) ?: ""
        fileName = intent.getStringExtra(EXTRA_FILE_NAME) ?: "video"
        cachedPath = intent.getStringExtra(EXTRA_CACHED_PATH)

        if (fileId.isEmpty()) { finish(); return }

        setupButtons()
        setupSwipeDismiss()

        val existingCache = cachedPath?.let { File(it) }?.takeIf { it.exists() }
            ?: FileCache.getFile(fileId)

        if (existingCache != null) {
            setupPlayerWithFile(existingCache)
        } else {
            downloadAndPlay()
        }
    }

    private fun setupPlayerWithFile(file: File) {
        binding.loadingProgress.visibility = View.GONE
        val uri = Uri.fromFile(file)
        initPlayer(uri)
    }

    private fun downloadAndPlay() {
        binding.loadingProgress.visibility = View.VISIBLE
        binding.bottomBar.visibility = View.GONE
        lifecycleScope.launch {
            val file = chatRepository.downloadFile(fileId)
            withContext(Dispatchers.Main) {
                binding.loadingProgress.visibility = View.GONE
                if (file != null) {
                    binding.bottomBar.visibility = View.VISIBLE
                    setupPlayerWithFile(file)
                } else {
                    Toast.makeText(this@MediaViewerActivity, "Ошибка загрузки", Toast.LENGTH_SHORT).show()
                    finish()
                }
            }
        }
    }

    private fun initPlayer(uri: Uri) {
        player = ExoPlayer.Builder(this).build().also { exo ->
            binding.playerView.player = exo
            exo.setMediaItem(MediaItem.fromUri(uri))
            exo.prepare()
            exo.playWhenReady = true

            exo.addListener(object : Player.Listener {
                override fun onPlaybackStateChanged(state: Int) {
                    if (state == Player.STATE_READY) {
                        updatePlayPauseIcon()
                        startProgressUpdates()
                    }
                    if (state == Player.STATE_ENDED) {
                        binding.playPauseButton.setImageResource(R.drawable.ic_play_arrow)
                        binding.videoSeekBar.progress = 0
                        exo.seekTo(0)
                        exo.playWhenReady = false
                    }
                }
                override fun onIsPlayingChanged(isPlaying: Boolean) {
                    updatePlayPauseIcon()
                }
            })
        }
    }

    private fun updatePlayPauseIcon() {
        val isPlaying = player?.isPlaying == true
        binding.playPauseButton.setImageResource(
            if (isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow
        )
    }

    private fun startProgressUpdates() {
        lifecycleScope.launch {
            while (true) {
                delay(250)
                val p = player ?: break
                val duration = p.duration.takeIf { it > 0 } ?: continue
                val position = p.currentPosition
                binding.videoSeekBar.progress = (position * 1000L / duration).toInt()
                binding.timeText.text = "${formatTime(position)} / ${formatTime(duration)}"
            }
        }
    }

    private fun setupButtons() {
        binding.backButton.setOnClickListener { finishWithAnimation() }

        binding.playPauseButton.setOnClickListener {
            player?.let {
                if (it.isPlaying) it.pause() else it.play()
            }
        }

        binding.videoSeekBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(sb: SeekBar, progress: Int, fromUser: Boolean) {
                if (fromUser) {
                    val duration = player?.duration ?: return
                    if (duration > 0) player?.seekTo(progress.toLong() * duration / 1000L)
                }
            }
            override fun onStartTrackingTouch(sb: SeekBar) {}
            override fun onStopTrackingTouch(sb: SeekBar) {}
        })

        binding.moreButton.setOnClickListener { showMoreMenu(it) }
    }

    private fun showMoreMenu(anchor: View) {
        val popup = PopupMenu(this, anchor)
        popup.menu.add(0, 1, 0, "Сохранить")
        popup.menu.add(0, 2, 1, "Удалить из кэша")
        popup.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                1 -> { saveToGallery(); true }
                2 -> { deleteFromCacheAndClose(); true }
                else -> false
            }
        }
        popup.show()
    }

    private fun deleteFromCacheAndClose() {
        player?.stop()
        player?.release()
        player = null
        FileCache.deleteFile(fileId)
        Toast.makeText(this, "Видео удалено из кэша", Toast.LENGTH_SHORT).show()
        setResult(RESULT_CACHE_DELETED)
        finish()
    }

    private fun setupSwipeDismiss() {
        binding.rootLayout.setOnTouchListener { v, event ->
            when (event.action) {
                MotionEvent.ACTION_DOWN -> {
                    swipeTouchStartY = event.rawY
                    swipeTouchStartTranslation = v.translationY
                    false
                }
                MotionEvent.ACTION_MOVE -> {
                    val delta = event.rawY - swipeTouchStartY
                    if (delta > 0) {
                        v.translationY = delta
                        val screenH = resources.displayMetrics.heightPixels.toFloat()
                        v.alpha = 1f - (delta / (screenH * 0.5f)).coerceIn(0f, 1f)
                        true
                    } else false
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    val delta = event.rawY - swipeTouchStartY
                    val screenH = resources.displayMetrics.heightPixels.toFloat()
                    if (delta > screenH * 0.25f) {
                        finishWithAnimation()
                    } else {
                        v.animate().translationY(0f).alpha(1f).setDuration(200).start()
                    }
                    false
                }
                else -> false
            }
        }
    }

    private fun finishWithAnimation() {
        val screenH = resources.displayMetrics.heightPixels.toFloat()
        binding.rootLayout.animate()
            .translationY(screenH)
            .alpha(0f)
            .setDuration(250)
            .withEndAction { finish() }
            .start()
    }

    private fun saveToGallery() {
        val cachedFile = FileCache.getFile(fileId) ?: run {
            Toast.makeText(this, "Файл не найден в кэше", Toast.LENGTH_SHORT).show()
            return
        }
        lifecycleScope.launch {
            withContext(Dispatchers.IO) {
                try {
                    val displayName = "BarkFluff_${System.currentTimeMillis()}.mp4"
                    val contentValues = ContentValues().apply {
                        put(MediaStore.Video.Media.DISPLAY_NAME, displayName)
                        put(MediaStore.Video.Media.MIME_TYPE, "video/mp4")
                        put(MediaStore.Video.Media.RELATIVE_PATH, "${Environment.DIRECTORY_MOVIES}/BarkFluff")
                        put(MediaStore.Video.Media.IS_PENDING, 1)
                    }
                    val uri = contentResolver.insert(
                        MediaStore.Video.Media.EXTERNAL_CONTENT_URI, contentValues
                    )
                    if (uri != null) {
                        contentResolver.openOutputStream(uri)?.use { out ->
                            cachedFile.inputStream().use { it.copyTo(out) }
                        }
                        contentValues.clear()
                        contentValues.put(MediaStore.Video.Media.IS_PENDING, 0)
                        contentResolver.update(uri, contentValues, null, null)
                        withContext(Dispatchers.Main) {
                            Toast.makeText(this@MediaViewerActivity, "Сохранено в Видео/BarkFluff", Toast.LENGTH_SHORT).show()
                        }
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error saving video", e)
                    withContext(Dispatchers.Main) {
                        Toast.makeText(this@MediaViewerActivity, "Ошибка сохранения", Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }
    }

    private fun formatTime(ms: Long): String {
        if (ms <= 0) return "0:00"
        val totalSec = ms / 1000
        val min = totalSec / 60
        val sec = totalSec % 60
        return "%d:%02d".format(min, sec)
    }

    override fun onDestroy() {
        player?.release()
        player = null
        chatRepository.close()
        super.onDestroy()
    }

    override fun onPause() {
        super.onPause()
        player?.pause()
    }
}
