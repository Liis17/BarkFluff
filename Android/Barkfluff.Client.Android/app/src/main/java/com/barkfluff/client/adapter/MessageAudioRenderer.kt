package com.barkfluff.client.adapter

import android.media.MediaMetadataRetriever
import java.io.File

/** Audio-specific presentation helpers; playback/cache ownership stays in AudioPlaybackController. */
class MessageAudioRenderer(private val playback: AudioPlaybackController) {
    fun duration(file: File): Int = runCatching {
        val retriever = MediaMetadataRetriever()
        try {
            retriever.setDataSource(file.absolutePath)
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toIntOrNull() ?: 0
        } finally {
            retriever.release()
        }
    }.getOrDefault(0)

    fun isActive(fileId: String): Boolean = playback.isActiveFile(fileId)
}
