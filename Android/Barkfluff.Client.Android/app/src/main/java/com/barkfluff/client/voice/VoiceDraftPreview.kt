package com.barkfluff.client.voice

import android.net.Uri
import androidx.lifecycle.LifecycleCoroutineScope
import androidx.media3.common.MediaItem
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.PlaybackException
import androidx.media3.common.PlaybackParameters
import androidx.media3.common.Player
import androidx.media3.exoplayer.ExoPlayer
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ViewVoiceDraftBinding
import com.barkfluff.client.audio.AudioPlayback
import com.barkfluff.client.utils.AudioWaveformExtractor
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Local-only preview. Its owner explicitly pauses/releases it with the chat lifecycle. */
class VoiceDraftPreview(
    private val binding: ViewVoiceDraftBinding,
    private val scope: LifecycleCoroutineScope,
    private val playback: AudioPlayback,
    private val onSend: () -> Unit,
    private val onDiscard: () -> Unit,
) {
    private val context = binding.root.context
    private val player = ExoPlayer.Builder(context)
        .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_SPEECH).build(), true)
        .setHandleAudioBecomingNoisy(true).build()
    private var path: String? = null
    private var progressJob: Job? = null
    private var waveformJob: Job? = null

    init {
        binding.voicePreviewPlay.setOnClickListener {
            if (player.isPlaying) player.pause() else if (playback.playbackAllowed) {
                playback.pause()
                if (player.playbackState == Player.STATE_ENDED) player.seekTo(0L)
                player.play()
                resumeUpdates()
            }
            render()
        }
        binding.voicePreviewSend.setOnClickListener { pause(); onSend() }
        binding.voicePreviewDelete.setOnClickListener { pause(); onDiscard() }
        binding.voicePreviewSpeed.setOnClickListener {
            player.playbackParameters = PlaybackParameters(VoicePlaybackSpeed.cycle(context), 1f)
            render()
        }
        binding.voicePreviewWaveform.onSeekRequested = { fraction ->
            player.seekTo((fraction * player.duration.coerceAtLeast(0L)).toLong())
            render()
        }
        scope.launch {
            playback.state.collect { state ->
                if (state.isPlaying) pause()
                render()
            }
        }
        player.addListener(object : Player.Listener {
            override fun onPlaybackSuppressionReasonChanged(playbackSuppressionReason: Int) {
                if (playbackSuppressionReason != Player.PLAYBACK_SUPPRESSION_REASON_NONE) pause()
            }
            override fun onEvents(player: Player, events: Player.Events) { render() }
            override fun onPlayerError(error: PlaybackException) {
                binding.voicePreviewStatus.setText(R.string.voice_preview_play_failed)
            }
        })
    }

    fun bind(filePath: String?, busy: Boolean) {
        binding.root.visibility = if (filePath == null) android.view.View.GONE else android.view.View.VISIBLE
        binding.voicePreviewPlay.isEnabled = !busy
        binding.voicePreviewSend.isEnabled = !busy
        binding.voicePreviewDelete.isEnabled = !busy
        binding.voicePreviewStatus.setText(if (busy) R.string.voice_draft_saving else R.string.voice_draft_title)
        if (filePath == path) return
        path = filePath
        waveformJob?.cancel()
        player.stop()
        player.clearMediaItems()
        binding.voicePreviewWaveform.resetAmplitudes()
        if (filePath == null) return
        player.playbackParameters = PlaybackParameters(VoicePlaybackSpeed.read(context), 1f)
        player.setMediaItem(MediaItem.fromUri(Uri.fromFile(File(filePath))))
        player.prepare()
        waveformJob = scope.launch {
            val waveform = withContext(Dispatchers.IO) { AudioWaveformExtractor.extract(File(filePath)) }
            if (path == filePath) binding.voicePreviewWaveform.setAmplitudes(waveform)
        }
        render()
    }

    fun resumeUpdates() {
        progressJob?.cancel()
        progressJob = scope.launch {
            while (isActive) { render(); delay(250L) }
        }
    }

    fun pause() { player.pause(); progressJob?.cancel(); render() }
    fun release() { progressJob?.cancel(); waveformJob?.cancel(); player.release() }

    private fun render() {
        val speedPreference = VoicePlaybackSpeed.read(context)
        if (player.playbackParameters.speed != speedPreference) player.playbackParameters = PlaybackParameters(speedPreference, 1f)
        val position = player.currentPosition.coerceAtLeast(0L)
        val duration = player.duration.coerceAtLeast(0L)
        binding.voicePreviewPlay.setImageResource(
            if (player.isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow,
        )
        binding.voicePreviewPlay.contentDescription = context.getString(if (player.isPlaying) R.string.cd_pause else R.string.cd_play)
        binding.voicePreviewDuration.text = context.getString(R.string.audio_position, time(position), time(duration))
        binding.voicePreviewWaveform.setProgress(if (duration > 0L) position.toFloat() / duration else 0f)
        binding.voicePreviewWaveform.contentDescription = context.getString(R.string.cd_voice_seek, time(position), time(duration))
        val speed = VoicePlaybackSpeed.label(context, player.playbackParameters.speed)
        binding.voicePreviewSpeed.text = speed
        binding.voicePreviewSpeed.contentDescription = context.getString(R.string.cd_voice_speed, speed)
    }

    private fun time(millis: Long) = context.getString(R.string.voice_record_timer_format, millis / 60_000L, millis / 1_000L % 60)
}
