package com.barkfluff.client

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
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
import com.barkfluff.client.utils.FileSaveUtils
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
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
    private var progressUpdateJob: Job? = null

    private var fileId: String = ""
    private var fileName: String = ""
    private var cachedPath: String? = null
    private var isSeeking = false
    private var controlsVisible = true

    private var swipeTouchStartY = 0f
    private var swipeTouchStartTranslation = 0f

    companion object {
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
        cachedPath = file.absolutePath
        binding.loadingProgress.visibility = View.GONE
        initPlayer(Uri.fromFile(file))
    }

    private fun downloadAndPlay() {
        binding.loadingProgress.visibility = View.VISIBLE
        binding.controlsCard.visibility = View.INVISIBLE
        controlsVisible = false
        lifecycleScope.launch {
            val file = chatRepository.downloadFile(fileId)
            withContext(Dispatchers.Main) {
                binding.loadingProgress.visibility = View.GONE
                if (file != null) {
                    showControls()
                    setupPlayerWithFile(file)
                } else {
                    Toast.makeText(this@MediaViewerActivity, R.string.media_load_failed, Toast.LENGTH_SHORT).show()
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
                        updatePlaybackProgress()
                        startProgressUpdates()
                    }
                    if (state == Player.STATE_ENDED) {
                        binding.playPauseButton.setIconResource(R.drawable.ic_play_arrow)
                        binding.videoSeekBar.progress = 0
                        binding.timeText.text = getString(R.string.media_time_position, formatTime(0), formatTime(exo.duration))
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
        binding.playPauseButton.setIconResource(
            if (isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow
        )
        binding.playPauseButton.contentDescription = getString(
            if (isPlaying) R.string.cd_pause else R.string.cd_play
        )
    }

    private fun startProgressUpdates() {
        progressUpdateJob?.cancel()
        progressUpdateJob = lifecycleScope.launch {
            while (true) {
                delay(250)
                if (player == null) break
                if (!isSeeking) updatePlaybackProgress()
            }
        }
    }

    private fun updatePlaybackProgress() {
        val currentPlayer = player ?: return
        val duration = currentPlayer.duration.takeIf { it > 0 } ?: return
        val position = currentPlayer.currentPosition
        binding.videoSeekBar.progress = (position * 1000L / duration).toInt()
        binding.timeText.text = getString(R.string.media_time_position, formatTime(position), formatTime(duration))
    }

    private fun setupButtons() {
        binding.backButton.setOnClickListener { finishWithAnimation() }
        binding.playerView.setOnClickListener { toggleControls() }

        binding.playPauseButton.setOnClickListener {
            player?.let {
                if (it.isPlaying) it.pause() else it.play()
            }
        }

        binding.downloadButton.setOnClickListener { saveToDownloads() }

        binding.videoSeekBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(sb: SeekBar, progress: Int, fromUser: Boolean) {
                if (fromUser) {
                    val duration = player?.duration?.takeIf { it > 0 } ?: return
                    binding.timeText.text = getString(
                        R.string.media_time_position,
                        formatTime(progress.toLong() * duration / 1000L),
                        formatTime(duration)
                    )
                }
            }

            override fun onStartTrackingTouch(sb: SeekBar) {
                isSeeking = true
            }

            override fun onStopTrackingTouch(sb: SeekBar) {
                val duration = player?.duration?.takeIf { it > 0 }
                if (duration != null) {
                    player?.seekTo(sb.progress.toLong() * duration / 1000L)
                }
                isSeeking = false
            }
        })

        binding.moreButton.setOnClickListener { showMoreMenu(it) }
    }

    private fun toggleControls() {
        if (controlsVisible) hideControls() else showControls()
    }

    private fun hideControls() {
        if (!controlsVisible) return
        controlsVisible = false
        binding.controlsCard.animate()
            .alpha(0f)
            .translationY(24f * resources.displayMetrics.density)
            .setDuration(150)
            .withEndAction {
                if (!controlsVisible) binding.controlsCard.visibility = View.INVISIBLE
            }
            .start()
    }

    private fun showControls() {
        if (controlsVisible) return
        controlsVisible = true
        binding.controlsCard.apply {
            visibility = View.VISIBLE
            alpha = 0f
            translationY = 24f * resources.displayMetrics.density
            animate()
                .alpha(1f)
                .translationY(0f)
                .setDuration(150)
                .start()
        }
    }

    private fun showMoreMenu(anchor: View) {
        val popup = PopupMenu(this, anchor)
        popup.menu.add(0, 1, 0, R.string.file_remove_from_cache)
        popup.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                1 -> { deleteFromCacheAndClose(); true }
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
        Toast.makeText(this, R.string.file_removed_from_cache, Toast.LENGTH_SHORT).show()
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

    private fun saveToDownloads() {
        val videoFile = cachedPath?.let { File(it) }?.takeIf { it.exists() }
            ?: FileCache.getFile(fileId)
        if (videoFile == null) {
            Toast.makeText(this, R.string.file_not_in_cache, Toast.LENGTH_SHORT).show()
            return
        }

        val displayName = fileName.ifBlank {
            getString(R.string.media_default_video_filename, System.currentTimeMillis())
        }
        lifecycleScope.launch {
            val ok = withContext(Dispatchers.IO) {
                FileSaveUtils.saveToDownloads(this@MediaViewerActivity, videoFile, displayName)
            }
            Toast.makeText(
                this@MediaViewerActivity,
                if (ok) R.string.file_saved_to_downloads else R.string.file_save_failed,
                Toast.LENGTH_SHORT
            ).show()
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
        progressUpdateJob?.cancel()
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
