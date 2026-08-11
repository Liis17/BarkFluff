package com.barkfluff.client.security

import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

data class TlsCertificateInfo(
    val host: String,
    val subject: String,
    val expiresAtMillis: Long,
    val spkiSha256: String,
    val isSelfSigned: Boolean
)

/**
 * Reads a peer certificate with a TLS handshake only. It never sends HTTP, gRPC metadata, or a
 * token, and its permissive trust manager is scoped to this short-lived inspection socket.
 */
class TlsCertificateProbe {
    fun inspect(address: String): TlsCertificateInfo = inspect(TlsEndpoint.parseAddress(address))

    fun inspectUrl(url: String): TlsCertificateInfo = inspect(TlsEndpoint.parseUrlEndpoint(url))

    private fun inspect(endpoint: TlsEndpoint): TlsCertificateInfo {
        require(endpoint.usesTls) { "A certificate can only be inspected over TLS" }
        val trustManager = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) = Unit
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        }
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf(trustManager), null)

        val socket = sslContext.socketFactory.createSocket(endpoint.host, endpoint.port) as SSLSocket
        socket.use {
            it.soTimeout = HANDSHAKE_TIMEOUT_MS
            it.startHandshake()
            val session = it.session
            require(HttpsURLConnection.getDefaultHostnameVerifier().verify(endpoint.host, session)) {
                "Certificate hostname does not match ${endpoint.host}"
            }
            val leaf = session.peerCertificates.firstOrNull() as? X509Certificate
                ?: throw CertificateException("Server did not provide an X.509 certificate")
            leaf.checkValidity()
            return TlsCertificateInfo(
                host = endpoint.host,
                subject = leaf.subjectX500Principal.name,
                expiresAtMillis = leaf.notAfter.time,
                spkiSha256 = TlsCertificate.spkiSha256(leaf),
                isSelfSigned = leaf.isSelfSigned()
            )
        }
    }

    private fun X509Certificate.isSelfSigned(): Boolean = runCatching {
        if (subjectX500Principal != issuerX500Principal) {
            false
        } else {
            verify(publicKey)
            true
        }
    }.getOrDefault(false)

    private companion object {
        const val HANDSHAKE_TIMEOUT_MS = 5_000
    }
}
