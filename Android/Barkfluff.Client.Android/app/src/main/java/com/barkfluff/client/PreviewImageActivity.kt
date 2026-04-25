package com.barkfluff.client

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import coil.load
import com.barkfluff.client.databinding.ActivityPreviewImageBinding

/**
 * Простой активити для просмотра локальной картинки до отправки.
 * В будущем сюда будут добавлены инструменты редактирования и кнопка отправки.
 */
class PreviewImageActivity : AppCompatActivity() {

    private lateinit var binding: ActivityPreviewImageBinding

    companion object {
        private const val EXTRA_URI = "uri"

        fun createIntent(context: Context, uri: Uri): Intent {
            return Intent(context, PreviewImageActivity::class.java).apply {
                putExtra(EXTRA_URI, uri)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPreviewImageBinding.inflate(layoutInflater)
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

        binding.photoView.load(uri) {
            crossfade(true)
        }

        binding.backButton.setOnClickListener { finish() }
    }
}
