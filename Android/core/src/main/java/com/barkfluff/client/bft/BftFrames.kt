package com.barkfluff.client.bft

import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

internal enum class BftFrameType(val id: Int) {
    AUTH(0x01), HELLO(0x02), OPEN(0x03), DATA(0x04), WINDOW_UPDATE(0x05), CLOSE(0x06), RESET(0x07),
    PING(0x08), PONG(0x09), GOAWAY(0x0a);

    companion object {
        fun fromId(id: Int): BftFrameType = entries.firstOrNull { it.id == id }
            ?: throw BftProtocolException("Unknown BFT frame type")
    }
}

enum class BftResetCode(val id: Int) {
    CANCEL(0), SERVICE_UNAVAILABLE(1), UPSTREAM_FAILED(2), FLOW_CONTROL(3), STREAM_LIMIT(4),
    IDLE_TIMEOUT(5), INTERNAL(6), STREAM_CLOSED(7);

    companion object {
        fun fromId(id: Int): BftResetCode = entries.firstOrNull { it.id == id } ?: INTERNAL
    }
}

enum class BftGoAwayCode(val id: Int) {
    SHUTDOWN(0), AUTH_FAILED(1), PROTOCOL_ERROR(2), OVERLOAD(3);

    companion object {
        fun fromId(id: Int): BftGoAwayCode = entries.firstOrNull { it.id == id } ?: SHUTDOWN
    }
}

data class BftHello(
    val proxyId: String,
    val initialWindow: Int,
    val maxStreams: Int,
    val tlsSpkiSha256: ByteArray,
    val serviceIds: List<Int>
)

internal data class BftFrame(val type: BftFrameType, val streamId: Long, val body: ByteArray) {
    companion object {
        fun auth(token: String): BftFrame = BftFrame(BftFrameType.AUTH, 0, BftFrameCodec.strictUtf8(token).also {
            if (it.size !in 1..256) throw IllegalArgumentException("AUTH token must be 1–256 UTF-8 bytes")
        })

        fun hello(value: BftHello) = BftFrame(BftFrameType.HELLO, 0, BftFrameCodec.encodeHello(value))
        fun open(id: Long, service: Int) = BftFrame(BftFrameType.OPEN, id, byteArrayOf(service.toByte()))
        fun data(id: Long, bytes: ByteArray) = BftFrame(BftFrameType.DATA, id, bytes)
        fun windowUpdate(id: Long, increment: Long) = BftFrame(BftFrameType.WINDOW_UPDATE, id, bftU32(increment))
        fun close(id: Long) = BftFrame(BftFrameType.CLOSE, id, ByteArray(0))
        fun reset(id: Long, code: BftResetCode) = BftFrame(BftFrameType.RESET, id, byteArrayOf(code.id.toByte()))
        fun ping(bytes: ByteArray) = BftFrame(BftFrameType.PING, 0, bytes)
        fun pong(bytes: ByteArray) = BftFrame(BftFrameType.PONG, 0, bytes)
        fun goAway(lastStreamId: Long, code: BftGoAwayCode) =
            BftFrame(BftFrameType.GOAWAY, 0, bftU32(lastStreamId) + byteArrayOf(code.id.toByte()))
    }
}

internal object BftFrameCodec {
    const val HEADER_LENGTH = 7
    const val MAX_BODY_LENGTH = 16377
    const val MAX_MESSAGE_LENGTH = 16384

    fun encode(frame: BftFrame): ByteArray {
        validate(frame)
        val result = ByteArray(HEADER_LENGTH + frame.body.size)
        result[0] = frame.type.id.toByte()
        putU32(frame.streamId, result, 1)
        putU16(frame.body.size, result, 5)
        frame.body.copyInto(result, HEADER_LENGTH)
        return result
    }

