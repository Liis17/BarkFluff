package com.barkfluff.client.editor

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updateLayoutParams
import androidx.core.view.updateMargins
import androidx.media3.common.MediaItem
import androidx.media3.exoplayer.ExoPlayer
import androidx.recyclerview.widget.RecyclerView
import androidx.viewpager2.widget.ViewPager2
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ActivityVideoEditorBinding
import com.barkfluff.client.databinding.PageVideoEditorBinding

/**
 * Редактор видео: ViewPager2 по списку URI видео + общий ExoPlayer на activity, привязываемый
 * к активной странице. Под видео — таймлайн обрезки и тогл сжатия 480p.
 *
 * Передаёт результат обратно в caller (ImagePickerBottomSheet) аналогично MediaEditorActivity.
 */
class VideoEditorActivity : AppCompatActivity() {

    private lateinit var binding: ActivityVideoEditorBinding
    private lateinit var adapter: PagerAdapter

    private val allUris = mutableListOf<Uri>()
    private val selectedUris = mutableListOf<Uri>()

    private var player: ExoPlayer? = null
    private val mainHandler = Handler(Looper.getMainLooper())
    private val playPositionTicker: Runnable = object : Runnable {
        override fun run() {
            val p = player
            if (p != null && p.isPlaying) {
                binding.trimmer.setPlayPosition(p.currentPosition)
            }
            mainHandler.postDelayed(this, 250)
        }
    }

    companion object {
        const val EXTRA_ALL_URIS = "all_uris"
        const val EXTRA_START_URI = "start_uri"
        const val EXTRA_PRESELECTED_URIS = "preselected_uris"
        const val EXTRA_CAPTION = "caption"
        const val EXTRA_RESULT_URIS = "result_uris"
        const val EXTRA_RESULT_CAPTION = "result_caption"
        const val EXTRA_RESULT_SEND = "result_send"

        fun newIntent(
            context: Context,
            allUris: List<Uri>,
            startUri: Uri,
            preselected: List<Uri>,
            caption: String
        ): Intent {
            return Intent(context, VideoEditorActivity::class.java).apply {
                putParcelableArrayListExtra(EXTRA_ALL_URIS, ArrayList(allUris))
                putExtra(EXTRA_START_URI, startUri)
                putParcelableArrayListExtra(EXTRA_PRESELECTED_URIS, ArrayList(preselected))
                putExtra(EXTRA_CAPTION, caption)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityVideoEditorBinding.inflate(layoutInflater)
        setContentView(binding.root)

        readIntent()
        applyInsets()
        setupPlayer()
        setupViewPager()
        setupTopBar()
        setupBottomBar()
        setupTrimmerControls()
        setupCompressSwitch()
        setupBackPress()
    }

    private fun readIntent() {
        val all: ArrayList<Uri>? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableArrayListExtra(EXTRA_ALL_URIS, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableArrayListExtra(EXTRA_ALL_URIS)
        }
        val pre: ArrayList<Uri>? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableArrayListExtra(EXTRA_PRESELECTED_URIS, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableArrayListExtra(EXTRA_PRESELECTED_URIS)
        }
        val start: Uri? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(EXTRA_START_URI, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_START_URI)
        }

        if (all == null || all.isEmpty() || start == null) {
            finish()
            return
        }
        allUris.addAll(all)
        pre?.let { selectedUris.addAll(it) }
        if (!selectedUris.contains(start)) selectedUris.add(start)

        binding.captionEditText.setText(intent.getStringExtra(EXTRA_CAPTION) ?: "")
    }

    private fun applyInsets() {
        ViewCompat.setOnApplyWindowInsetsListener(binding.root) { _, insets ->
            val top = insets.getInsets(WindowInsetsCompat.Type.systemBars()).top
            val bottom = insets.getInsets(WindowInsetsCompat.Type.systemBars()).bottom
            binding.topBar.updateLayoutParams<ViewGroup.MarginLayoutParams> {
                updateMargins(top = top)
            }
            binding.bottomBar.updateLayoutParams<ViewGroup.MarginLayoutParams> {
                updateMargins(bottom = bottom)
            }
            insets
        }
    }

    private fun setupPlayer() {
        player = ExoPlayer.Builder(this).build()
    }

    private fun setupViewPager() {
        adapter = PagerAdapter(allUris)
        binding.viewPager.adapter = adapter
        binding.viewPager.offscreenPageLimit = 1

        val start: Uri? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(EXTRA_START_URI, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_START_URI)
        }
        val idx = start?.let { allUris.indexOf(it) } ?: 0
        binding.viewPager.setCurrentItem(idx.coerceAtLeast(0), false)

        binding.viewPager.registerOnPageChangeCallback(object : ViewPager2.OnPageChangeCallback() {
            override fun onPageSelected(position: Int) {
                bindPlayerToPage(position)
                updateTopBar()
            }
        })

        binding.viewPager.post { bindPlayerToPage(binding.viewPager.currentItem) }
        updateTopBar()
    }

