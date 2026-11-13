package com.barkfluff.client.bft

import okhttp3.Dns
import java.io.Closeable
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketAddress
import java.net.SocketException
import java.util.concurrent.atomic.AtomicBoolean
import javax.net.SocketFactory

/** Byte streams supplied by the future BFT multiplexer for one virtual connection. */
class BftSocketStreams(
    val input: InputStream,
    val output: OutputStream,
    internal val tlsSpkiPin: String? = null,
    private val closeAction: (() -> Unit)? = null
) : Closeable {
    constructor(input: InputStream, output: OutputStream, closeAction: (() -> Unit)?) :
        this(input, output, tlsSpkiPin = null, closeAction = closeAction)

    private var setReadTimeoutAction: ((Int) -> Unit)? = null
    private val closed = AtomicBoolean()

    internal fun onReadTimeoutChanged(action: (Int) -> Unit) {
        setReadTimeoutAction = action
    }

    fun setReadTimeout(timeoutMillis: Int) {
        require(timeoutMillis >= 0) { "timeout must be non-negative" }
        setReadTimeoutAction?.invoke(timeoutMillis)
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        try {
            input.close()
        } finally {
            try {
                output.close()
            } finally {
                closeAction?.invoke()
            }
        }
    }
}

/** Creates Socket instances whose connected I/O is backed by caller-supplied byte streams. */
class BftSocketFactory(
    private val openStreams: () -> BftSocketStreams
) : SocketFactory() {
    constructor(tunnel: BftTunnel, serviceId: Int) : this({ tunnel.openStream(serviceId).asSocketStreams() })

    override fun createSocket(): Socket = BftVirtualSocket(openStreams)

    override fun createSocket(host: String, port: Int): Socket = connect(InetSocketAddress(host, port))

    override fun createSocket(
        host: String,
        port: Int,
        localHost: InetAddress,
        localPort: Int
    ): Socket = connect(InetSocketAddress(host, port))

    override fun createSocket(address: InetAddress, port: Int): Socket =
        connect(InetSocketAddress(address, port))

    override fun createSocket(
        address: InetAddress,
        port: Int,
        localAddress: InetAddress,
        localPort: Int
    ): Socket = connect(InetSocketAddress(address, port))

    private fun connect(address: SocketAddress): Socket = createSocket().apply { connect(address) }
}

/** Maps any origin name to a caller-selected address without consulting system DNS. */
class BftDns(
    private val address: InetAddress
) : Dns {
    override fun lookup(hostname: String): List<InetAddress> = listOf(address)
}

internal class BftVirtualSocket(
    private val openStreams: () -> BftSocketStreams
) : Socket() {
    private var streams: BftSocketStreams? = null
    private var remoteAddress: InetSocketAddress? = null
    private var closed = false
    private var inputShutdown = false
    private var outputShutdown = false
    private var soTimeoutMillis = 0

    internal val tlsSpkiPin: String?
        get() = streams?.tlsSpkiPin

    @Synchronized
    override fun connect(endpoint: SocketAddress, timeout: Int) {
        if (closed) throw SocketException("Socket is closed")
        if (streams != null) throw SocketException("Socket is already connected")
        val address = endpoint as? InetSocketAddress
            ?: throw SocketException("BFT virtual sockets require an Internet endpoint")

        streams = openStreams().also { it.setReadTimeout(soTimeoutMillis) }
        remoteAddress = address
    }

    override fun getInputStream(): InputStream {
        if (closed) throw SocketException("Socket is closed")
        if (inputShutdown) throw SocketException("Socket input is shut down")
        return streams?.input ?: throw SocketException("Socket is not connected")
    }

    override fun getOutputStream(): OutputStream {
        if (closed) throw SocketException("Socket is closed")
        if (outputShutdown) throw SocketException("Socket output is shut down")
        return streams?.output ?: throw SocketException("Socket is not connected")
    }

    @Synchronized
    override fun close() {
        if (closed) return
        closed = true
        streams?.close()
    }

    override fun shutdownInput() {
        if (inputShutdown) return
        inputShutdown = true
        streams?.input?.close()
    }

    override fun shutdownOutput() {
        if (outputShutdown) return
        outputShutdown = true
        streams?.output?.close()
    }

    override fun isConnected(): Boolean = streams != null
    override fun isBound(): Boolean = streams != null
    override fun isClosed(): Boolean = closed
    override fun isInputShutdown(): Boolean = inputShutdown
    override fun isOutputShutdown(): Boolean = outputShutdown
    override fun getInetAddress(): InetAddress? = remoteAddress?.address ?: InetAddress.getLoopbackAddress()
    override fun getPort(): Int = remoteAddress?.port ?: 0
    override fun getRemoteSocketAddress(): SocketAddress? = remoteAddress
    override fun getLocalAddress(): InetAddress = InetAddress.getLoopbackAddress()
    override fun getLocalPort(): Int = 0
    override fun getLocalSocketAddress(): SocketAddress = InetSocketAddress(getLocalAddress(), localPort)

    override fun setSoTimeout(timeout: Int) {
        require(timeout >= 0) { "timeout must be non-negative" }
        soTimeoutMillis = timeout
        streams?.setReadTimeout(timeout)
    }

    override fun getSoTimeout(): Int = soTimeoutMillis
    override fun setTcpNoDelay(on: Boolean) = Unit
    override fun getTcpNoDelay(): Boolean = false
    override fun setKeepAlive(on: Boolean) = Unit
    override fun getKeepAlive(): Boolean = false
}
