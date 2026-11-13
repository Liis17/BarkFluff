package com.barkfluff.client.bft

import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.Socket
import java.util.concurrent.CompletableFuture

data class BftGoAway(val lastStreamId: Long, val code: BftGoAwayCode)

data class BftTunnelCloseReason(
    val kind: Kind,
    val cause: Throwable?,
    val goAway: BftGoAway? = null
) {
    enum class Kind { NETWORK_ERROR, AUTH_FAILED, GOAWAY, PROTOCOL_ERROR, LOCAL_CLOSE }
}

class BftTunnelOptions @JvmOverloads constructor(
    val randomSource: BftRandomSource = BftSystemRandomSource,
    val handshakeTimeoutMillis: Long = 10_000,
    val helloTimeoutMillis: Long = 10_000,
    val keepAliveIdleMillis: Long = 30_000,
    val keepAliveTimeoutMillis: Long = 20_000,
    val onStateChanged: ((BftTunnel.State, BftTunnelCloseReason?) -> Unit)? = null
) {
    init {
        require(handshakeTimeoutMillis in 1..Int.MAX_VALUE.toLong())
        require(helloTimeoutMillis > 0 && keepAliveIdleMillis > 0 && keepAliveTimeoutMillis > 0)
    }
}

class BftTunnel private constructor(
    private val connection: BftMuxConnection,
    val hello: BftHello
) : Closeable {
    enum class State { CONNECTING, READY, CLOSED }

    val state: State get() = connection.state
    val closeReason: BftTunnelCloseReason? get() = connection.terminalReason
    val goAway: BftGoAway? get() = connection.goAway
    val completion: CompletableFuture<BftTunnelCloseReason> get() = connection.completion

    fun openStream(serviceId: Int): BftTunnelStream = connection.openStream(serviceId)

    override fun close() = connection.close()

    companion object {
        @JvmStatic
        @JvmOverloads
        fun connect(socket: Socket, connectionString: String, options: BftTunnelOptions = BftTunnelOptions()): BftTunnel {
            require(socket.isConnected && !socket.isClosed) { "BFT socket must already be connected" }
            val previousTimeout = socket.soTimeout
            socket.soTimeout = options.handshakeTimeoutMillis.toInt()
            return try {
                connect(
                    socket.getInputStream(), socket.getOutputStream(), connectionString, options,
                    closeTransport = { socket.close() }, timeSource = BftSystemTimeSource, handshakeOptions = BftClientHandshakeOptions()
                )
            } finally {
                if (!socket.isClosed) socket.soTimeout = previousTimeout
            }
        }

        @JvmStatic
        @JvmOverloads
        fun connect(
            input: InputStream,
            output: OutputStream,
            connectionString: String,
            options: BftTunnelOptions = BftTunnelOptions()
        ): BftTunnel {
            return connect(
                input, output, connectionString, options,
                closeTransport = {
                    runCatching { input.close() }
                    runCatching { output.close() }
                },
                timeSource = BftSystemTimeSource,
                handshakeOptions = BftClientHandshakeOptions()
            )
        }

        internal fun connect(
            input: InputStream,
            output: OutputStream,
            connectionString: String,
            options: BftTunnelOptions,
            closeTransport: () -> Unit,
            timeSource: BftTimeSource,
            handshakeOptions: BftClientHandshakeOptions
        ): BftTunnel {
            options.onStateChanged?.invoke(State.CONNECTING, null)
            var mux: BftMuxConnection? = null
            var settings: BftConnectionSettings? = null
            try {
                val parsedSettings = BftConnectionString.parse(connectionString)
                settings = parsedSettings
                val session = BftClientHandshake.connect(input, output, parsedSettings, handshakeOptions.copy(random = options.randomSource))
                mux = BftMuxConnection(
                    session = session,
                    closeTransport = closeTransport,
                    random = options.randomSource,
                    time = timeSource,
                    helloTimeoutMillis = options.helloTimeoutMillis,
                    keepAliveIdleMillis = options.keepAliveIdleMillis,
                    keepAliveTimeoutMillis = options.keepAliveTimeoutMillis,
                    connected = { options.onStateChanged?.invoke(State.READY, null) }
                )
                val hello = mux.start(parsedSettings.token)
                mux.completion.thenAccept { options.onStateChanged?.invoke(State.CLOSED, it) }
                return BftTunnel(mux, hello)
            } catch (error: Throwable) {
                val reason = mux?.terminalReason ?: BftTunnelCloseReason(
                    if (error is BftProtocolException || error is BftRecordException) BftTunnelCloseReason.Kind.PROTOCOL_ERROR
                    else BftTunnelCloseReason.Kind.NETWORK_ERROR,
                    error
                )
                mux?.close()
                runCatching(closeTransport)
                options.onStateChanged?.invoke(State.CLOSED, reason)
                throw BftTunnelConnectException("Unable to connect BFT tunnel", reason, error)
            } finally {
                settings?.staticPublicKey?.fill(0)
                settings?.obfsKey?.fill(0)
            }
        }
    }
}

class BftTunnelConnectException(
    message: String,
    val closeReason: BftTunnelCloseReason,
    cause: Throwable
) : IOException(message, cause)
