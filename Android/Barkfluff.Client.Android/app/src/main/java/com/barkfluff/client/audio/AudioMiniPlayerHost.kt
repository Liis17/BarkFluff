package com.barkfluff.client.audio

import android.app.Activity
import android.app.Application
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import androidx.core.graphics.Insets
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ViewAudioMiniPlayerBinding
import com.barkfluff.client.voice.VoicePlaybackSpeed
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/** Reserves a top row in each activity; existing content receives the remaining insets. */
class AudioMiniPlayerHost(private val playback: AudioPlayback) : Application.ActivityLifecycleCallbacks {
    private val hosts = mutableMapOf<Activity, Host>()

    override fun onActivityPostCreated(activity: Activity, savedInstanceState: Bundle?) {
        val content = activity.findViewById<ViewGroup>(android.R.id.content) ?: return
        if (content.childCount == 0) return
        val column = LinearLayout(activity).apply { orientation = LinearLayout.VERTICAL }
        val binding = ViewAudioMiniPlayerBinding.inflate(activity.layoutInflater, column, false)
        binding.root.visibility = View.GONE
        column.addView(binding.root)
        val body = FrameLayout(activity)
        while (content.childCount > 0) {
            val child = content.getChildAt(0)
            val parameters = child.layoutParams
            content.removeView(child)
            body.addView(child, parameters)
        }
        column.addView(body, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        content.addView(column, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        ViewCompat.setOnApplyWindowInsetsListener(column) { _, insets ->
            if (binding.root.visibility != View.VISIBLE) {
                column.setPadding(0, 0, 0, 0)
                insets
            } else {
                val bars = insets.getInsets(WindowInsetsCompat.Type.statusBars() or WindowInsetsCompat.Type.displayCutout())
                column.setPadding(0, bars.top, 0, 0)
                binding.root.setPadding(bars.left, 0, bars.right, 0)
                WindowInsetsCompat.Builder(insets)
                    .setInsets(WindowInsetsCompat.Type.statusBars(), Insets.of(bars.left, 0, bars.right, 0))
                    .setInsets(WindowInsetsCompat.Type.displayCutout(), Insets.of(bars.left, 0, bars.right, bars.bottom))
                    .build()
            }
        }
        hosts[activity] = Host(activity, column, binding)
        ViewCompat.requestApplyInsets(column)
    }

    override fun onActivityStarted(activity: Activity) { hosts[activity]?.start() }
    override fun onActivityStopped(activity: Activity) { hosts[activity]?.stop() }
    override fun onActivityDestroyed(activity: Activity) { hosts.remove(activity)?.release() }
    override fun onActivityCreated(activity: Activity, savedInstanceState: Bundle?) = Unit
    override fun onActivityResumed(activity: Activity) = Unit
    override fun onActivityPaused(activity: Activity) = Unit
    override fun onActivitySaveInstanceState(activity: Activity, outState: Bundle) = Unit

    private inner class Host(
        private val activity: Activity,
        private val column: View,
        private val binding: ViewAudioMiniPlayerBinding,
    ) {
        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
        private var subscription: Job? = null

        init {
            binding.miniPlay.setOnClickListener {
                if (playback.state.value.isPlaying) playback.pause() else playback.resume()
            }
            binding.miniClose.setOnClickListener { playback.stop() }
            binding.miniSpeed.setOnClickListener { playback.cycleSpeed() }
            binding.miniSource.setOnClickListener {
                val track = playback.state.value.track ?: return@setOnClickListener
                if (track.chatId.isNotBlank() && track.messageId > 0L) {
                    activity.startActivity(ChatActivity.voiceMessageIntent(activity, track.chatId, track.chatTitle, track.messageId,
                        track.isGroupChat, track.otherUserId))
                }
            }

        }

        fun start() {
            subscription?.cancel()
            subscription = scope.launch { playback.state.collect(::render) }
        }

        fun stop() { subscription?.cancel(); subscription = null }
        fun release() { scope.cancel() }

        private fun render(state: PlaybackState) {
            val visibility = if (state.track == null) View.GONE else View.VISIBLE
            if (binding.root.visibility != visibility) {
                binding.root.visibility = visibility
                ViewCompat.requestApplyInsets(column)
            }
            val track = state.track ?: return
            binding.miniSender.text = track.senderName.ifBlank { activity.getString(R.string.voice_playback_title) }
            binding.miniSource.isEnabled = track.chatId.isNotBlank() && track.messageId > 0L
            binding.miniPosition.text = if (state.hasError) activity.getString(R.string.voice_player_error) else
                activity.getString(R.string.audio_position, time(state.positionMillis), time(state.durationMillis))
            binding.miniPlay.setImageResource(if (state.isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow)
            binding.miniPlay.contentDescription = activity.getString(if (state.isPlaying) R.string.cd_pause else R.string.cd_play)
            binding.miniSpeed.visibility = if (track.isVoice) View.VISIBLE else View.GONE
            val speed = VoicePlaybackSpeed.label(activity, state.speed)
            binding.miniSpeed.text = speed
            binding.miniSpeed.contentDescription = activity.getString(R.string.cd_voice_speed, speed)
            binding.miniProgress.progress =
                if (state.durationMillis > 0L) (state.positionMillis * 1000L / state.durationMillis).toInt() else 0
            binding.miniProgress.contentDescription = activity.getString(R.string.cd_voice_seek, time(state.positionMillis), time(state.durationMillis))
        }

        private fun time(ms: Long) = activity.getString(R.string.voice_record_timer_format, ms / 60_000L, ms / 1_000L % 60)
    }
}
