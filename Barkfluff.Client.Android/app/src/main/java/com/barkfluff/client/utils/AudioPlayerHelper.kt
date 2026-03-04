package com.barkfluff.client.utils

import android.media.MediaPlayer
import android.os.Handler
import android.os.Looper
import android.util.Log
import java.io.File

/**
 * Синглтон для воспроизведения аудиофайлов.
 * Гарантирует что одновременно играет только один файл.
 */
object AudioPlayerHelper {

    private const val TAG = "AudioPlayerHelper"
    private const val PROGRESS_INTERVAL_MS = 250L

    private var mediaPlayer: MediaPlayer? = null
    private var currentFileId: String? = null
    private var currentCallbacks: AudioCallbacks? = null
    private val handler = Handler(Looper.getMainLooper())

    private val progressRunnable = object : Runnable {
        override fun run() {
            val mp = mediaPlayer ?: return
            if (mp.isPlaying) {
                try {
                    currentCallbacks?.onProgress(mp.currentPosition, mp.duration)
                } catch (_: IllegalStateException) { }
                handler.postDelayed(this, PROGRESS_INTERVAL_MS)
            }
        }
    }

    fun getCurrentFileId(): String? = currentFileId

    fun isPlaying(): Boolean = mediaPlayer?.isPlaying == true

    fun isActiveFile(fileId: String): Boolean = currentFileId == fileId

    /**
     * Запускает воспроизведение файла. Если уже играет другой файл — останавливает его.
     */
    fun play(fileId: String, file: File, callbacks: AudioCallbacks) {
        if (currentFileId != fileId) {
            stopInternal()
        }
        currentFileId = fileId
        currentCallbacks = callbacks

        if (mediaPlayer != null && !isPlaying()) {
            // Resume
            mediaPlayer?.start()
            callbacks.onStateChanged(true)
            handler.post(progressRunnable)
            return
        }

        mediaPlayer = MediaPlayer().apply {
            try {
                setDataSource(file.absolutePath)
                prepare()
                setOnCompletionListener {
                    handler.removeCallbacks(progressRunnable)
                    currentCallbacks?.onStateChanged(false)
                    currentCallbacks?.onProgress(duration, duration)
                    currentCallbacks?.onComplete()
                }
                setOnErrorListener { _, what, extra ->
                    Log.e(TAG, "MediaPlayer error: what=$what extra=$extra")
                    handler.removeCallbacks(progressRunnable)
                    currentCallbacks?.onError()
                    true
                }
                start()
                callbacks.onStateChanged(true)
                handler.post(progressRunnable)
            } catch (e: Exception) {
                Log.e(TAG, "Error playing audio", e)
                callbacks.onError()
            }
        }
    }

    fun pause() {
        mediaPlayer?.let {
            if (it.isPlaying) {
                it.pause()
                handler.removeCallbacks(progressRunnable)
                currentCallbacks?.onStateChanged(false)
            }
        }
    }

    fun resume() {
        mediaPlayer?.let {
            if (!it.isPlaying) {
                it.start()
                currentCallbacks?.onStateChanged(true)
                handler.post(progressRunnable)
            }
        }
    }

    fun stop() {
        stopInternal()
        currentCallbacks?.onStateChanged(false)
        currentCallbacks = null
        currentFileId = null
    }

    fun seekTo(positionMs: Int) {
        try {
            mediaPlayer?.seekTo(positionMs)
        } catch (_: IllegalStateException) { }
    }

    fun getCurrentPosition(): Int = try { mediaPlayer?.currentPosition ?: 0 } catch (_: IllegalStateException) { 0 }

    fun getDuration(): Int = try { mediaPlayer?.duration ?: 0 } catch (_: IllegalStateException) { 0 }

    fun swapCallbacks(fileId: String, callbacks: AudioCallbacks?) {
        if (currentFileId == fileId) {
            currentCallbacks = callbacks
        }
    }

    fun release() {
        stopInternal()
        currentCallbacks = null
        currentFileId = null
    }

    private fun stopInternal() {
        handler.removeCallbacks(progressRunnable)
        mediaPlayer?.let {
            try { if (it.isPlaying) it.stop() } catch (_: IllegalStateException) { }
            it.release()
        }
        mediaPlayer = null
    }
}

interface AudioCallbacks {
    fun onProgress(positionMs: Int, durationMs: Int)
    fun onStateChanged(isPlaying: Boolean)
    fun onError()
    fun onComplete()
}