    fun encodeMany(frames: List<BftFrame>): ByteArray {
        var length = 0
        for (frame in frames) {
            validate(frame)
            length += HEADER_LENGTH + frame.body.size
            if (length > MAX_MESSAGE_LENGTH) throw IllegalArgumentException("BFT frames exceed one Noise message")
        }
        val result = ByteArray(length)
        var offset = 0
        for (frame in frames) {
            result[offset] = frame.type.id.toByte()
            putU32(frame.streamId, result, offset + 1)
            putU16(frame.body.size, result, offset + 5)
            frame.body.copyInto(result, offset + HEADER_LENGTH)
            offset += HEADER_LENGTH + frame.body.size
        }
        return result
    }

    fun decode(message: ByteArray): List<BftFrame> {
        if (message.size > MAX_MESSAGE_LENGTH) throw BftProtocolException("BFT mux message exceeds 16384 bytes")
        val frames = ArrayList<BftFrame>()
        var offset = 0
        while (offset < message.size) {
            if (message.size - offset < HEADER_LENGTH) throw BftProtocolException("Truncated BFT frame header")
            val type = BftFrameType.fromId(message[offset].toInt() and 0xff)
            val streamId = readU32(message, offset + 1)
            val length = readU16(message, offset + 5)
            if (length > MAX_BODY_LENGTH || message.size - offset - HEADER_LENGTH < length) {
                throw BftProtocolException("Invalid BFT frame length")
            }
            val frame = BftFrame(type, streamId, message.copyOfRange(offset + HEADER_LENGTH, offset + HEADER_LENGTH + length))
            validate(frame)
            frames += frame
            offset += HEADER_LENGTH + length
        }
        return frames
    }

    fun parseHello(body: ByteArray): BftHello {
        if (body.size < 40) throw BftProtocolException("Truncated HELLO body")
        val idLength = body[0].toInt() and 0xff
        if (idLength !in 1..32 || body.size < 40 + idLength) throw BftProtocolException("Invalid HELLO proxy ID length")
        val idBytes = body.copyOfRange(1, 1 + idLength)
        if (idBytes.any { (it.toInt() and 0xff) >= 128 }) throw BftProtocolException("HELLO proxy ID must be ASCII")
        val proxyId = String(idBytes, StandardCharsets.US_ASCII)
        val window = readU32(body, 1 + idLength)
        val maxStreams = readU16(body, 5 + idLength)
        val pin = body.copyOfRange(7 + idLength, 39 + idLength)
        val serviceCount = body[39 + idLength].toInt() and 0xff
        if (body.size != 40 + idLength + serviceCount) throw BftProtocolException("Invalid HELLO service list length")
        val services = (0 until serviceCount).map { body[40 + idLength + it].toInt() and 0xff }
        val hello = BftHello(proxyId, window.toIntChecked(), maxStreams, pin, services)
        validateHello(hello)
        return hello
    }

    fun encodeHello(hello: BftHello): ByteArray {
        validateHello(hello)
        val id = strictUtf8(hello.proxyId)
        val result = ByteArray(40 + id.size + hello.serviceIds.size)
        result[0] = id.size.toByte()
        id.copyInto(result, 1)
        putU32(hello.initialWindow.toLong(), result, 1 + id.size)
        putU16(hello.maxStreams, result, 5 + id.size)
        hello.tlsSpkiSha256.copyInto(result, 7 + id.size)
        result[39 + id.size] = hello.serviceIds.size.toByte()
        hello.serviceIds.forEachIndexed { index, service -> result[40 + id.size + index] = service.toByte() }
        return result
    }

    fun validateHello(hello: BftHello) {
        val idBytes = strictUtf8(hello.proxyId)
        if (idBytes.size !in 1..32 || idBytes.any { (it.toInt() and 0xff) >= 128 }) {
            throw BftProtocolException("HELLO proxy ID must be 1–32 ASCII bytes")
        }
        if (hello.initialWindow !in 16384..Int.MAX_VALUE || hello.maxStreams !in 1..65535) {
            throw BftProtocolException("HELLO limits are invalid")
        }
        if (hello.tlsSpkiSha256.size != 32) throw BftProtocolException("HELLO TLS pin must be 32 bytes")
        if (hello.serviceIds.size > 255 || hello.serviceIds.any { it !in 1..12 } || hello.serviceIds.toSet().size != hello.serviceIds.size) {
            throw BftProtocolException("HELLO service IDs must be known and unique")
        }
    }

