package com.barkfluff.client.cache

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * SQLCipher 4 derives the page key from a passphrase with PBKDF2-HMAC-SHA512 (256 000 iterations)
 * on every new connection. Room opens several connections, so each one paid that cost at startup.
 * Opening with the already derived key in raw form (`x'<hex>'`) skips the derivation and reads the
 * same file without re-encrypting it.
 */
internal object SqlCipherRawKey {
    private const val KDF_ITERATIONS = 256_000
    private const val KEY_LENGTH = 32
    const val SALT_LENGTH = 16

    /** Same key SQLCipher 4 derives from [passphrase] and the file salt (first 16 bytes of the db). */
    fun deriveFromPassphrase(passphrase: ByteArray, salt: ByteArray): ByteArray =
        pbkdf2HmacSha512(passphrase, salt, KDF_ITERATIONS, KEY_LENGTH)

    fun format(key: ByteArray): String = key.joinToString(separator = "", prefix = "x'", postfix = "'") {
        "%02x".format(it.toInt() and 0xff)
    }

    // PBEKeySpec takes char[], which cannot carry arbitrary passphrase bytes, so PBKDF2 is spelled out.
    internal fun pbkdf2HmacSha512(password: ByteArray, salt: ByteArray, iterations: Int, length: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA512").apply { init(SecretKeySpec(password, "HmacSHA512")) }
        val result = ByteArray(length)
        var offset = 0
        var block = 1
        while (offset < length) {
            mac.update(salt)
            mac.update(byteArrayOf((block ushr 24).toByte(), (block ushr 16).toByte(), (block ushr 8).toByte(), block.toByte()))
            var u = mac.doFinal()
            val t = u.copyOf()
            repeat(iterations - 1) {
                u = mac.doFinal(u)
                for (i in t.indices) t[i] = (t[i].toInt() xor u[i].toInt()).toByte()
            }
            val count = minOf(t.size, length - offset)
            t.copyInto(result, offset, 0, count)
            offset += count
            block++
        }
        return result
    }
}
