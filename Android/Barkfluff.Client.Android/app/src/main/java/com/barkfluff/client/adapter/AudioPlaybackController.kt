package com.barkfluff.client.adapter

import com.barkfluff.client.audio.AudioPlayback
import com.barkfluff.client.audio.AudioTrack
import java.io.File

/** View-independent audio playback and waveform cache. */
class AudioPlaybackController(private val playback: AudioPlayback) {
    private val waveforms = mutableMapOf<String, FloatArray>()

    fun waveform(fileId: String): FloatArray? = waveforms[fileId]
    fun cacheWaveform(fileId: String, waveform: FloatArray) { waveforms[fileId] = waveform }
    fun remove(fileId: String) {
        waveforms.remove(fileId)
    }

    fun isActiveFile(fileId: String): Boolean = playback.state.value.track?.fileId == fileId
    fun isPlaying(): Boolean = playback.state.value.isPlaying
    fun play(track: AudioTrack, file: File) = playback.play(track, file)
    fun pause() = playback.pause()
    fun resume() = playback.resume()
    fun stop() = playback.stop()
    fun seekTo(positionMs: Int) = playback.seekTo(positionMs.toLong())
    fun currentPosition(): Int = playback.state.value.positionMillis.toInt()
    fun duration(): Int = playback.state.value.durationMillis.toInt()
}
