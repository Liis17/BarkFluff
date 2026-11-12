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

/**
 * Показывает мини-плеер в каждой activity.
 *
 * - Если в разметке activity есть [R.id.audioMiniPlayerSlot] (ChatActivity), плеер живёт там — под шапкой.
 * - Иначе резервируется строка сверху, а остальной контент получает оставшиеся insets.
 * - Фрагмент может подключить собственное место через [attachSlot] (список чатов — под поиском);
 *   пока оно активно, плеер показывается в нём, а верхняя строка скрыта.
 */
class AudioMiniPlayerHost(private val playback: AudioPlayback) : Application.ActivityLifecycleCallbacks {
    private val hosts = mutableMapOf<Activity, Host>()

    fun attachSlot(activity: Activity, slot: ViewGroup) { hosts[activity]?.attachSlot(slot) }
    fun detachSlot(activity: Activity, slot: ViewGroup) { hosts[activity]?.detachSlot(slot) }
    /** Фрагмент со слотом скрыт/показан (переключение вкладок через hide/show). */
    fun setSlotActive(activity: Activity, slot: ViewGroup, active: Boolean) { hosts[activity]?.setSlotActive(slot, active) }

    override fun onActivityPostCreated(activity: Activity, savedInstanceState: Bundle?) {
        val layoutSlot = activity.findViewById<ViewGroup>(R.id.audioMiniPlayerSlot)
        if (layoutSlot != null) {
            hosts[activity] = Host(activity, top = null).also { it.attachSlot(layoutSlot) }
            return
        }
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
                insets
            } else {
                // Фон плеера заходит под status bar, чтобы над ним не оставалась полоса цвета окна.
                val bars = insets.getInsets(WindowInsetsCompat.Type.statusBars() or WindowInsetsCompat.Type.displayCutout())
                binding.root.setPadding(bars.left, bars.top, bars.right, 0)
                WindowInsetsCompat.Builder(insets)
                    .setInsets(WindowInsetsCompat.Type.statusBars(), Insets.of(bars.left, 0, bars.right, 0))
                    .setInsets(WindowInsetsCompat.Type.displayCutout(), Insets.of(bars.left, 0, bars.right, bars.bottom))
                    .build()
            }
        }
        hosts[activity] = Host(activity, Panel(binding.root, binding, column))
        ViewCompat.requestApplyInsets(column)
    }

    override fun onActivityStarted(activity: Activity) { hosts[activity]?.start() }
    override fun onActivityStopped(activity: Activity) { hosts[activity]?.stop() }
    override fun onActivityDestroyed(activity: Activity) { hosts.remove(activity)?.release() }
    override fun onActivityCreated(activity: Activity, savedInstanceState: Bundle?) = Unit
    override fun onActivityResumed(activity: Activity) = Unit
    override fun onActivityPaused(activity: Activity) = Unit
    override fun onActivitySaveInstanceState(activity: Activity, outState: Bundle) = Unit

    /**
     * Одно место показа плеера: [container] скрывается/показывается вместе с треком,
     * [insetsTarget] — обёртка верхней строки, которой нужно заново раздать insets.
     */
    private class Panel(
        val container: View,
        val binding: ViewAudioMiniPlayerBinding,
        val insetsTarget: View? = null,
        var active: Boolean = true,
    )

    private inner class Host(private val activity: Activity, private val top: Panel?) {
        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
        private var subscription: Job? = null
        private var slot: Panel? = null

        init { top?.let { wire(it.binding) } }

        fun attachSlot(container: ViewGroup) {
            if (slot?.container === container) return
            slot?.let { detachSlot(it.container as ViewGroup) }
            container.removeAllViews()
            val binding = ViewAudioMiniPlayerBinding.inflate(activity.layoutInflater, container, true)
            binding.root.background = null
            container.visibility = View.GONE
            wire(binding)
            slot = Panel(container, binding)
            render(playback.state.value)
        }

        fun detachSlot(container: ViewGroup) {
            if (slot?.container !== container) return
            container.removeAllViews()
            container.visibility = View.GONE
            slot = null
            render(playback.state.value)
        }

        fun setSlotActive(container: ViewGroup, active: Boolean) {
            val current = slot?.takeIf { it.container === container } ?: return
            if (current.active == active) return
            current.active = active
            render(playback.state.value)
        }

        fun start() {
            subscription?.cancel()
            subscription = scope.launch { playback.state.collect(::render) }
        }

        fun stop() { subscription?.cancel(); subscription = null }
        fun release() { scope.cancel() }

        private fun wire(binding: ViewAudioMiniPlayerBinding) {
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

        private fun render(state: PlaybackState) {
            val target = slot?.takeIf { it.active } ?: top
            for (panel in listOfNotNull(top, slot)) {
                val visibility = if (state.track != null && panel === target) View.VISIBLE else View.GONE
                if (panel.container.visibility != visibility) {
                    panel.container.visibility = visibility
                    panel.insetsTarget?.let(ViewCompat::requestApplyInsets)
                }
            }
            val track = state.track ?: return
            val binding = target?.binding ?: return
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
