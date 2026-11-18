package com.barkfluff.client.bft

import java.io.Closeable
import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream

internal class BftObfuscationKeys private constructor(
    val c2sLength: ByteArray,
    val c2sData: ByteArray,
    val s2cLength: ByteArray,
    val s2cData: ByteArray
) : Closeable {
    private val c2sLengthAead = BftAead(c2sLength)
    private val c2sDataAead = BftAead(c2sData)
    private val s2cLengthAead = BftAead(s2cLength)
    private val s2cDataAead = BftAead(s2cData)
    private var closed = false

    fun lengthAead(clientToServer: Boolean): BftAead = if (clientToServer) c2sLengthAead else s2cLengthAead
    fun dataAead(clientToServer: Boolean): BftAead = if (clientToServer) c2sDataAead else s2cDataAead

    override fun close() {
        if (closed) return
        c2sLengthAead.close()
        c2sDataAead.close()
        s2cLengthAead.close()
        s2cDataAead.close()
        c2sLength.fill(0)
        c2sData.fill(0)
        s2cLength.fill(0)
        s2cData.fill(0)
        closed = true
    }

    companion object {
        fun derive(obfsKey: ByteArray, salt: ByteArray): BftObfuscationKeys {
            require(obfsKey.size == 32 && salt.size == 32) { "BFT obfs key and salt must be 32 bytes" }
            val prk = BftCrypto.hkdfExtract(salt, obfsKey)
            try {
                fun derive(label: String) = BftCrypto.hkdfExpand32(prk, "BFTv1 obfs $label".toByteArray(Charsets.US_ASCII))
                return BftObfuscationKeys(
                    derive("c2s len"), derive("c2s data"), derive("s2c len"), derive("s2c data")
                )
            } finally {
                prk.fill(0)
            }
        }
    }
}

internal object BftObfuscatedRecords {
    const val MAX_PAYLOAD_LENGTH = 16400
    const val MAX_PADDING_LENGTH = 1024
    const val MIN_ENCRYPTED_LENGTH = 18
    const val MAX_ENCRYPTED_LENGTH = 17442
    const val MAX_RECORD_COUNT = 1L shl 48

    fun encrypt(
        keys: BftObfuscationKeys,
        clientToServer: Boolean,
        sequence: Long,
        payload: ByteArray,
        paddingLength: Int
    ): ByteArray {
        require(sequence in 0 until MAX_RECORD_COUNT) { "BFT record counter limit reached" }
        require(payload.size <= MAX_PAYLOAD_LENGTH) { "BFT record payload is too large" }
        require(paddingLength in 0..MAX_PADDING_LENGTH) { "BFT record padding is out of range" }
        val plainBody = ByteArray(2 + payload.size + paddingLength)
        plainBody[0] = (payload.size ushr 8).toByte()
        plainBody[1] = payload.size.toByte()
        payload.copyInto(plainBody, 2)
        val encryptedLength = 2 + plainBody.size - 2 + 16
        val lengthPlain = byteArrayOf((encryptedLength ushr 8).toByte(), encryptedLength.toByte())
        val length = keys.lengthAead(clientToServer).encrypt(sequence, lengthPlain)
        val body = try { keys.dataAead(clientToServer).encrypt(sequence, plainBody) } finally { plainBody.fill(0) }
        return ByteArray(length.size + body.size).also {
            length.copyInto(it)
            body.copyInto(it, length.size)
            length.fill(0)
            body.fill(0)
        }
    }

    fun decrypt(keys: BftObfuscationKeys, clientToServer: Boolean, sequence: Long, record: ByteArray): ByteArray {
        require(sequence in 0 until MAX_RECORD_COUNT) { "BFT record counter limit reached" }
        if (record.size < 18) throw BftRecordException("Truncated BFT record header")
        val lengthPlain = keys.lengthAead(clientToServer).decrypt(sequence, record.copyOfRange(0, 18))
        val encryptedLength = try {
            if (lengthPlain.size != 2) throw BftRecordException("Invalid encrypted BFT record length")
            u16(lengthPlain, 0)
        } finally {
            lengthPlain.fill(0)
        }
        validateLength(encryptedLength)
        if (record.size != 18 + encryptedLength) throw BftRecordException("BFT record length does not match its header")
        return decryptBody(keys, clientToServer, sequence, record.copyOfRange(18, record.size))
    }

