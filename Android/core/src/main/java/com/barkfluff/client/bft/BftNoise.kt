package com.barkfluff.client.bft

import java.io.InputStream
import java.io.OutputStream
import java.security.SecureRandom
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.min

internal data class BftClientHandshakeOptions(
    val random: BftRandomSource = BftSystemRandomSource,
    val timestampSeconds: Long? = null,
    val salt: ByteArray? = null,
    val ephemeralPrivateKey: ByteArray? = null,
    val firstRecordPaddingLength: Int? = null
)

internal data class BftClientFlight(
    val salt: ByteArray,
    val header: ByteArray,
    val noiseMessage: ByteArray,
    val record: ByteArray
)

internal class BftNoiseHandshakeState(prologue: ByteArray, responderStaticPublicKey: ByteArray) : AutoCloseable {
    private var chainingKey = PROTOCOL_NAME.copyOf()
    private var hash = PROTOCOL_NAME.copyOf()
    private var key: ByteArray? = null
    private var nonce = 0L
    private var closed = false

    init {
        mixHash(prologue)
        mixHash(responderStaticPublicKey)
    }

    fun handshakeHash(): ByteArray = hash.copyOf()

    fun mixHash(data: ByteArray) {
        val input = ByteArray(hash.size + data.size)
        hash.copyInto(input)
        data.copyInto(input, hash.size)
        val next = BftCrypto.sha256(input)
        input.fill(0)
        hash.fill(0)
        hash = next
    }

    fun mixKey(inputKeyMaterial: ByteArray) {
        val temporary = BftCrypto.hmac(chainingKey, inputKeyMaterial)
        val nextChainingKey = BftCrypto.hmac(temporary, byteArrayOf(1))
        val output = nextChainingKey + byteArrayOf(2)
        val nextKey = BftCrypto.hmac(temporary, output)
        temporary.fill(0)
        output.fill(0)
        chainingKey.fill(0)
        chainingKey = nextChainingKey
        key?.fill(0)
        key = nextKey
        nonce = 0
    }

    fun encryptAndHash(plaintext: ByteArray): ByteArray {
        val cipherKey = checkNotNull(key) { "Noise key is not initialized" }
        val ciphertext = BftAead(cipherKey).use { it.encrypt(nonce++, plaintext, hash) }
        mixHash(ciphertext)
        return ciphertext
    }

    fun decryptAndHash(ciphertext: ByteArray): ByteArray {
        val cipherKey = checkNotNull(key) { "Noise key is not initialized" }
        val plaintext = BftAead(cipherKey).use { it.decrypt(nonce++, ciphertext, hash) }
        mixHash(ciphertext)
        return plaintext
    }

    fun split(): Pair<ByteArray, ByteArray> {
        val temporary = BftCrypto.hmac(chainingKey, ByteArray(0))
        val first = BftCrypto.hmac(temporary, byteArrayOf(1))
        val secondInput = first + byteArrayOf(2)
        val second = BftCrypto.hmac(temporary, secondInput)
        temporary.fill(0)
        secondInput.fill(0)
        return first to second
    }

    override fun close() {
        if (closed) return
        chainingKey.fill(0)
        hash.fill(0)
        key?.fill(0)
        key = null
        closed = true
    }

    companion object {
        private val PROTOCOL_NAME = "Noise_NK_25519_ChaChaPoly_SHA256".toByteArray(Charsets.US_ASCII)
    }
}

internal class BftNoiseCipherState(key: ByteArray, initialCounter: Long = 0) : AutoCloseable {
    private var cipherKey = key.copyOf()
    private var aead = BftAead(cipherKey)
    var counter: Long = initialCounter
        private set
    private var closed = false

    fun encrypt(plaintext: ByteArray, associatedData: ByteArray = ByteArray(0)): ByteArray {
        check(!closed) { "Noise cipher is closed" }
        val result = aead.encrypt(counter, plaintext, associatedData)
        advance()
        return result
    }

    fun decrypt(ciphertext: ByteArray, associatedData: ByteArray = ByteArray(0)): ByteArray {
        check(!closed) { "Noise cipher is closed" }
        if (ciphertext.size < 16) throw BftRecordException("Truncated Noise transport message")
        val result = aead.decrypt(counter, ciphertext, associatedData)
        advance()
        return result
    }

    private fun advance() {
        if (counter == -1L) throw BftRecordException("Noise nonce counter exhausted")
        counter++
        if (counter != 0L && counter and 0xffffL == 0L) rekey()
    }