    private fun validate(frame: BftFrame) {
        if (frame.body.size > MAX_BODY_LENGTH) throw BftProtocolException("BFT frame body is too large")
        val tunnelFrame = when (frame.type) {
            BftFrameType.AUTH, BftFrameType.HELLO, BftFrameType.PING, BftFrameType.PONG, BftFrameType.GOAWAY -> true
            else -> false
        }
        if (if (tunnelFrame) frame.streamId != 0L else frame.streamId == 0L) {
            throw BftProtocolException("BFT frame uses an invalid stream ID")
        }
        when (frame.type) {
            BftFrameType.AUTH -> {
                if (frame.body.size !in 1..256) throw BftProtocolException("Invalid AUTH length")
                decodeUtf8(frame.body)
            }
            BftFrameType.HELLO -> parseHello(frame.body)
            BftFrameType.OPEN -> if (frame.body.size != 1 || (frame.body[0].toInt() and 0xff) !in 1..12) {
                throw BftProtocolException("Invalid OPEN service ID")
            }
            BftFrameType.DATA -> if (frame.body.isEmpty()) throw BftProtocolException("DATA cannot be empty")
            BftFrameType.WINDOW_UPDATE -> if (frame.body.size != 4 || readU32(frame.body, 0) == 0L) {
                throw BftProtocolException("Invalid WINDOW_UPDATE increment")
            }
            BftFrameType.CLOSE -> if (frame.body.isNotEmpty()) throw BftProtocolException("CLOSE body must be empty")
            BftFrameType.RESET -> if (frame.body.size != 1) throw BftProtocolException("RESET body must contain one code")
            BftFrameType.PING, BftFrameType.PONG -> if (frame.body.size != 8) throw BftProtocolException("PING/PONG body must be 8 bytes")
            BftFrameType.GOAWAY -> if (frame.body.size != 5) throw BftProtocolException("GOAWAY body must be 5 bytes")
        }
    }

    internal fun strictUtf8(value: String): ByteArray = try {
        StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT).encode(java.nio.CharBuffer.wrap(value))
            .let { ByteArray(it.remaining()).also(it::get) }
    } catch (error: CharacterCodingException) {
        throw IllegalArgumentException("Text is not valid UTF-8", error)
    }

    private fun decodeUtf8(value: ByteArray): String = try {
        StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT).decode(java.nio.ByteBuffer.wrap(value)).toString()
    } catch (error: CharacterCodingException) {
        throw BftProtocolException("Text is not valid UTF-8", error)
    }

    private fun readU16(bytes: ByteArray, offset: Int): Int =
        ((bytes[offset].toInt() and 0xff) shl 8) or (bytes[offset + 1].toInt() and 0xff)

    private fun readU32(bytes: ByteArray, offset: Int): Long =
        ((bytes[offset].toLong() and 0xff) shl 24) or ((bytes[offset + 1].toLong() and 0xff) shl 16) or
            ((bytes[offset + 2].toLong() and 0xff) shl 8) or (bytes[offset + 3].toLong() and 0xff)

    private fun putU16(value: Int, output: ByteArray, offset: Int) {
        output[offset] = (value ushr 8).toByte()
        output[offset + 1] = value.toByte()
    }

    private fun putU32(value: Long, output: ByteArray, offset: Int) {
        if (value !in 0..0xffff_ffffL) throw IllegalArgumentException("u32 value is out of range")
        for (index in 0..3) output[offset + index] = (value ushr (24 - index * 8)).toByte()
    }

    private fun Long.toIntChecked(): Int = if (this <= Int.MAX_VALUE) toInt() else -1
}

private fun bftU32(value: Long): ByteArray {
    if (value !in 0..0xffff_ffffL) throw IllegalArgumentException("u32 value is out of range")
    return ByteArray(4).also { output ->
        for (index in 0..3) output[index] = (value ushr (24 - index * 8)).toByte()
    }
}
