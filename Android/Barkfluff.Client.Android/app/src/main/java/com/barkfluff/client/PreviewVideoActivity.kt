package com.barkfluff.client

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import androidx.media3.common.MediaItem
import androidx.media3.exoplayer.ExoPlayer
import com.barkfluff.client.databinding.ActivityPreviewVideoBinding

/**
 * Простой активити для просмотра локального видео до отправки.
 * В будущем сюда будут добавлены инструменты редактирования и кнопка отправки.
 */
class PreviewVideoActivity : AppCompatActivity() {

    private lateinit var binding: ActivityPreviewVideoBinding
    private var player: ExoPlayer? = null

    companion object {
        private const val EXTRA_URI = "uri"

        fun createIntent(context: Context, uri: Uri): Intent {
            return Intent(context, PreviewVideoActivity::class.java).apply {
                putExtra(EXTRA_URI, uri)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPreviewVideoBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val uri: Uri = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(EXTRA_URI, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_URI)
        } ?: run {
            finish()
            return
        }

        player = ExoPlayer.Builder(this).build().also { exo ->
            binding.playerView.player = exo
            exo.setMediaItem(MediaItem.fromUri(uri))
            exo.prepare()
            exo.playWhenReady = true
        }

        binding.backButton.setOnClickListener { finish() }
    }

    override fun onPause() {
        super.onPause()
        player?.pause()
    }

    override fun onDestroy() {
        super.onDestroy()
        player?.release()
        player = null
    }
}
