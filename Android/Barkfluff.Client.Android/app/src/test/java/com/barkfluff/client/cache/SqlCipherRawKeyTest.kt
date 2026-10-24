package com.barkfluff.client.cache

import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class SqlCipherRawKeyTest {
    @Test
    fun pbkdf2MatchesPublishedSha512Vector() {
        val key = SqlCipherRawKey.pbkdf2HmacSha512("password".toByteArray(), "salt".toByteArray(), 1, 64)

        assertEquals(
            "867f70cf1ade02cff3752599a3a53dc4af34c7a669815ae5d513554e1c8cf252" +
                "c02d470a285a0501bad999bfe943c08f050235d7d68b1da55e63f73b60a57fce",
            key.joinToString("") { "%02x".format(it) },
        )
    }

    @Test
    fun pbkdf2MatchesJdkForManyIterations() {
        val salt = ByteArray(16) { it.toByte() }
        val expected = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA512")
            .generateSecret(PBEKeySpec("passphrase".toCharArray(), salt, 4096, 256))
            .encoded

        assertArrayEquals(expected, SqlCipherRawKey.pbkdf2HmacSha512("passphrase".toByteArray(), salt, 4096, 32))
    }

    @Test
    fun formatsRawKeyLiteral() {
        assertEquals("x'00ff10'", SqlCipherRawKey.format(byteArrayOf(0, -1, 16)))
    }
}
