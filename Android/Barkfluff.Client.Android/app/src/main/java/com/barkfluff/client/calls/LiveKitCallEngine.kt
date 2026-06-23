package com.barkfluff.client.calls

import android.content.Context
import android.content.Intent
import io.livekit.android.LiveKit
import io.livekit.android.events.RoomEvent
import io.livekit.android.renderer.SurfaceViewRenderer
import io.livekit.android.room.Room
import io.livekit.android.room.track.VideoTrack
import io.livekit.android.room.track.screencapture.ScreenCaptureParams
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.launch

class LiveKitCallEngine(
    private val context: Context,
    private val scope: CoroutineScope,
    private val listener: Listener
) {
    interface Listener {
        fun onConnecting()
        fun onConnected(cameraEnabled: Boolean)
        fun onRemoteVideoAttached()
        fun onRemoteVideoDetached()
        fun onLocalPreviewChanged(visible: Boolean)
        fun onReconnecting()
        fun onDisconnected()
        fun onScreenShareChanged(enabled: Boolean)
        fun onError(message: String)
    }

    private var room: Room? = null
    private var eventsJob: Job? = null
    private var remoteRenderer: SurfaceViewRenderer? = null
    private var localRenderer: SurfaceViewRenderer? = null
    private var remoteVideoTrack: VideoTrack? = null
    private var localVideoTrack: VideoTrack? = null

    var isConnected: Boolean = false
        private set

    suspend fun connect(
        livekitUrl: String,
        accessToken: String,
        remoteRenderer: SurfaceViewRenderer,
        localRenderer: SurfaceViewRenderer,
        cameraOnStart: Boolean
    ): Result<Unit> = runCatching {
        if (isConnected) return@runCatching

        listener.onConnecting()
        this.remoteRenderer = remoteRenderer
        this.localRenderer = localRenderer

        val newRoom = LiveKit.create(context.applicationContext)
        room = newRoom
        newRoom.initVideoRenderer(remoteRenderer)
        newRoom.initVideoRenderer(localRenderer)

        eventsJob = scope.launch {
            newRoom.events.events.collect { event ->
                when (event) {
                    is RoomEvent.TrackSubscribed -> attachRemoteVideo(event.track)
                    is RoomEvent.TrackUnsubscribed -> detachRemoteVideo(event.track)
                    is RoomEvent.Reconnecting -> listener.onReconnecting()
                    is RoomEvent.Reconnected -> listener.onConnected(localVideoTrack != null)
                    is RoomEvent.Disconnected -> {
                        isConnected = false
                        listener.onDisconnected()
                    }
                    is RoomEvent.FailedToConnect -> listener.onError("Не удалось подключиться к звонку")
                    else -> Unit
                }
            }
        }

        newRoom.connect(livekitUrl, accessToken)
        isConnected = true
        newRoom.localParticipant.setMicrophoneEnabled(true)
        if (cameraOnStart) {
            setCameraEnabled(true).getOrThrow()
        }
        listener.onConnected(cameraOnStart)
    }

    suspend fun setMicrophoneEnabled(enabled: Boolean): Result<Unit> = runCatching {
        val currentRoom = requireRoom()
        currentRoom.localParticipant.setMicrophoneEnabled(enabled)
    }

    suspend fun setCameraEnabled(enabled: Boolean): Result<Unit> = runCatching {
        val currentRoom = requireRoom()
        currentRoom.localParticipant.setCameraEnabled(enabled)
        val renderer = localRenderer
        if (enabled && renderer != null) {
            val track = currentRoom.localParticipant.getOrCreateDefaultVideoTrack()
            localVideoTrack = track
            track.addRenderer(renderer)
            listener.onLocalPreviewChanged(true)
        } else {
            val track = localVideoTrack
            if (track != null && renderer != null) {
                track.removeRenderer(renderer)
            }
            localVideoTrack = null
            listener.onLocalPreviewChanged(false)
        }
    }

    suspend fun setScreenShareEnabled(enabled: Boolean, data: Intent? = null): Result<Unit> = runCatching {
        val currentRoom = requireRoom()
        if (enabled) {
            val resultData = requireNotNull(data) { "Screen capture data is required" }
            currentRoom.localParticipant.setScreenShareEnabled(true, ScreenCaptureParams(resultData))
        } else {
            currentRoom.localParticipant.setScreenShareEnabled(false)
        }
        listener.onScreenShareChanged(enabled)
    }

    fun disconnect() {
        eventsJob?.cancel()
        eventsJob = null
        val renderer = remoteRenderer
        val track = remoteVideoTrack
        if (track != null && renderer != null) {
            runCatching { track.removeRenderer(renderer) }
        }
        val local = localVideoTrack
        val localView = localRenderer
        if (local != null && localView != null) {
            runCatching { local.removeRenderer(localView) }
        }
        remoteVideoTrack = null
        localVideoTrack = null
        runCatching { room?.disconnect() }
        runCatching { room?.release() }
        room = null
        isConnected = false
    }

    private fun attachRemoteVideo(track: io.livekit.android.room.track.Track) {
        if (track !is VideoTrack) return
        val renderer = remoteRenderer ?: return
        remoteVideoTrack?.removeRenderer(renderer)
        remoteVideoTrack = track
        track.addRenderer(renderer)
        listener.onRemoteVideoAttached()
    }

    private fun detachRemoteVideo(track: io.livekit.android.room.track.Track) {
        if (track != remoteVideoTrack) return
        val renderer = remoteRenderer ?: return
        remoteVideoTrack?.removeRenderer(renderer)
        remoteVideoTrack = null
        listener.onRemoteVideoDetached()
    }

    private fun requireRoom(): Room = room ?: error("LiveKit room is not connected")
}
