package com.barkfluff.client.audio

import android.app.PendingIntent
import android.content.Intent
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.Player
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.R

@dagger.hilt.android.AndroidEntryPoint
class VoicePlaybackService : MediaSessionService() {
    @javax.inject.Inject lateinit var playback: AudioPlayback
    private var session: MediaSession? = null

    override fun onCreate() {
        super.onCreate()
        val player = ExoPlayer.Builder(this)
            .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_SPEECH).build(), true)
            .setHandleAudioBecomingNoisy(true)
            .build()
        player.addListener(object : Player.Listener {
            override fun onPlayWhenReadyChanged(playWhenReady: Boolean, reason: Int) {
                if (playWhenReady && !playback.playbackAllowed) player.pause()
            }
            override fun onPlaybackSuppressionReasonChanged(playbackSuppressionReason: Int) {
                // Transient focus loss must not unexpectedly restart spoken messages.
                if (playbackSuppressionReason != Player.PLAYBACK_SUPPRESSION_REASON_NONE) player.pause()
            }
            override fun onMediaItemTransition(mediaItem: androidx.media3.common.MediaItem?, reason: Int) {
                val extras = mediaItem?.mediaMetadata?.extras ?: return
                val intent = ChatActivity.voiceMessageIntent(this@VoicePlaybackService,
                    extras.getString("chat_id").orEmpty(), extras.getString("chat_title").orEmpty(), extras.getLong("message_id"),
                    extras.getBoolean("is_group_chat"), extras.getLong("other_user_id"))
                session?.setSessionActivity(PendingIntent.getActivity(this@VoicePlaybackService, 0, intent,
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
            }
        })
        session = MediaSession.Builder(this, player).build()
        setMediaNotificationProvider(androidx.media3.session.DefaultMediaNotificationProvider.Builder(this)
            .setChannelId("voice_playback")
            .setChannelName(R.string.voice_playback_channel)
            .build())
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? =
        session.takeIf { controllerInfo.packageName == packageName || controllerInfo.isTrusted }

    override fun onDestroy() {
        session?.run { player.release(); release() }
        session = null
        super.onDestroy()
    }
}
