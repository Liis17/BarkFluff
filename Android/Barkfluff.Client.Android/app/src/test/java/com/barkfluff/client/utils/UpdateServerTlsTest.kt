package com.barkfluff.client.utils

import java.io.IOException
import java.net.SocketTimeoutException
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.CancellationException
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLHandshakeException
import javax.net.ssl.X509TrustManager
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.fail
import org.junit.Test

class UpdateServerTlsTest {

    private val embeddedTrust = UpdateServerTls.Trust(
        socketFactory = SSLContext.getDefault().socketFactory,
        trustManager = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) = Unit
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        }
    )

    @Test
    fun `successful system attempt does not resolve embedded trust`() = runBlocking {
        var providerCalls = 0
        var attempts = 0

        val result = UpdateServerTls.withFallback(
            embeddedTrustProvider = {
                providerCalls++
                embeddedTrust
            },
            operation = { trust ->
                assertNull(trust)
                attempts++
                "ok"
            }
        )

        assertEquals("ok", result)
        assertEquals(0, providerCalls)
        assertEquals(1, attempts)
    }

    @Test
    fun `TLS failure retries once with embedded trust`() = runBlocking {
        val trusts = mutableListOf<UpdateServerTls.Trust?>()

        val result = UpdateServerTls.withFallback(
            embeddedTrustProvider = { embeddedTrust },
            operation = { trust ->
                trusts += trust
                if (trust == null) throw SSLHandshakeException("untrusted system CA")
                "fallback-ok"
            }
        )

        assertEquals("fallback-ok", result)
        assertEquals(listOf(null, embeddedTrust), trusts)
    }

    @Test
    fun `nested TLS failure retries with embedded trust`() = runBlocking {
        var attempts = 0

        val result = UpdateServerTls.withFallback(
            embeddedTrustProvider = { embeddedTrust },
            operation = { trust ->
                attempts++
                if (trust == null) {
                    throw IOException("request failed", CertificateException("certificate rejected"))
                }
                "nested-fallback-ok"
            }
        )

        assertEquals("nested-fallback-ok", result)
        assertEquals(2, attempts)
    }

    @Test
    fun `ordinary network failure is not retried`() = runBlocking {
        val original = SocketTimeoutException("timeout")
        var attempts = 0

        try {
            UpdateServerTls.withFallback(
                embeddedTrustProvider = { fail("embedded trust must not be resolved") },
                operation = {
                    attempts++
                    throw original
                }
            )
            fail("Expected the original exception")
        } catch (actual: SocketTimeoutException) {
            assertSame(original, actual)
        }

        assertEquals(1, attempts)
    }

    @Test
    fun `missing embedded trust preserves the original TLS failure`() = runBlocking {
        val original = SSLHandshakeException("untrusted certificate")
        var attempts = 0

        try {
            UpdateServerTls.withFallback(
                embeddedTrustProvider = { null },
                operation = {
                    attempts++
                    throw original
                }
            )
            fail("Expected the original exception")
        } catch (actual: SSLHandshakeException) {
            assertSame(original, actual)
        }

        assertEquals(1, attempts)
    }

    @Test
    fun `fallback failure is propagated`() = runBlocking {
        val fallbackFailure = IOException("fallback failed")
        var attempts = 0

        try {
            UpdateServerTls.withFallback(
                embeddedTrustProvider = { embeddedTrust },
                operation = { trust ->
                    attempts++
                    if (trust == null) throw SSLHandshakeException("system failed")
                    throw fallbackFailure
                }
            )
            fail("Expected the fallback exception")
        } catch (actual: IOException) {
            assertSame(fallbackFailure, actual)
        }

        assertEquals(2, attempts)
    }

    @Test
    fun `cancellation is not retried`() = runBlocking {
        val original = CancellationException("cancelled")
        var attempts = 0

        try {
            UpdateServerTls.withFallback(
                embeddedTrustProvider = { fail("embedded trust must not be resolved") },
                operation = {
                    attempts++
                    throw original
                }
            )
            fail("Expected cancellation")
        } catch (actual: CancellationException) {
            assertSame(original, actual)
        }

        assertEquals(1, attempts)
    }
}
