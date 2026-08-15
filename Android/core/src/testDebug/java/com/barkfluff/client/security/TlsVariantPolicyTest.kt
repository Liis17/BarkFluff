package com.barkfluff.client.security

import io.grpc.okhttp.OkHttpChannelBuilder
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TlsVariantPolicyTest {
    private val pins = object : TlsPinLookup {
        override fun pinFor(host: String): TlsPin? = null
    }

    @Test
    fun `debug keeps the local self signed TLS transport`() {
        val trustManager = TlsVariantPolicy.socketConfig("localhost", pins).trustManager

        trustManager.checkServerTrusted(arrayOf(TestCertificate("debug-key")), "RSA")
    }

    @Test
    fun `debug creates an h2c channel for a local endpoint`() {
        val endpoint = TlsEndpoint.parseAddress("http://localhost:8080", TlsVariantPolicy.allowCleartext)

        val channel = TlsVariantPolicy.configureGrpcBuilder(
            OkHttpChannelBuilder.forAddress(endpoint.host, endpoint.port),
            endpoint,
            pins
        ).build()
        channel.shutdownNow()

        assertTrue(TlsVariantPolicy.allowCleartext)
        assertEquals(TlsEndpoint.HTTP, endpoint.scheme)
    }
}
