package com.barkfluff.client.bft

import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.util.concurrent.atomic.AtomicBoolean

class BftTunnelStream internal constructor(
    private val connection: BftMuxConnection,
    internal val state: BftMuxStreamState
) : Closeable {
    private val disposed = AtomicBoolean(false)
    private val outputClosed = AtomicBoolean(false)

    val streamId: Long get() = state.streamId
    val serviceId: Int get() = state.serviceId
    val canRetryAfterGoAway: Boolean get() = connection.isRetryableAfterGoAway(state)
    val input: InputStream = StreamInput()
    val output: OutputStream = StreamOutput()

    fun reset(code: BftResetCode = BftResetCode.CANCEL) {
        ensureNotDisposed()
        connection.reset(state, code)
    }

    fun completeWrites() {
        ensureNotDisposed()
        closeOutput()
    }

    fun setReadTimeout(timeoutMillis: Int) {
        ensureNotDisposed()
        connection.setReadTimeout(state, timeoutMillis)
    }

    internal fun asSocketStreams(tlsSpkiPin: String? = null, onClosed: (() -> Unit)? = null): BftSocketStreams =
        BftSocketStreams(input, output, tlsSpkiPin = tlsSpkiPin, closeAction = {
            try {
                close()
            } finally {
                onClosed?.invoke()
            }
        }).also {
            it.onReadTimeoutChanged(::setReadTimeout)
        }

    override fun close() {
        if (!disposed.compareAndSet(false, true)) return
        connection.disposeStream(state)
    }

    private fun closeOutput() {
        if (outputClosed.compareAndSet(false, true)) connection.completeWrite(state)
    }

    private fun ensureNotDisposed() {
        if (disposed.get()) throw IOException("BFT stream is closed")
    }

    private inner class StreamInput : InputStream() {
        override fun read(): Int {
            ensureNotDisposed()
            val one = ByteArray(1)
            val count = connection.read(state, one, 0, 1)
            return if (count < 0) -1 else one[0].toInt() and 0xff
        }

        override fun read(bytes: ByteArray, offset: Int, length: Int): Int {
            ensureNotDisposed()
            if (offset < 0 || length < 0 || length > bytes.size - offset) throw IndexOutOfBoundsException()
            return connection.read(state, bytes, offset, length)
        }

        override fun available(): Int {
            ensureNotDisposed()
            return connection.available(state)
        }

        override fun close() = this@BftTunnelStream.close()
    }

    private inner class StreamOutput : OutputStream() {
        override fun write(value: Int) {
            ensureNotDisposed()
            val one = byteArrayOf(value.toByte())
            connection.write(state, one, 0, 1)
        }

        override fun write(bytes: ByteArray, offset: Int, length: Int) {
            ensureNotDisposed()
            if (offset < 0 || length < 0 || length > bytes.size - offset) throw IndexOutOfBoundsException()
            connection.write(state, bytes, offset, length)
        }

        override fun flush() {
            ensureNotDisposed()
        }

        override fun close() = closeOutput()
    }
}

class BftServiceUnavailableException(val service: Int) : IOException("BFT service $service is unavailable")
class BftStreamLimitException : IOException("BFT tunnel stream limit was reached")
class BftGoAwayException(val lastStreamId: Long, val retryable: Boolean) : IOException("Exit sent GOAWAY at stream $lastStreamId")
