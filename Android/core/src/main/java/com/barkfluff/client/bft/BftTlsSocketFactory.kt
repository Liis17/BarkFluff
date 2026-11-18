package com.barkfluff.client.bft

import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketException
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.Base64
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.X509TrustManager

/** Layers pinned TLS over a BFT virtual socket; ALPN is selected by the HTTP/gRPC client. */
class BftTlsSocketFactory(
    spkiPin: String,
    private val virtualSocketFactory: BftSocketFactory? = null,
    private val tlsHostOverride: String? = null
) : SSLSocketFactory() {
    val trustManager: X509TrustManager = BftPinnedTrustManager(spkiPin)
    private val delegate: SSLSocketFactory

    init {
        val context = SSLContext.getInstance("TLS")
        context.init(null, arrayOf(trustManager), null)
        delegate = context.socketFactory
    }

    override fun createSocket(socket: Socket, host: String, port: Int, autoClose: Boolean): Socket {
        val socketToWrap = if (socket is BftVirtualSocket) {
            socket
        } else {
            val virtualSocket = createVirtualSocket(InetSocketAddress.createUnresolved(host, port))
            if (autoClose) socket.close()
            virtualSocket
        }
        return createTlsSocket(socketToWrap, host, port, socketToWrap !== socket || autoClose)
    }

    override fun createSocket(): Socket = directSocketNotSupported()
    override fun createSocket(host: String, port: Int): Socket =
        createDirectSocket(InetSocketAddress.createUnresolved(host, port), host, port)
    override fun createSocket(
        host: String,
        port: Int,
        localHost: InetAddress,
        localPort: Int
    ): Socket = createDirectSocket(InetSocketAddress.createUnresolved(host, port), host, port)
    override fun createSocket(host: InetAddress, port: Int): Socket =
        createDirectSocket(InetSocketAddress(host, port), host.hostAddress, port)
    override fun createSocket(
        address: InetAddress,
        port: Int,
        localAddress: InetAddress,
        localPort: Int
    ): Socket = createDirectSocket(InetSocketAddress(address, port), address.hostAddress, port)

    private fun createDirectSocket(endpoint: InetSocketAddress, host: String, port: Int): Socket =
        createTlsSocket(createVirtualSocket(endpoint), host, port, autoClose = true)

    private fun createVirtualSocket(endpoint: InetSocketAddress): Socket {
        val factory = virtualSocketFactory ?: throw directSocketNotSupported()
        return factory.createSocket().apply { connect(endpoint) }
    }

    private fun createTlsSocket(socket: Socket, host: String, port: Int, autoClose: Boolean): Socket {
        val tlsSocket = delegate.createSocket(socket, tlsHostOverride ?: host, port, autoClose) as SSLSocket
        tlsSocket.useClientMode = true
        val parameters = tlsSocket.sslParameters
        parameters.endpointIdentificationAlgorithm = "HTTPS"
        tlsSocket.sslParameters = parameters
        return tlsSocket
    }

    override fun getDefaultCipherSuites(): Array<String> = delegate.defaultCipherSuites
    override fun getSupportedCipherSuites(): Array<String> = delegate.supportedCipherSuites

    private fun directSocketNotSupported(): Nothing =
        throw SocketException("BFT TLS must wrap a connected virtual socket")

    companion object {
        fun spkiPin(certificate: X509Certificate): String {
            val digest = MessageDigest.getInstance("SHA-256").digest(certificate.publicKey.encoded)
            return "sha256/${Base64.getEncoder().encodeToString(digest)}"
        }
    }
}

private class BftPinnedTrustManager(
    private val expectedPin: String
) : X509TrustManager {
    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {
        throw CertificateException("BFT client certificates are not configured")
    }

    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val leaf = chain.firstOrNull() ?: throw CertificateException("Exit did not provide a certificate")
        leaf.checkValidity()
        val observedPin = BftTlsSocketFactory.spkiPin(leaf)
        if (!MessageDigest.isEqual(
                expectedPin.toByteArray(Charsets.US_ASCII),
                observedPin.toByteArray(Charsets.US_ASCII)
            )
        ) {
            throw CertificateException("Exit certificate public key does not match the configured pin")
        }
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}