    private fun bindPlayerToPage(position: Int) {
        val uri = allUris.getOrNull(position) ?: return
        val rv = binding.viewPager.getChildAt(0) as? RecyclerView ?: return
        val vh = rv.findViewHolderForAdapterPosition(position) as? PagerAdapter.PageVh
        val p = player ?: return
        vh?.binding?.playerView?.player = p

        val spec = VideoEditCache.get(uri) ?: EditedVideoSpec(uri = uri)
        binding.compress480pSwitch.setOnCheckedChangeListener(null)
        binding.compress480pSwitch.isChecked = spec.compressTo480p
        binding.compress480pSwitch.setOnCheckedChangeListener { _, checked ->
            val cur = VideoEditCache.get(uri) ?: EditedVideoSpec(uri = uri)
            VideoEditCache.put(cur.copy(compressTo480p = checked))
        }

        val mediaItemBuilder = MediaItem.Builder().setUri(uri)
        if (spec.trimStartMs > 0 || spec.trimEndMs > 0) {
            val clip = MediaItem.ClippingConfiguration.Builder()
                .setStartPositionMs(spec.trimStartMs.coerceAtLeast(0))
            if (spec.trimEndMs > 0) clip.setEndPositionMs(spec.trimEndMs)
            mediaItemBuilder.setClippingConfiguration(clip.build())
        }
        p.setMediaItem(mediaItemBuilder.build())
        p.prepare()
        p.playWhenReady = true

        // Установим продолжительность в trimmer когда плеер прочитает метаданные
        p.addListener(object : androidx.media3.common.Player.Listener {
            override fun onPlaybackStateChanged(state: Int) {
                if (state == androidx.media3.common.Player.STATE_READY) {
                    val duration = p.duration.coerceAtLeast(0L)
                    binding.trimmer.setVideo(uri, duration)
                    val curSpec = VideoEditCache.get(uri) ?: EditedVideoSpec(uri = uri)
                    if (curSpec.trimEndMs <= 0) {
                        VideoEditCache.put(curSpec.copy(trimEndMs = duration))
                    }
                    p.removeListener(this)
                }
            }
        })
    }

    private fun setupTopBar() {
        binding.closeButton.setOnClickListener { finishWithResult(send = false) }
        binding.checkboxTouchTarget.setOnClickListener { toggleCurrentSelection() }
    }

    private fun setupBottomBar() {
        binding.sendButton.setOnClickListener { finishWithResult(send = true) }
    }

    private fun setupTrimmerControls() {
        binding.trimmer.onRangeChanged = { startMs, endMs ->
            currentUri()?.let { uri ->
                val cur = VideoEditCache.get(uri) ?: EditedVideoSpec(uri = uri)
                VideoEditCache.put(cur.copy(trimStartMs = startMs, trimEndMs = endMs))
            }
        }
        binding.trimmer.onSeekRequested = { timeMs ->
            player?.seekTo(timeMs)
        }
    }

    private fun setupCompressSwitch() {
        // обновляется при page change
    }

    private fun setupBackPress() {
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                finishWithResult(send = false)
            }
        })
    }

    private fun toggleCurrentSelection() {
        val uri = currentUri() ?: return
        if (selectedUris.contains(uri)) {
            if (selectedUris.size <= 1) return
            selectedUris.remove(uri)
        } else {
            selectedUris.add(uri)
        }
        updateTopBar()
    }

    private fun updateTopBar() {
        val uri = currentUri()
        val pos = binding.viewPager.currentItem + 1
        binding.counterText.text = "$pos / ${allUris.size}"

        val idx = uri?.let { selectedUris.indexOf(it) } ?: -1
        if (idx >= 0) {
            binding.selectionIndicator.setBackgroundResource(R.drawable.selection_indicator_selected_background)
            binding.checkIcon.visibility = View.GONE
            binding.selectionNumber.visibility = View.VISIBLE
            binding.selectionNumber.text = (idx + 1).toString()
        } else {
            binding.selectionIndicator.setBackgroundResource(R.drawable.selection_indicator_background)
            binding.checkIcon.visibility = View.GONE
            binding.selectionNumber.visibility = View.GONE
        }
    }

    private fun currentUri(): Uri? = allUris.getOrNull(binding.viewPager.currentItem)

    override fun onResume() {
        super.onResume()
        mainHandler.post(playPositionTicker)
    }

    override fun onPause() {
        super.onPause()
        mainHandler.removeCallbacks(playPositionTicker)
        player?.pause()
    }

    override fun onDestroy() {
        super.onDestroy()
        mainHandler.removeCallbacks(playPositionTicker)
        player?.release()
        player = null
    }

    private fun finishWithResult(send: Boolean) {
        val data = Intent().apply {
            putParcelableArrayListExtra(EXTRA_RESULT_URIS, ArrayList(selectedUris))
            putExtra(EXTRA_RESULT_CAPTION, binding.captionEditText.text.toString())
            putExtra(EXTRA_RESULT_SEND, send)
        }
        setResult(RESULT_OK, data)
        finish()
    }

    private class PagerAdapter(private val uris: List<Uri>) :
        RecyclerView.Adapter<PagerAdapter.PageVh>() {

        class PageVh(val binding: PageVideoEditorBinding) : RecyclerView.ViewHolder(binding.root)

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): PageVh {
            val b = PageVideoEditorBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            return PageVh(b)
        }

        override fun getItemCount(): Int = uris.size
        override fun onBindViewHolder(holder: PageVh, position: Int) {
            // Player привязывается из активити через bindPlayerToPage — здесь nothing.
        }

        override fun onViewRecycled(holder: PageVh) {
            super.onViewRecycled(holder)
            holder.binding.playerView.player = null
        }
    }
}