    fun decryptBody(keys: BftObfuscationKeys, clientToServer: Boolean, sequence: Long, encryptedBody: ByteArray): ByteArray {
        if (encryptedBody.size !in MIN_ENCRYPTED_LENGTH..MAX_ENCRYPTED_LENGTH) {
            throw BftRecordException("BFT record body length is out of range")
        }
        val plain = keys.dataAead(clientToServer).decrypt(sequence, encryptedBody)
        try {
            if (plain.size < 2) throw BftRecordException("Truncated BFT record body")
            val payloadLength = u16(plain, 0)
            if (payloadLength > MAX_PAYLOAD_LENGTH || 2 + payloadLength > plain.size) {
                throw BftRecordException("BFT payload length exceeds its record body")
            }
            for (index in 2 + payloadLength until plain.size) {
                if (plain[index] != 0.toByte()) throw BftRecordException("BFT padding must be zero")
            }
            return plain.copyOfRange(2, 2 + payloadLength)
        } finally {
            plain.fill(0)
        }
    }

    fun validateLength(length: Int) {
        if (length !in MIN_ENCRYPTED_LENGTH..MAX_ENCRYPTED_LENGTH) {
            throw BftRecordException("BFT encrypted record length is out of range")
        }
    }

    private fun u16(bytes: ByteArray, offset: Int): Int =
        ((bytes[offset].toInt() and 0xff) shl 8) or (bytes[offset + 1].toInt() and 0xff)
}

internal class BftObfuscatedRecordWriter(
    private val output: OutputStream,
    private val keys: BftObfuscationKeys,
    private val clientToServer: Boolean,
    private val random: BftRandomSource,
    initialSequence: Long = 0
) {
    var sequence: Long = initialSequence
        private set

    @Synchronized
    fun write(payload: ByteArray, paddingLength: Int? = null) {
        if (sequence !in 0 until BftObfuscatedRecords.MAX_RECORD_COUNT) {
            throw BftRecordException("BFT record counter limit reached")
        }
        val padding = paddingLength ?: when {
            sequence == 0L -> random.nextIntInclusive(BftObfuscatedRecords.MAX_PADDING_LENGTH)
            payload.size < 1024 -> random.nextIntInclusive(255)
            else -> 0
        }
        val record = BftObfuscatedRecords.encrypt(keys, clientToServer, sequence, payload, padding)
        try {
            output.write(record)
            output.flush()
            sequence++
        } finally {
            record.fill(0)
        }
    }
}

internal class BftObfuscatedRecordReader(
    private val input: InputStream,
    private val keys: BftObfuscationKeys,
    private val clientToServer: Boolean,
    initialSequence: Long = 0
) {
    var sequence: Long = initialSequence
        private set

    @Synchronized
    fun read(): ByteArray? {
        if (sequence !in 0 until BftObfuscatedRecords.MAX_RECORD_COUNT) {
            throw BftRecordException("BFT record counter limit reached")
        }
        val header = ByteArray(18)
        if (!readExactly(header, allowCleanEof = true)) return null
        val lengthPlain = keys.lengthAead(clientToServer).decrypt(sequence, header)
        val length = try {
            if (lengthPlain.size != 2) throw BftRecordException("Invalid encrypted BFT record length")
            ((lengthPlain[0].toInt() and 0xff) shl 8) or (lengthPlain[1].toInt() and 0xff)
        } finally {
            lengthPlain.fill(0)
            header.fill(0)
        }
        BftObfuscatedRecords.validateLength(length)
        val body = ByteArray(length)
        try {
            readExactly(body, allowCleanEof = false)
            val payload = BftObfuscatedRecords.decryptBody(keys, clientToServer, sequence, body)
            sequence++
            return payload
        } finally {
            body.fill(0)
        }
    }

    private fun readExactly(destination: ByteArray, allowCleanEof: Boolean): Boolean {
        var offset = 0
        while (offset < destination.size) {
            val count = input.read(destination, offset, destination.size - offset)
            if (count < 0) {
                if (offset == 0 && allowCleanEof) return false
                throw BftRecordException("Connection ended in the middle of a BFT record", EOFException())
            }
            if (count == 0) {
                val next = input.read()
                if (next < 0) {
                    if (offset == 0 && allowCleanEof) return false
                    throw BftRecordException("Connection ended in the middle of a BFT record", EOFException())
                }
                destination[offset++] = next.toByte()
            } else {
                offset += count
            }
        }
        return true
    }
}
