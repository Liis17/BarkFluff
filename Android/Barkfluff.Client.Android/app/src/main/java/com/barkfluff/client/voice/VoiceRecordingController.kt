package com.barkfluff.client.voice

import java.io.File
import kotlin.math.abs
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

enum class VoiceRecordingMode { IDLE, HOLDING, LOCKED, PAUSED }

data class VoiceRecordingState(
    val mode: VoiceRecordingMode = VoiceRecordingMode.IDLE,
    val durationMillis: Long = 0L,
    val cancelPending: Boolean = false,
) {
    val isActive: Boolean get() = mode != VoiceRecordingMode.IDLE
}

data class RecordedVoice(val file: File, val durationMillis: Long)

interface VoiceRecordingDevice {
    fun start()
    fun pause()
    fun resume()
    fun stop(): File
    fun cancel()
}

/** Owns gestures and active recording time; a completed clip is handed to the composer. */
class VoiceRecordingController(
    private val device: VoiceRecordingDevice,
    private val nowMillis: () -> Long,
) {
    private val mutableState = MutableStateFlow(VoiceRecordingState())
    val state: StateFlow<VoiceRecordingState> = mutableState.asStateFlow()
    private var activeSince = 0L
    private var recordedMillis = 0L

    fun start(locked: Boolean = false) {
        if (state.value.isActive) return
        device.start()
        recordedMillis = 0L
        activeSince = nowMillis()
        mutableState.value = VoiceRecordingState(
            mode = if (locked) VoiceRecordingMode.LOCKED else VoiceRecordingMode.HOLDING,
        )
    }

    fun drag(dxDp: Float, dyDp: Float) {
        if (state.value.mode != VoiceRecordingMode.HOLDING) return
        if (dyDp <= -GESTURE_THRESHOLD_DP && abs(dyDp) > abs(dxDp)) {
            mutableState.value = state.value.copy(mode = VoiceRecordingMode.LOCKED, cancelPending = false)
        } else {
            mutableState.value = state.value.copy(
                cancelPending = dxDp <= -GESTURE_THRESHOLD_DP && abs(dxDp) >= abs(dyDp),
            )
        }
    }

    fun release(): RecordedVoice? {
        if (state.value.mode != VoiceRecordingMode.HOLDING) return null
        if (state.value.cancelPending) {
            cancel()
            return null
        }
        return stop()
    }

    fun pause() {
        if (state.value.mode != VoiceRecordingMode.LOCKED) return
        device.pause()
        recordedMillis += nowMillis() - activeSince
        mutableState.value = state.value.copy(mode = VoiceRecordingMode.PAUSED, durationMillis = recordedMillis)
    }

    fun resume() {
        if (state.value.mode != VoiceRecordingMode.PAUSED) return
        device.resume()
        activeSince = nowMillis()
        mutableState.value = state.value.copy(mode = VoiceRecordingMode.LOCKED)
    }

    fun refreshDuration() {
        if (state.value.isActive) mutableState.value = state.value.copy(durationMillis = durationMillis())
    }

    fun stop(): RecordedVoice? {
        if (!state.value.isActive) return null
        val duration = durationMillis()
        try {
            val file = device.stop()
            if (duration < MIN_DURATION_MILLIS || !file.isFile || file.length() == 0L) {
                file.delete()
                return null
            }
            return RecordedVoice(file, duration)
        } finally {
            mutableState.value = VoiceRecordingState()
        }
    }

    fun cancel() {
        if (!state.value.isActive) return
        try {
            device.cancel()
        } finally {
            mutableState.value = VoiceRecordingState()
        }
    }

    private fun durationMillis(): Long = recordedMillis +
        if (state.value.mode == VoiceRecordingMode.PAUSED) 0L else (nowMillis() - activeSince).coerceAtLeast(0L)

    companion object {
        const val GESTURE_THRESHOLD_DP = 72f
        const val MIN_DURATION_MILLIS = 500L
    }
}
