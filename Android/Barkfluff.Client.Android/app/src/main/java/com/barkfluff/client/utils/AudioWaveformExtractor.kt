package com.barkfluff.client.utils

import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.util.Log
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.abs
import kotlin.math.sqrt

object AudioWaveformExtractor {

    private const val TAG = "AudioWaveformExtractor"
    private const val DEFAULT_BAR_COUNT = 48
    private const val CODEC_TIMEOUT_US = 10_000L
    private const val MAX_CODEC_LOOPS = 4_000
    private const val MIN_USEFUL_PEAK = 0.001f

    fun extract(file: File, barCount: Int = DEFAULT_BAR_COUNT): FloatArray {
        val count = barCount.coerceAtLeast(1)
        if (!file.exists() || file.length() == 0L) return fallbackPattern(count)

        val decoded = runCatching { decodeWithCodec(file, count) }
            .onFailure { Log.w(TAG, "Failed to decode waveform for ${file.name}", it) }
            .getOrNull()

        return decoded ?: fallbackFromBytes(file, count)
    }

    private fun decodeWithCodec(file: File, barCount: Int): FloatArray? {
        val extractor = MediaExtractor()
        var decoder: MediaCodec? = null

        try {
            extractor.setDataSource(file.absolutePath)

            var trackIndex = -1
            var inputFormat: MediaFormat? = null
            for (index in 0 until extractor.trackCount) {
                val format = extractor.getTrackFormat(index)
                val mime = format.getString(MediaFormat.KEY_MIME).orEmpty()
                if (mime.startsWith("audio/")) {
                    trackIndex = index
                    inputFormat = format
                    break
                }
            }
            val format = inputFormat ?: return null
            val mime = format.getString(MediaFormat.KEY_MIME).orEmpty()
            if (trackIndex < 0 || mime.isBlank()) return null

            val durationUs = if (format.containsKey(MediaFormat.KEY_DURATION)) {
                format.getLong(MediaFormat.KEY_DURATION)
            } else {
                -1L
            }
            val sampleRate = if (format.containsKey(MediaFormat.KEY_SAMPLE_RATE)) {
                format.getInteger(MediaFormat.KEY_SAMPLE_RATE)
            } else {
                0
            }
            val channels = if (format.containsKey(MediaFormat.KEY_CHANNEL_COUNT)) {
                format.getInteger(MediaFormat.KEY_CHANNEL_COUNT).coerceAtLeast(1)
            } else {
                1
            }
            val estimatedValues = if (durationUs > 0L && sampleRate > 0) {
                ((sampleRate.toLong() * durationUs) / 1_000_000L * channels).coerceAtLeast(1L)
            } else {
                -1L
            }

            extractor.selectTrack(trackIndex)
            decoder = MediaCodec.createDecoderByType(mime)
            decoder.configure(format, null, null, 0)
            decoder.start()

            val peaks = FloatArray(barCount)
            val info = MediaCodec.BufferInfo()
            var inputDone = false
            var outputDone = false
            var valueIndex = 0L
            var loops = 0

            while (!outputDone && loops++ < MAX_CODEC_LOOPS) {
                if (!inputDone) {
                    val inputIndex = decoder.dequeueInputBuffer(CODEC_TIMEOUT_US)
                    if (inputIndex >= 0) {
                        val inputBuffer = decoder.getInputBuffer(inputIndex)
                        inputBuffer?.clear()

                        val sampleSize = if (inputBuffer != null) {
                            extractor.readSampleData(inputBuffer, 0)
                        } else {
                            -1
                        }

                        if (sampleSize < 0) {
                            decoder.queueInputBuffer(
                                inputIndex,
                                0,
                                0,
                                0L,
                                MediaCodec.BUFFER_FLAG_END_OF_STREAM
                            )
                            inputDone = true
                        } else {
                            decoder.queueInputBuffer(
                                inputIndex,
                                0,
                                sampleSize,
                                extractor.sampleTime.coerceAtLeast(0L),
                                0
                            )
                            extractor.advance()
                        }
                    }
                }

                val outputIndex = decoder.dequeueOutputBuffer(info, CODEC_TIMEOUT_US)
                when {
                    outputIndex >= 0 -> {
                        val outputBuffer = decoder.getOutputBuffer(outputIndex)
                        if (outputBuffer != null && info.size > 0) {
                            outputBuffer.position(info.offset)
                            outputBuffer.limit(info.offset + info.size)
                            val pcm = outputBuffer.slice().order(ByteOrder.LITTLE_ENDIAN)
                            val encoding = outputEncoding(decoder.outputFormat)
                            valueIndex += collectPeaks(pcm, encoding, estimatedValues, valueIndex, peaks)
                        }

                        outputDone = info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
                        decoder.releaseOutputBuffer(outputIndex, false)
                    }
                    outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                        // Output format is read on every buffer because some decoders report it late.
                    }
                    outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER && inputDone -> {
                        // Keep draining until EOS or the loop guard stops a broken decoder.
                    }
                }
            }

            return normalize(peaks).takeIf { normalized ->
                normalized.any { it > MIN_USEFUL_PEAK }
            }
        } finally {
            runCatching { decoder?.stop() }
            runCatching { decoder?.release() }
            runCatching { extractor.release() }
        }
    }

    private fun collectPeaks(
        buffer: ByteBuffer,
        encoding: Int,
        estimatedValues: Long,
        startValueIndex: Long,
        peaks: FloatArray
    ): Long {
        var consumed = 0L

        fun addPeak(amplitude: Float) {
            val bucket = if (estimatedValues > 0L) {
                ((startValueIndex + consumed) * peaks.size / estimatedValues)
                    .toInt()
                    .coerceIn(0, peaks.lastIndex)
            } else {
                ((startValueIndex + consumed) % peaks.size).toInt()
            }
            if (amplitude > peaks[bucket]) peaks[bucket] = amplitude
            consumed++
        }

        when (encoding) {
            AudioFormat.ENCODING_PCM_FLOAT -> {
                while (buffer.remaining() >= 4) {
                    addPeak(abs(buffer.getFloat()).coerceIn(0f, 1f))
                }
            }
            AudioFormat.ENCODING_PCM_8BIT -> {
                while (buffer.hasRemaining()) {
                    val unsigned = buffer.get().toInt() and 0xFF
                    addPeak(abs(unsigned - 128) / 128f)
                }
            }
            else -> {
                while (buffer.remaining() >= 2) {
                    addPeak((abs(buffer.getShort().toInt()) / 32768f).coerceIn(0f, 1f))
                }
            }
        }

        return consumed
    }

    private fun outputEncoding(format: MediaFormat): Int {
        return if (format.containsKey(MediaFormat.KEY_PCM_ENCODING)) {
            format.getInteger(MediaFormat.KEY_PCM_ENCODING)
        } else {
            AudioFormat.ENCODING_PCM_16BIT
        }
    }

    private fun normalize(peaks: FloatArray): FloatArray {
        val maxPeak = peaks.maxOrNull() ?: 0f
        if (maxPeak <= MIN_USEFUL_PEAK) return FloatArray(peaks.size)

        return FloatArray(peaks.size) { index ->
            sqrt((peaks[index] / maxPeak).coerceIn(0f, 1f)).coerceIn(0.08f, 1f)
        }
    }

    private fun fallbackFromBytes(file: File, barCount: Int): FloatArray {
        val data = runCatching { file.readBytes() }.getOrNull()
            ?: return fallbackPattern(barCount)
        if (data.isEmpty()) return fallbackPattern(barCount)

        val peaks = FloatArray(barCount)
        val chunkSize = (data.size / barCount).coerceAtLeast(1)
        for (index in 0 until barCount) {
            val start = index * chunkSize
            val end = if (index == barCount - 1) data.size else minOf(data.size, start + chunkSize)
            var sum = 0L
            var count = 0
            for (cursor in start until end) {
                sum += abs((data[cursor].toInt() and 0xFF) - 128)
                count++
            }
            peaks[index] = if (count > 0) sum / (count * 128f) else 0f
        }

        val normalized = normalize(peaks)
        return if (normalized.any { it > MIN_USEFUL_PEAK }) normalized else fallbackPattern(barCount)
    }

    private fun fallbackPattern(barCount: Int): FloatArray = FloatArray(barCount) { index ->
        val wave = abs(kotlin.math.sin(index * 0.58f)).toFloat()
        val accent = abs(kotlin.math.sin(index * 0.21f + 1.4f)).toFloat()
        (0.18f + wave * 0.58f + accent * 0.24f).coerceIn(0.12f, 1f)
    }
}
