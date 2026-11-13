package com.barkfluff.client.bft

import org.bouncycastle.crypto.params.X25519PrivateKeyParameters
import org.bouncycastle.crypto.params.X25519PublicKeyParameters
import java.io.Closeable
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec

interface BftRandomSource {
    fun fill(destination: ByteArray)
}

object BftSystemRandomSource : BftRandomSource {
    private val random = SecureRandom()
    @Synchronized override fun fill(destination: ByteArray) = random.nextBytes(destination)
}

internal fun BftRandomSource.nextIntInclusive(maximum: Int): Int {
    require(maximum >= 0)
    val range = maximum.toLong() + 1L
    val limit = (1L shl 32) - ((1L shl 32) % range)
    val bytes = ByteArray(4)
    try {
        while (true) {
            fill(bytes)
            val value = (bytes[0].toLong() and 0xff) or
                ((bytes[1].toLong() and 0xff) shl 8) or
                ((bytes[2].toLong() and 0xff) shl 16) or
                ((bytes[3].toLong() and 0xff) shl 24)
            if (value < limit) return (value % range).toInt()
        }
    } finally {
        bytes.fill(0)
    }
}

internal object BftCrypto {
    fun hkdfExtract(salt: ByteArray, inputKeyMaterial: ByteArray): ByteArray = hmac(salt, inputKeyMaterial)

    fun hkdfExpand32(prk: ByteArray, info: ByteArray): ByteArray {
        val input = info.copyOf(info.size + 1).also { it[it.lastIndex] = 1 }
        return try { hmac(prk, input) } finally { input.fill(0) }
    }

    fun hmac(key: ByteArray, data: ByteArray): ByteArray =
        Mac.getInstance("HmacSHA256").run {
            init(SecretKeySpec(key, "HmacSHA256"))
            doFinal(data)
        }

    fun sha256(data: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(data)

    fun x25519Public(privateKey: ByteArray): ByteArray {
        require(privateKey.size == KEY_LENGTH) { "X25519 private key must be 32 bytes" }
        return X25519PrivateKeyParameters(privateKey, 0).generatePublicKey().encoded
    }

    fun x25519(privateKey: ByteArray, publicKey: ByteArray): ByteArray {
        require(privateKey.size == KEY_LENGTH && publicKey.size == KEY_LENGTH) { "X25519 keys must be 32 bytes" }
        val result = ByteArray(KEY_LENGTH)
        try {
            X25519PrivateKeyParameters(privateKey, 0).generateSecret(
                X25519PublicKeyParameters(publicKey, 0), result, 0
            )
        } catch (error: RuntimeException) {
            result.fill(0)
            throw BftProtocolException("Invalid X25519 public key", error)
        }
        if (result.all { it == 0.toByte() }) {
            result.fill(0)
            throw BftProtocolException("X25519 produced an all-zero shared secret")
        }
        return result
    }

    const val KEY_LENGTH = 32
}

/** Reuses the JCA Cipher instance; each operation reinitializes it with the message nonce. */
internal class BftAead(key: ByteArray) : Closeable {
    private val keyBytes = key.copyOf()
    private val cipher = Cipher.getInstance("ChaCha20-Poly1305")
    private var closed = false

    @Synchronized
    fun encrypt(counter: Long, plaintext: ByteArray, associatedData: ByteArray = EMPTY): ByteArray {
        check(!closed) { "AEAD is closed" }
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(keyBytes, "ChaCha20"), IvParameterSpec(nonce(counter)))
        if (associatedData.isNotEmpty()) cipher.updateAAD(associatedData)
        return cipher.doFinal(plaintext)
    }

    @Synchronized
    fun decrypt(counter: Long, ciphertextAndTag: ByteArray, associatedData: ByteArray = EMPTY): ByteArray {
        check(!closed) { "AEAD is closed" }
        require(ciphertextAndTag.size >= 16) { "Truncated ChaCha20-Poly1305 message" }
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(keyBytes, "ChaCha20"), IvParameterSpec(nonce(counter)))
        if (associatedData.isNotEmpty()) cipher.updateAAD(associatedData)
        return try {
            cipher.doFinal(ciphertextAndTag)
        } catch (error: javax.crypto.AEADBadTagException) {
            throw BftRecordException("ChaCha20-Poly1305 authentication failed", error)
        }
    }

    @Synchronized
    override fun close() {
        if (closed) return
        keyBytes.fill(0)
        val zeroKey = ByteArray(BftCrypto.KEY_LENGTH)
        try {
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(zeroKey, "ChaCha20"), IvParameterSpec(ByteArray(12)))
        } catch (_: Exception) {
        } finally {
            zeroKey.fill(0)
            closed = true
        }
    }

    companion object {
        private val EMPTY = ByteArray(0)

        fun nonce(counter: Long): ByteArray = ByteArray(12).also { bytes ->
            for (index in 0 until 8) bytes[4 + index] = (counter ushr (index * 8)).toByte()
        }
    }
}

internal class BftRecordException(message: String, cause: Throwable? = null) : java.io.IOException(message, cause)
internal class BftProtocolException(message: String, cause: Throwable? = null) : java.io.IOException(message, cause)