    private fun rekey() {
        val zeroes = ByteArray(32)
        val encrypted = aead.encrypt(-1L, zeroes)
        zeroes.fill(0)
        val replacementKey = encrypted.copyOf(32)
        encrypted.fill(0)
        aead.close()
        cipherKey.fill(0)
        cipherKey = replacementKey
        aead = BftAead(cipherKey)
    }

    override fun close() {
        if (closed) return
        aead.close()
        cipherKey.fill(0)
        closed = true
    }
}

internal class BftNoiseSession private constructor(
    private val reader: BftObfuscatedRecordReader,
    private val writer: BftObfuscatedRecordWriter,
    private val readCipher: BftNoiseCipherState,
    private val writeCipher: BftNoiseCipherState,
    private val keys: BftObfuscationKeys,
    val handshakeHash: ByteArray
) : AutoCloseable {
    private val closed = AtomicBoolean(false)

    fun readMessage(): ByteArray? {
        check(!closed.get()) { "Noise session is closed" }
        val encrypted = reader.read() ?: return null
        try {
            if (encrypted.size !in 16..BftObfuscatedRecords.MAX_PAYLOAD_LENGTH) {
                throw BftRecordException("Invalid Noise transport message length")
            }
            val plaintext = readCipher.decrypt(encrypted)
            if (plaintext.size > 16384) {
                plaintext.fill(0)
                throw BftRecordException("Noise transport plaintext exceeds 16384 bytes")
            }
            return plaintext
        } finally {
            encrypted.fill(0)
        }
    }

    fun writeMessage(plaintext: ByteArray) {
        check(!closed.get()) { "Noise session is closed" }
        require(plaintext.size <= 16384) { "Noise transport plaintext exceeds 16384 bytes" }
        val encrypted = writeCipher.encrypt(plaintext)
        try {
            writer.write(encrypted)
        } finally {
            encrypted.fill(0)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        readCipher.close()
        writeCipher.close()
        keys.close()
        handshakeHash.fill(0)
    }

    companion object {
        fun create(
            input: InputStream,
            output: OutputStream,
            keys: BftObfuscationKeys,
            client: Boolean,
            c2sKey: ByteArray,
            s2cKey: ByteArray,
            handshakeHash: ByteArray,
            random: BftRandomSource
        ): BftNoiseSession = BftNoiseSession(
            BftObfuscatedRecordReader(input, keys, clientToServer = !client, initialSequence = 1),
            BftObfuscatedRecordWriter(output, keys, clientToServer = client, random = random, initialSequence = 1),
            BftNoiseCipherState(if (client) s2cKey else c2sKey),
            BftNoiseCipherState(if (client) c2sKey else s2cKey),
            keys,
            handshakeHash
        ).also {
            c2sKey.fill(0)
            s2cKey.fill(0)
        }
    }
}

internal object BftClientHandshake {
    fun createFlight(settings: BftConnectionSettings, options: BftClientHandshakeOptions = BftClientHandshakeOptions()): BftClientFlight =
        buildMaterial(settings, options).use { it.flight.copyDeep() }

    fun connect(
        input: InputStream,
        output: OutputStream,
        settings: BftConnectionSettings,
        options: BftClientHandshakeOptions = BftClientHandshakeOptions()
    ): BftNoiseSession {
        val material = buildMaterial(settings, options)
        try {
            output.write(material.flight.salt)
            output.write(material.flight.record)
            output.flush()
            val serverMessage = BftObfuscatedRecordReader(input, material.keys, clientToServer = false).read()
                ?: throw BftProtocolException("Exit closed before its first Noise response")
            try {
                if (serverMessage.size != 48) throw BftProtocolException("Exit Noise response must be 48 bytes")
                val remoteEphemeral = serverMessage.copyOfRange(0, 32)
                material.noise.mixHash(remoteEphemeral)
                val ee = BftCrypto.x25519(material.ephemeralPrivateKey, remoteEphemeral)
                remoteEphemeral.fill(0)
                try {
                    material.noise.mixKey(ee)
                } finally {
                    ee.fill(0)
                }
                val empty = material.noise.decryptAndHash(serverMessage.copyOfRange(32, 48))
                try {
                    if (empty.isNotEmpty()) throw BftProtocolException("Exit Noise payload must be empty")
                } finally {
                    empty.fill(0)
                }
            } finally {
                serverMessage.fill(0)
            }
            val (c2s, s2c) = material.noise.split()
            val hash = material.noise.handshakeHash()
            val sessionKeys = material.takeKeys()
            return try {
                BftNoiseSession.create(input, output, sessionKeys, true, c2s, s2c, hash, options.random)
            } catch (error: Throwable) {
                sessionKeys.close()
                c2s.fill(0)
                s2c.fill(0)
                hash.fill(0)
                throw error
            }
        } finally {
            material.close()
        }
    }

    private fun buildMaterial(settings: BftConnectionSettings, options: BftClientHandshakeOptions): HandshakeMaterial {
        require(settings.staticPublicKey.size == 32 && settings.obfsKey.size == 32) { "BFT keys must be 32 bytes" }
        val salt = options.salt?.copyOf() ?: ByteArray(32).also(options.random::fill)
        if (salt.size != 32) {
            salt.fill(0)
            throw IllegalArgumentException("BFT salt must be 32 bytes")
        }
        val privateKey = options.ephemeralPrivateKey?.copyOf() ?: ByteArray(32).also(options.random::fill)
        if (privateKey.size != 32) {
            privateKey.fill(0)
            salt.fill(0)
            throw IllegalArgumentException("X25519 ephemeral private key must be 32 bytes")
        }
        var noise: BftNoiseHandshakeState? = null
        var keys: BftObfuscationKeys? = null
        var ephemeralPublic: ByteArray? = null
        var prologue: ByteArray? = null
        var payload: ByteArray? = null
        try {
            val timestamp = options.timestampSeconds ?: System.currentTimeMillis() / 1000L
            require(timestamp >= 0) { "BFT timestamp cannot be negative" }
            val header = ByteArray(10).also {
                it[0] = 1
                it[1] = 0
                for (index in 0 until 8) it[2 + index] = (timestamp ushr (56 - 8 * index)).toByte()
            }
            val publicKey = BftCrypto.x25519Public(privateKey)
            ephemeralPublic = publicKey
            val noisePrologue = "BFTv1".toByteArray(Charsets.US_ASCII) + salt + header
            prologue = noisePrologue
            val noiseState = BftNoiseHandshakeState(noisePrologue, settings.staticPublicKey)
            noise = noiseState
            val obfuscationKeys = BftObfuscationKeys.derive(settings.obfsKey, salt)
            keys = obfuscationKeys
            noiseState.mixHash(publicKey)
            val es = BftCrypto.x25519(privateKey, settings.staticPublicKey)
            try {
                noiseState.mixKey(es)
            } finally {
                es.fill(0)
            }
            val tag = noiseState.encryptAndHash(ByteArray(0))
            val noiseMessage = publicKey + tag
            val firstPayload = header + noiseMessage
            payload = firstPayload
            val record = BftObfuscatedRecords.encrypt(
                obfuscationKeys, true, 0, firstPayload,
                options.firstRecordPaddingLength ?: options.random.nextIntInclusive(BftObfuscatedRecords.MAX_PADDING_LENGTH)
            )
            firstPayload.fill(0)
            tag.fill(0)
            return HandshakeMaterial(BftClientFlight(salt, header, noiseMessage, record), privateKey, noiseState, obfuscationKeys)
        } catch (error: Throwable) {
            noise?.close()
            keys?.close()
            privateKey.fill(0)
            salt.fill(0)
            payload?.fill(0)
            throw error
        } finally {
            prologue?.fill(0)
            ephemeralPublic?.fill(0)
        }
    }

    private data class HandshakeMaterial(
        val flight: BftClientFlight,
        val ephemeralPrivateKey: ByteArray,
        val noise: BftNoiseHandshakeState,
        private var ownedKeys: BftObfuscationKeys?
    ) : AutoCloseable {
        val keys: BftObfuscationKeys get() = checkNotNull(ownedKeys) { "Handshake keys already transferred" }

        fun takeKeys(): BftObfuscationKeys = checkNotNull(ownedKeys).also { ownedKeys = null }

        override fun close() {
            ephemeralPrivateKey.fill(0)
            flight.record.fill(0)
            noise.close()
            ownedKeys?.close()
            ownedKeys = null
        }
    }

    private fun BftClientFlight.copyDeep(): BftClientFlight =
        BftClientFlight(salt.copyOf(), header.copyOf(), noiseMessage.copyOf(), record.copyOf())
}
