package com.barkfluff.client.audio

import android.content.ComponentName
import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import android.os.Bundle
import androidx.core.content.ContextCompat
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackParameters
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import com.barkfluff.client.R
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.voice.VoicePlaybackSpeed
import com.google.common.util.concurrent.ListenableFuture
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

data class AudioTrack(
    val fileId: String,
    val chatId: String = "",
    val chatTitle: String = "",
    val senderName: String = "",
    val messageId: Long = 0L,
    val isVoice: Boolean = true,
    val isGroupChat: Boolean = false,
    val otherUserId: Long = 0L,
)

data class PlaybackState(
    val track: AudioTrack? = null,
    val isPlaying: Boolean = false,
    val positionMillis: Long = 0L,
    val durationMillis: Long = 0L,
    val speed: Float = 1f,
    val hasError: Boolean = false,
)

/** One controller shared by chat, profile and the app player; the service owns the actual player. */
@Singleton
class AudioPlayback @Inject constructor(
    @ApplicationContext private val context: Context,
    private val calls: CallEventsService,
    cache: ChatCacheRepository,
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val mutableState = MutableStateFlow(PlaybackState(speed = VoicePlaybackSpeed.read(context)))
    val state: StateFlow<PlaybackState> = mutableState.asStateFlow()
    private var connection: ListenableFuture<MediaController>? = null
    private var controller: MediaController? = null
    private var progressJob: Job? = null
    private var playRequest = 0L
    private var selection: Pair<AudioTrack, File>? = null
    private var recordingActive = false
    val playbackAllowed: Boolean get() = !recordingActive && calls.currentCall.value?.isTerminal != false

    fun setRecordingActive(active: Boolean) {
        if (recordingActive == active) return
        recordingActive = active
        if (active) pause()
    }

    private val speedPreferences = context.getSharedPreferences("voice_playback", Context.MODE_PRIVATE)
    private val speedListener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
        if (key == "speed") scope.launch {
            controller?.let(::applySpeed)
            if (controller == null) mutableState.value = mutableState.value.copy(speed = VoicePlaybackSpeed.read(context)) else snapshot()
        }
    }

    private val accountPreferences = GlobalParam(context).sharedPreferences
    private var accountScope = CacheScope.from(GlobalParam(context))
    private val accountListener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ ->
        scope.launch {
            val currentScope = CacheScope.from(GlobalParam(context))
            if (currentScope != accountScope) { accountScope = currentScope; stop() }
        }
    }

    init {
        accountPreferences.registerOnSharedPreferenceChangeListener(accountListener)
        speedPreferences.registerOnSharedPreferenceChangeListener(speedListener)
        scope.launch { calls.currentCall.collect { if (it != null && !it.isTerminal) pause() } }
        scope.launch { cache.clearedEvents.collect { stop() } }
    }

    fun play(track: AudioTrack, file: File) {
        if (!playbackAllowed) return
        if (!file.isFile) {
            stop()
            mutableState.value = PlaybackState(track = track, speed = VoicePlaybackSpeed.read(context), hasError = true)
            return
        }
        selection = track to file
        mutableState.value = PlaybackState(track = track, speed = VoicePlaybackSpeed.read(context))
        val request = ++playRequest
        withController { player ->
            if (request != playRequest) return@withController
            val extras = Bundle().apply {
                putString("chat_id", track.chatId)
                putString("chat_title", track.chatTitle)
                putString("sender_name", track.senderName)
                putLong("message_id", track.messageId)
                putBoolean("is_voice", track.isVoice)
                putBoolean("is_group_chat", track.isGroupChat)
                putLong("other_user_id", track.otherUserId)
            }
            val title = track.senderName.ifBlank {
                context.getString(if (track.isVoice) R.string.voice_playback_title else R.string.attachment_file)
            }
            player.setMediaItem(MediaItem.Builder()
                .setMediaId(track.fileId)
                .setUri(Uri.fromFile(file))
                .setMediaMetadata(MediaMetadata.Builder().setTitle(title).setArtist(track.chatTitle).setExtras(extras).build())
                .build())
            applySpeed(player)
            player.prepare()
            player.play()
        }
    }

    fun pause() {
        playRequest++
        controller?.pause()
        snapshot()
    }

    fun resume() {
        if (!playbackAllowed) return
        val player = controller
        if (player == null || player.currentMediaItem == null) {
            selection?.let { (track, file) -> play(track, file) }
            return
        }
        if (player.playerError != null) player.prepare()
        if (player.playbackState == Player.STATE_ENDED) player.seekTo(0L)
        applySpeed(player)
        player.play()
    }

    fun seekTo(positionMillis: Long) {
        controller?.let { it.seekTo(positionMillis.coerceIn(0L, it.duration.coerceAtLeast(0L))) }
        snapshot()
    }

    fun cycleSpeed() {
        VoicePlaybackSpeed.cycle(context)
        controller?.let(::applySpeed)
        snapshot()
    }

    fun stop() {
        selection = null
        playRequest++
        controller?.run { stop(); clearMediaItems() }
        progressJob?.cancel()
        mutableState.value = PlaybackState(speed = VoicePlaybackSpeed.read(context))
    }

    private fun withController(action: (MediaController) -> Unit) {
        controller?.let { action(it); return }
        val future = connection ?: MediaController.Builder(context,
            SessionToken(context, ComponentName(context, VoicePlaybackService::class.java)),
        ).setListener(object : MediaController.Listener {
            override fun onDisconnected(disconnectedController: MediaController) {
                if (controller === disconnectedController) {
                    controller = null
                    connection = null
                    progressJob?.cancel()
                    mutableState.value = PlaybackState(speed = VoicePlaybackSpeed.read(context))
                }
            }
        }).buildAsync().also { connection = it }
        future.addListener({
            runCatching { future.get() }.onSuccess { player ->
                if (controller !== player) {
                    controller = player
                    player.addListener(object : Player.Listener {
                        override fun onEvents(player: Player, events: Player.Events) {
                            snapshot()
                            progressJob?.cancel()
                            if (player.isPlaying) progressJob = scope.launch {
                                while (isActive && player.isPlaying) { snapshot(); delay(250L) }
                            }
                        }
                    })
                }
                action(player)
                snapshot()
            }.onFailure {
                connection = null
                mutableState.value = mutableState.value.copy(isPlaying = false, hasError = true)
            }
        }, ContextCompat.getMainExecutor(context))
    }

    private fun applySpeed(player: MediaController) {
        val voice = player.currentMediaItem?.mediaMetadata?.extras?.getBoolean("is_voice") == true
        player.playbackParameters = PlaybackParameters(if (voice) VoicePlaybackSpeed.read(context) else 1f, 1f)
    }

    private fun snapshot() {
        val player = controller ?: return
        val item = player.currentMediaItem
        val extras = item?.mediaMetadata?.extras
        mutableState.value = PlaybackState(
            track = item?.let { AudioTrack(it.mediaId,
                extras?.getString("chat_id").orEmpty(), extras?.getString("chat_title").orEmpty(),
                extras?.getString("sender_name").orEmpty(), extras?.getLong("message_id") ?: 0L,
                extras?.getBoolean("is_voice") ?: false,
                extras?.getBoolean("is_group_chat") ?: false,
                extras?.getLong("other_user_id") ?: 0L,
            ) },
            isPlaying = player.isPlaying,
            positionMillis = player.currentPosition.coerceAtLeast(0L),
            durationMillis = player.duration.coerceAtLeast(0L),
            speed = VoicePlaybackSpeed.read(context),
            hasError = player.playerError != null,
        )
    }
}
