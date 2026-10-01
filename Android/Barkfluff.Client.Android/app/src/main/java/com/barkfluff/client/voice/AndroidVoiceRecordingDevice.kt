package com.barkfluff.client.voice

import android.content.Context
import android.media.MediaRecorder
import android.media.AudioManager
import android.media.AudioRecordingConfiguration
import java.io.File

class AndroidVoiceRecordingDevice(
    private val context: Context,
    private val onInterrupted: () -> Unit,
) : VoiceRecordingDevice {
    private var recorder: MediaRecorder? = null
    private var file: File? = null
    private var recordingCallback: AudioManager.AudioRecordingCallback? = null
    private var paused = false

    override fun start() {
        val target = File.createTempFile("voice_", ".ogg", context.cacheDir)
        val mediaRecorder = MediaRecorder(context)
        try {
            mediaRecorder.apply {
                setAudioSource(MediaRecorder.AudioSource.MIC)
                setOutputFormat(MediaRecorder.OutputFormat.OGG)
                setAudioEncoder(MediaRecorder.AudioEncoder.OPUS)
                setAudioChannels(1)
                setAudioSamplingRate(48_000)
                setAudioEncodingBitRate(24_000)
                setOutputFile(target.absolutePath)
                prepare()
                start()
            }
            recorder = mediaRecorder
            file = target
            val callback = object : AudioManager.AudioRecordingCallback() {
                override fun onRecordingConfigChanged(configs: MutableList<AudioRecordingConfiguration>) {
                    if (recorder === mediaRecorder && configs.any { it.isClientSilenced }) onInterrupted()
                }
            }
            recordingCallback = callback
            mediaRecorder.registerAudioRecordingCallback(context.mainExecutor, callback)
            mediaRecorder.setOnErrorListener { _, _, _ -> if (recorder === mediaRecorder) onInterrupted() }
        } catch (error: Exception) {
            recordingCallback?.let { runCatching { mediaRecorder.unregisterAudioRecordingCallback(it) } }
            recordingCallback = null
            recorder = null
            file = null
            mediaRecorder.release()
            target.delete()
            throw error
        }
    }

    override fun pause() { recorder?.pause(); paused = true }
    override fun resume() { recorder?.resume(); paused = false }

    override fun stop(): File {
        val target = checkNotNull(file)
        try {
            // OGG/Opus stop can stall while its encoder is paused (including on API 31).
            // Resume only to drain/finalize; the controller already froze the active-time timer.
            if (paused) resume()
            checkNotNull(recorder).stop()
            return target
        } catch (error: Exception) {
            target.delete()
            throw error
        } finally {
            releaseRecorder()
            file = null
        }
    }

    override fun cancel() {
        if (paused) runCatching { resume() }
        // A canceled clip needs no finalization. Release the capture and delete its bytes.
        releaseRecorder()
        file?.delete()
        file = null
    }

    private fun releaseRecorder() {
        recorder?.let { current ->
            recordingCallback?.let { runCatching { current.unregisterAudioRecordingCallback(it) } }
            runCatching { current.release() }
        }
        recordingCallback = null
        recorder = null
        paused = false
    }
}
