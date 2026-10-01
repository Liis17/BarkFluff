package com.barkfluff.client.voice

import java.io.File
import org.junit.Assert.*
import org.junit.Test

class OggOpusTimelineTest {
    private val original = requireNotNull(javaClass.getResourceAsStream("/voice_timeline.ogg")).use { it.readBytes() }

    @Test
    fun pausedGranulesAreRepairedWithoutChangingPacketsHeadersOrEndTrim() {
        val paused = original.copyOf()
        pages(paused).drop(3).forEach { offset ->
            val granule = readGranule(paused, offset) + 96_000L
            repeat(8) { paused[offset + 6 + it] = (granule ushr (8 * it)).toByte() }
        }
        withClip(paused) { file ->
            OggOpusTimeline.normalize(file)
            // Exact fixture equality independently verifies page CRCs, OpusHead/pre-skip,
            // encoded payloads, and the FFmpeg fixture's partial final-frame trim.
            assertArrayEquals(original, file.readBytes())
        }
    }

    @Test
    fun normalizingAnUnpausedClipAndRetryingPreservesItsBytes() {
        withClip(original) { file ->
            OggOpusTimeline.normalize(file)
            OggOpusTimeline.normalize(file)
            assertArrayEquals(original, file.readBytes())
        }
    }

    @Test
    fun truncatedRecordingCannotBecomeAnAcceptedPreview() {
        withClip(original.copyOf(original.size - 1)) { file ->
            assertTrue(runCatching { OggOpusTimeline.normalize(file) }.isFailure)
        }
    }

    private fun pages(bytes: ByteArray): List<Int> {
        val result = mutableListOf<Int>()
        var offset = 0
        while (offset < bytes.size) {
            result += offset
            val segments = bytes[offset + 26].toInt() and 255
            val size = (0 until segments).sumOf { bytes[offset + 27 + it].toInt() and 255 }
            offset += 27 + segments + size
        }
        return result
    }

    private fun readGranule(bytes: ByteArray, offset: Int): Long {
        var value = 0L
        repeat(8) { value = value or ((bytes[offset + 6 + it].toLong() and 255L) shl (8 * it)) }
        return value
    }

    private fun withClip(bytes: ByteArray, action: (File) -> Unit) {
        val file = File.createTempFile("voice-timeline", ".ogg").apply { writeBytes(bytes) }
        try { action(file) } finally { file.delete() }
    }
}
