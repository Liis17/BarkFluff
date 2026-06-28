package com.barkfluff.client.calls

import android.content.Context
import android.content.Intent
import com.twilio.audioswitch.AudioDevice
import io.livekit.android.LiveKit
import io.livekit.android.LiveKitOverrides
import io.livekit.android.events.RoomEvent
import io.livekit.android.renderer.SurfaceViewRenderer
import io.livekit.android.room.Room
import io.livekit.android.room.participant.Participant
import io.livekit.android.room.track.LocalVideoTrack
import io.livekit.android.room.track.RemoteTrackPublication
import io.livekit.android.room.track.Track
import io.livekit.android.room.track.VideoQuality
import io.livekit.android.room.track.VideoTrack
import io.livekit.android.room.track.screencapture.ScreenCaptureParams
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import okhttp3.OkHttpClient
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

/**
 * Движок звонка поверх LiveKit. Отдаёт UI-модель участников ([participants]) и набор управляющих
 * команд; renderer'ы движок не хранит — UI создаёт их per-плитка и привязывает к трекам из модели.
 */
class LiveKitCallEngine(
    private val context: Context,
    private val scope: CoroutineScope,
    private val listener: Listener
) {
    interface Listener {
        fun onConnecting()
        fun onConnected(cameraEnabled: Boolean)
        fun onReconnecting()
        fun onDisconnected()
        fun onError(message: String)
    }

    private var room: Room? = null
    private var eventsJob: Job? = null

    private val _participants = MutableStateFlow<List<CallParticipant>>(emptyList())
    val participants: StateFlow<List<CallParticipant>> = _participants.asStateFlow()

    var isConnected: Boolean = false
        private set

    suspend fun connect(
        livekitUrl: String,
        accessToken: String,
        cameraOnStart: Boolean
    ): Result<Unit> = runCatching {
        if (isConnected) return@runCatching

        listener.onConnecting()

        // Сервер использует самоподписанный сертификат — сигнальный WebSocket LiveKit
        // поднимается через свой OkHttp со стандартным доверием Android и падает на TLS-handshake.
        // Передаём OkHttpClient с тем же trust-all, что и gRPC-каналы в GrpcManager.
        val newRoom = LiveKit.create(
            context.applicationContext,
            overrides = LiveKitOverrides(okHttpClient = buildTrustAllOkHttpClient())
        )
        room = newRoom

        eventsJob = scope.launch {
            newRoom.events.events.collect { event ->
                when (event) {
                    is RoomEvent.Reconnecting -> listener.onReconnecting()
                    is RoomEvent.Reconnected -> {
                        rebuildParticipants()
                        listener.onConnected(isLocalCameraEnabled())
                    }
                    is RoomEvent.Disconnected -> {
                        isConnected = false
                        _participants.value = emptyList()
                        listener.onDisconnected()
                    }
                    is RoomEvent.FailedToConnect -> {
                        android.util.Log.e("LiveKitCallEngine", "FailedToConnect", event.error)
                        listener.onError("Не удалось подключиться к звонку")
                    }
                    is RoomEvent.ParticipantConnected,
                    is RoomEvent.ParticipantDisconnected,
                    is RoomEvent.ParticipantNameChanged,
                    is RoomEvent.ActiveSpeakersChanged,
                    is RoomEvent.ConnectionQualityChanged,
                    is RoomEvent.TrackPublished,
                    is RoomEvent.TrackUnpublished,
                    is RoomEvent.TrackSubscribed,
                    is RoomEvent.TrackUnsubscribed,
                    is RoomEvent.LocalTrackSubscribed,
                    is RoomEvent.TrackMuted,
                    is RoomEvent.TrackUnmuted -> rebuildParticipants()
                    else -> Unit
                }
            }
        }

        newRoom.connect(livekitUrl, accessToken)
        isConnected = true
        newRoom.localParticipant.setMicrophoneEnabled(true)
        if (cameraOnStart) {
            newRoom.localParticipant.setCameraEnabled(true)
        }
        rebuildParticipants()
        listener.onConnected(cameraOnStart)
    }

    /** Инициализирует renderer общим EGL-контекстом Room (обязательно перед привязкой трека). */
    fun initRenderer(view: SurfaceViewRenderer) {
        room?.initVideoRenderer(view)
    }

    suspend fun setMicrophoneEnabled(enabled: Boolean): Result<Unit> = runCatching {
        requireRoom().localParticipant.setMicrophoneEnabled(enabled)
        rebuildParticipants()
    }

    suspend fun setCameraEnabled(enabled: Boolean): Result<Unit> = runCatching {
        requireRoom().localParticipant.setCameraEnabled(enabled)
        rebuildParticipants()
    }

    suspend fun setScreenShareEnabled(enabled: Boolean, data: Intent? = null): Result<Unit> = runCatching {
        val currentRoom = requireRoom()
        if (enabled) {
            val resultData = requireNotNull(data) { "Screen capture data is required" }
            currentRoom.localParticipant.setScreenShareEnabled(true, ScreenCaptureParams(resultData))
        } else {
            currentRoom.localParticipant.setScreenShareEnabled(false)
        }
        rebuildParticipants()
    }

    /** Переключает фронтальную/тыльную камеру (если активна камера и доступно >1 устройства). */
    fun flipCamera(): Result<Unit> = runCatching {
        val track = requireRoom().localParticipant.getTrackPublication(Track.Source.CAMERA)?.track as? LocalVideoTrack
            ?: error("Камера не активна")
        track.switchCamera()
    }

    fun isLocalScreenShareEnabled(): Boolean =
        room?.localParticipant?.getTrackPublication(Track.Source.SCREEN_SHARE)?.track != null

    fun availableAudioDevices(): List<AudioDevice> =
        room?.audioSwitchHandler?.availableAudioDevices ?: emptyList()

    fun selectedAudioDevice(): AudioDevice? = room?.audioSwitchHandler?.selectedAudioDevice

    fun selectAudioDevice(device: AudioDevice) {
        room?.audioSwitchHandler?.selectDevice(device)
    }

    /** Меняет качество подписки на видео указанного удалённого участника. */
    fun setRemoteVideoQuality(identity: String, quality: VideoQuality) {
        val currentRoom = room ?: return
        val participant = currentRoom.remoteParticipants.values
            .firstOrNull { it.identity?.value == identity } ?: return
        (participant.getTrackPublication(Track.Source.CAMERA) as? RemoteTrackPublication)
            ?.setVideoQuality(quality)
    }

    fun disconnect() {
        eventsJob?.cancel()
        eventsJob = null
        _participants.value = emptyList()
        runCatching { room?.disconnect() }
        runCatching { room?.release() }
        room = null
        isConnected = false
    }

    private fun isLocalCameraEnabled(): Boolean =
        room?.localParticipant?.getTrackPublication(Track.Source.CAMERA)?.let {
            it.track != null && !it.muted
        } ?: false

    private fun rebuildParticipants() {
        val currentRoom = room ?: run {
            _participants.value = emptyList()
            return
        }
        _participants.value = buildList {
            add(toModel(currentRoom.localParticipant, isLocal = true))
            currentRoom.remoteParticipants.values.forEach { add(toModel(it, isLocal = false)) }
        }
    }

    private fun toModel(participant: Participant, isLocal: Boolean): CallParticipant {
        val cameraPub = participant.getTrackPublication(Track.Source.CAMERA)
        val screenPub = participant.getTrackPublication(Track.Source.SCREEN_SHARE)
        val micPub = participant.getTrackPublication(Track.Source.MICROPHONE)
        val identity = participant.identity?.value ?: participant.sid.value
        return CallParticipant(
            identity = identity,
            name = participant.name?.takeIf { it.isNotBlank() } ?: identity,
            isLocal = isLocal,
            cameraTrack = cameraPub?.track as? VideoTrack,
            screenTrack = screenPub?.track as? VideoTrack,
            micEnabled = micPub?.let { !it.muted } ?: false,
            cameraEnabled = cameraPub?.let { it.track != null && !it.muted } ?: false,
            isSpeaking = participant.isSpeaking,
            connectionQuality = participant.connectionQuality
        )
    }

    private fun requireRoom(): Room = room ?: error("LiveKit room is not connected")

    private fun buildTrustAllOkHttpClient(): OkHttpClient {
        val trustManager = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
            override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
        }
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
        return OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier { _, _ -> true }
            .build()
    }
}
