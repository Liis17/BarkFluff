package com.barkfluff.client.security

import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigInteger
import java.security.Principal
import java.security.PublicKey
import java.security.cert.CertificateException
import java.security.cert.CertificateExpiredException
import java.security.cert.X509Certificate
import java.util.Date
import javax.security.auth.x500.X500Principal
import javax.net.ssl.X509TrustManager

class PinnedTrustManagerTest {
    private val host = "files.example.test"

    @Test
    fun `system trusted certificate without pin uses platform trust store`() {
        val platform = RecordingTrustManager()
        val manager = PinnedTrustManager(host, FakePinLookup(), platform)

        manager.checkServerTrusted(arrayOf(TestCertificate("system-key")), "RSA")

        assertTrue(platform.serverTrustedChecked)
    }

    @Test
    fun `self signed certificate without pin is rejected by platform trust store`() {
        val manager = PinnedTrustManager(host, FakePinLookup(), RecordingTrustManager(rejectServer = true))

        assertThrows(CertificateException::class.java) {
            manager.checkServerTrusted(arrayOf(TestCertificate("self-signed-key")), "RSA")
        }
    }

    @Test
    fun `confirmed self signed pin bypasses platform trust store`() {
        val certificate = TestCertificate("approved-key")
        val pin = TlsCertificate.spkiSha256(certificate)
        val manager = PinnedTrustManager(
            host,
            FakePinLookup(host to pin),
            RecordingTrustManager(rejectServer = true)
        )

        manager.checkServerTrusted(arrayOf(certificate), "RSA")
    }

    @Test
    fun `a new SPKI for a pinned hostname is blocked`() {
        val approved = TestCertificate("approved-key")
        val manager = PinnedTrustManager(
            host,
            FakePinLookup(host to TlsCertificate.spkiSha256(approved)),
            RecordingTrustManager()
        )

        assertThrows(CertificateException::class.java) {
            manager.checkServerTrusted(arrayOf(TestCertificate("replacement-key")), "RSA")
        }
    }

    @Test
    fun `pin for another hostname does not authorize this host`() {
        val manager = PinnedTrustManager(
            host,
            FakePinLookup("beacon.example.test" to TlsCertificate.spkiSha256(TestCertificate("shared-key"))),
            RecordingTrustManager(rejectServer = true)
        )

        assertThrows(CertificateException::class.java) {
            manager.checkServerTrusted(arrayOf(TestCertificate("shared-key")), "RSA")
        }
    }

    @Test
    fun `expired certificate is rejected even when its SPKI is pinned`() {
        val certificate = TestCertificate("expired-key", valid = false)
        val manager = PinnedTrustManager(
            host,
            FakePinLookup(host to TlsCertificate.spkiSha256(certificate)),
            RecordingTrustManager()
        )

        assertThrows(CertificateExpiredException::class.java) {
            manager.checkServerTrusted(arrayOf(certificate), "RSA")
        }
    }

    private class FakePinLookup(vararg pins: Pair<String, String>) : TlsPinLookup {
        private val pinsByHost = pins.associate { (pinHost, pin) ->
            pinHost to TlsPin(pinHost, pin, 0)
        }

        override fun pinFor(host: String): TlsPin? = pinsByHost[host]
    }

    private class RecordingTrustManager(
        private val rejectServer: Boolean = false
    ) : X509TrustManager {
        var serverTrustedChecked = false

        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit

        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            serverTrustedChecked = true
            if (rejectServer) throw CertificateException("Untrusted test certificate")
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }
}

internal class TestCertificate(
    keyMaterial: String,
    private val valid: Boolean = true
) : X509Certificate() {
    private val key = TestPublicKey(keyMaterial)
    private val principal = X500Principal("CN=files.example.test")

    override fun checkValidity() {
        if (!valid) throw CertificateExpiredException("Expired test certificate")
    }

    override fun checkValidity(date: Date) = checkValidity()
    override fun getVersion(): Int = 3
    override fun getSerialNumber(): BigInteger = BigInteger.ONE
    override fun getIssuerDN(): Principal = principal
    override fun getSubjectDN(): Principal = principal
    override fun getIssuerX500Principal(): X500Principal = principal
    override fun getSubjectX500Principal(): X500Principal = principal
    override fun getNotBefore(): Date = Date(0)
    override fun getNotAfter(): Date = Date(Long.MAX_VALUE)
    override fun getTBSCertificate(): ByteArray = byteArrayOf()
    override fun getSignature(): ByteArray = byteArrayOf()
    override fun getSigAlgName(): String = "NONE"
    override fun getSigAlgOID(): String = "0.0"
    override fun getSigAlgParams(): ByteArray? = null
    override fun getIssuerUniqueID(): BooleanArray? = null
    override fun getSubjectUniqueID(): BooleanArray? = null
    override fun getKeyUsage(): BooleanArray? = null
    override fun getBasicConstraints(): Int = -1
    override fun getEncoded(): ByteArray = key.encoded
    override fun verify(key: PublicKey) = Unit
    override fun verify(key: PublicKey, sigProvider: String) = Unit
    override fun toString(): String = "TestCertificate"
    override fun getPublicKey(): PublicKey = key
    override fun hasUnsupportedCriticalExtension(): Boolean = false
    override fun getCriticalExtensionOIDs(): MutableSet<String>? = null
    override fun getNonCriticalExtensionOIDs(): MutableSet<String>? = null
    override fun getExtensionValue(oid: String): ByteArray? = null
}

private class TestPublicKey(private val material: String) : PublicKey {
    override fun getAlgorithm(): String = "RSA"
    override fun getFormat(): String = "X.509"
    override fun getEncoded(): ByteArray = material.toByteArray()
}
