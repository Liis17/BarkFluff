package com.barkfluff.client.calls

import io.livekit.android.room.participant.ConnectionQuality
import io.livekit.android.room.track.VideoTrack

/**
 * UI-модель участника звонка. Движок ([LiveKitCallEngine]) пересобирает список из состояния
 * LiveKit Room; UI сам привязывает renderer к [cameraTrack] / [screenTrack] каждой плитки.
 */
data class CallParticipant(
    val identity: String,
    val name: String,
    val isLocal: Boolean,
    val cameraTrack: VideoTrack?,
    val screenTrack: VideoTrack?,
    val micEnabled: Boolean,
    val cameraEnabled: Boolean,
    val isSpeaking: Boolean,
    val connectionQuality: ConnectionQuality
)
