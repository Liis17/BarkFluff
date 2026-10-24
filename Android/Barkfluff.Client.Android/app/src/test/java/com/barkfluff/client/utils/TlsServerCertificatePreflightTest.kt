package com.barkfluff.client.utils

import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.security.TlsCertificateInfo
import com.barkfluff.client.security.TlsPin
import java.io.IOException
import java.security.cert.CertificateException
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class TlsServerCertificatePreflightTest {

    @Test
    fun probesEndpointsConcurrently() = runBlocking {
        val preflight = TlsServerCertificatePreflight(pinFor = { null }) { url ->
            Thread.sleep(300)
            certificate(host(url))
        }

        val started = System.nanoTime()
        assertNull(preflight.approvalRequired(serverInfo()))

        val elapsedMs = (System.nanoTime() - started) / 1_000_000
        assertTrue("10 handshakes of 300 ms must overlap, took $elapsedMs ms", elapsedMs < 1_500)
    }

    @Test
    fun returnsFirstEndpointNeedingApprovalInEndpointOrder() = runBlocking {
        val preflight = TlsServerCertificatePreflight(pinFor = { null }) { url ->
            val host = host(url)
            // The later endpoint answers first; the result must still follow endpoint order.
            if (host == "identity.test") Thread.sleep(200)
            certificate(host, selfSigned = host == "identity.test" || host == "users.test")
        }

        assertEquals("identity.test", preflight.approvalRequired(serverInfo())?.host)
    }

    @Test
    fun skipsUnreachableAndPinnedEndpoints() = runBlocking {
        val pins = mapOf("identity.test" to TlsPin("identity.test", "pin:identity.test", 0))
        val preflight = TlsServerCertificatePreflight(pinFor = pins::get) { url ->
            when (val host = host(url)) {
                "users.test" -> throw IOException("unreachable")
                else -> certificate(host, selfSigned = host == "identity.test")
            }
        }

        assertNull(preflight.approvalRequired(serverInfo()))
    }

    @Test
    fun invalidCertificateRejectsServer() {
        val preflight = TlsServerCertificatePreflight(pinFor = { null }) { url ->
            if (host(url) == "files.test") throw CertificateException("expired")
            certificate(host(url))
        }

        val error = assertThrows(TlsEndpointSecurityException::class.java) {
            runBlocking { preflight.approvalRequired(serverInfo()) }
        }
        assertTrue(error.message.orEmpty().contains("files.test"))
    }

    private fun host(url: String) = url.substringAfter("://").substringBefore(':')

    private fun certificate(host: String, selfSigned: Boolean = false) =
        TlsCertificateInfo(host, "CN=$host", 0, "pin:$host", selfSigned)

    private fun serverInfo() = ServerInfo(
        name = "test",
        description = "",
        color = ClientColors(),
        identityEndpoint = "https://identity.test:443",
        usersEndpoint = "https://users.test:443",
        filesEndpoint = "https://files.test:443",
        messagesEndpoint = "https://messages.test:443",
        updatesEndpoint = "https://updates.test:443",
        onlinerEndpoint = "https://onliner.test:443",
        fastAuthEndpoint = "https://fastauth.test:443",
        callsEndpoint = "https://calls.test:443",
        livekitUrl = "https://livekit.test:443",
        filesMediaEndpoint = "https://media.test:443",
    )
}
