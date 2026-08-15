package com.barkfluff.client.security

import org.junit.Assert.assertFalse
import org.junit.Test
import java.security.Principal
import java.security.cert.Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLSession

class TlsHostnameVerifierTest {
    @Test
    fun `hostname mismatch is rejected even when a certificate is otherwise pinned`() {
        val certificate = TestCertificate("shared-key")
        PinnedTrustManager(
            "files.example.test",
            object : TlsPinLookup {
                override fun pinFor(host: String): TlsPin? = TlsPin(
                    host,
                    TlsCertificate.spkiSha256(certificate),
                    0
                )
            },
            RecordingTrustManager()
        ).checkServerTrusted(arrayOf(certificate), "RSA")

        assertFalse(
            HttpsURLConnection.getDefaultHostnameVerifier()
                .verify("other.example.test", CertificateSession(certificate))
        )
    }

    private class CertificateSession(private val certificate: Certificate) : SSLSession {
        override fun getId(): ByteArray = byteArrayOf()
        override fun getSessionContext() = null
        override fun getCreationTime(): Long = 0
        override fun getLastAccessedTime(): Long = 0
        override fun invalidate() = Unit
        override fun isValid(): Boolean = true
        override fun putValue(name: String, value: Any) = Unit
        override fun getValue(name: String): Any? = null
        override fun removeValue(name: String) = Unit
        override fun getValueNames(): Array<String> = emptyArray()
        override fun getPeerCertificates(): Array<Certificate> = arrayOf(certificate)
        override fun getLocalCertificates(): Array<Certificate>? = null
        @Suppress("DEPRECATION")
        override fun getPeerCertificateChain(): Array<javax.security.cert.X509Certificate> = emptyArray()
        override fun getPeerPrincipal(): Principal = (certificate as java.security.cert.X509Certificate).subjectX500Principal
        override fun getLocalPrincipal(): Principal? = null
        override fun getCipherSuite(): String = "TLS_AES_128_GCM_SHA256"
        override fun getProtocol(): String = "TLSv1.3"
        override fun getPeerHost(): String = "files.example.test"
        override fun getPeerPort(): Int = 443
        override fun getPacketBufferSize(): Int = 16_384
        override fun getApplicationBufferSize(): Int = 16_384
    }
}

private class RecordingTrustManager : javax.net.ssl.X509TrustManager {
    override fun checkClientTrusted(chain: Array<java.security.cert.X509Certificate>, authType: String) = Unit
    override fun checkServerTrusted(chain: Array<java.security.cert.X509Certificate>, authType: String) = Unit
    override fun getAcceptedIssuers(): Array<java.security.cert.X509Certificate> = emptyArray()
}
