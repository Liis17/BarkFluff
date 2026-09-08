package com.barkfluff.client.adapter

import com.barkfluff.client.utils.AudioCallbacks
import com.barkfluff.client.utils.AudioPlayerHelper
import java.io.File

/** View-independent ownership of audio auto-download and waveform caches. */
class AudioPlaybackController {
    private val autoDownloads = mutableSetOf<String>()
    private val waveforms = mutableMapOf<String, FloatArray>()

    fun claimAutoDownload(fileId: String): Boolean = autoDownloads.add(fileId)
    fun releaseAutoDownload(fileId: String) { autoDownloads.remove(fileId) }
    fun waveform(fileId: String): FloatArray? = waveforms[fileId]
    fun cacheWaveform(fileId: String, waveform: FloatArray) { waveforms[fileId] = waveform }
    fun remove(fileId: String) {
        autoDownloads.remove(fileId)
        waveforms.remove(fileId)
    }

    fun isActiveFile(fileId: String): Boolean = AudioPlayerHelper.isActiveFile(fileId)
    fun isPlaying(): Boolean = AudioPlayerHelper.isPlaying()
    fun play(fileId: String, file: File, callbacks: AudioCallbacks) =
        AudioPlayerHelper.play(fileId, file, callbacks)
    fun pause() = AudioPlayerHelper.pause()
    fun resume() = AudioPlayerHelper.resume()
    fun stop() = AudioPlayerHelper.stop()
    fun seekTo(positionMs: Int) = AudioPlayerHelper.seekTo(positionMs)
    fun currentPosition(): Int = AudioPlayerHelper.getCurrentPosition()
    fun duration(): Int = AudioPlayerHelper.getDuration()
}
