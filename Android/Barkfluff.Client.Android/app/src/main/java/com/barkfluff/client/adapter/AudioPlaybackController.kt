package com.barkfluff.client.adapter

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
}
