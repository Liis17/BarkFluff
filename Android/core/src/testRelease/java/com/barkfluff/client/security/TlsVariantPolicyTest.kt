package com.barkfluff.client.security

import okhttp3.OkHttpClient
import okhttp3.HttpUrl.Companion.toHttpUrl
import org.junit.Assert.assertFalse
import org.junit.Assert.assertSame
import org.junit.Assert.assertThrows
import org.junit.Test

class TlsVariantPolicyTest {
    private val pins = object : TlsPinLookup {
        override fun pinFor(host: String): TlsPin? = null
    }

    @Test
    fun `release factory rejects cleartext before creating a channel`() {
        val factory = TlsTransportFactory(pins)

        assertFalse(TlsVariantPolicy.allowCleartext)
        assertThrows(IllegalArgumentException::class.java) {
            factory.createGrpcChannel("http://localhost:8080")
        }
    }

    @Test
    fun `release OkHttp keeps its default hostname verifier`() {
        val factory = TlsTransportFactory(pins)
        val defaultVerifier = OkHttpClient.Builder().build().hostnameVerifier

        val client = factory.createOkHttpClient("https://files.example.test/media".toHttpUrl())

        assertSame(defaultVerifier, client.hostnameVerifier)
    }
}
