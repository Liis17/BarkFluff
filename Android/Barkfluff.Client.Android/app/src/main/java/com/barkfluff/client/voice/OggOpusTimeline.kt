package com.barkfluff.client.voice

import androidx.media3.extractor.OpusUtil
import java.nio.ByteBuffer
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.RandomAccessFile

/** Repairs paused MediaRecorder OGG granules from encoded Opus samples, without re-encoding. */
internal object OggOpusTimeline {
    private val crcTable = IntArray(256) { index ->
        var value = index shl 24
        repeat(8) { value = (value shl 1) xor if (value < 0) 0x04c11db7 else 0 }
        value
    }

    fun normalize(file: File) = RandomAccessFile(file, "rw").use { stream ->
        val packet = ByteArrayOutputStream()
        var packetIndex = 0
        var samples = 0L
        var previousGranule = 0L
        while (stream.filePointer < stream.length()) {
            val offset = stream.filePointer
            val header = ByteArray(27).also(stream::readFully)
            require(String(header, 0, 4, Charsets.US_ASCII) == "OggS" && header[4] == 0.toByte())
            val lacing = ByteArray(header[26].toInt() and 255).also(stream::readFully)
            val body = ByteArray(lacing.sumOf { it.toInt() and 255 }).also(stream::readFully)
            val nextPage = stream.filePointer
            val before = samples
            var position = 0
            for (segment in lacing) {
                val length = segment.toInt() and 255
                packet.write(body, position, length)
                position += length
                if (length < 255) {
                    val bytes = packet.toByteArray()
                    when (packetIndex++) {
                        0 -> require(bytes.size >= 19 && String(bytes, 0, 8, Charsets.US_ASCII) == "OpusHead")
                        1 -> require(bytes.size >= 8 && String(bytes, 0, 8, Charsets.US_ASCII) == "OpusTags")
                        else -> samples += OpusUtil.parsePacketAudioSampleCount(ByteBuffer.wrap(bytes)).also { require(it in 1..5_760) }
                    }
                    packet.reset()
                }
            }
            val eos = header[5].toInt() and 4 != 0
            if (samples != before || eos && samples > 0L) {
                var originalGranule = 0L
                repeat(8) { originalGranule = originalGranule or ((header[6 + it].toLong() and 255L) shl (it * 8)) }
                // Keep the final packet's end trim, including an empty EOS page. Pre-skip
                // stays in OpusHead and is applied by the decoder (RFC 7845 section 4).
                val trim = if (eos) (previousGranule + samples - before - originalGranule).coerceIn(0L, samples) else 0L
                val granule = samples - trim
                repeat(8) { header[6 + it] = (granule ushr (it * 8)).toByte() }
                repeat(4) { header[22 + it] = 0 }
                var crc = 0
                for (bytes in arrayOf(header, lacing, body)) {
                    for (byte in bytes) crc = (crc shl 8) xor crcTable[((crc ushr 24) xor (byte.toInt() and 255)) and 255]
                }
                repeat(4) { header[22 + it] = (crc ushr (it * 8)).toByte() }
                stream.seek(offset)
                stream.write(header)
                stream.seek(nextPage)
                previousGranule = originalGranule
            }
        }
        require(packet.size() == 0 && samples > 0L)
    }

}
