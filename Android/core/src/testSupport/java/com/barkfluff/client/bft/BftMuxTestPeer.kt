package com.barkfluff.client.bft

import java.io.Closeable
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.PipedInputStream
import java.io.PipedOutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketException
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

internal class BftMuxTestPeer(
    private val timeSource: BftTimeSource = BftSystemTimeSource,
    private val keepAliveIdleMillis: Long = 30_000,
    private val keepAliveTimeoutMillis: Long = 20_000,
    private val upstreamPort: Int? = null,
    private val upstreamPorts: Map<Int, Int> = emptyMap(),
    tlsSpkiSha256Override: ByteArray? = null
) : Closeable {
    private val tlsSpkiSha256Override = tlsSpkiSha256Override?.copyOf()
    private val clientInput = PipedInputStream(256 * 1024)
    private val serverOutput = PipedOutputStream(clientInput)
    private val serverInput = PipedInputStream(256 * 1024)
    private val clientOutput = PipedOutputStream(serverInput)
    private val incoming = LinkedBlockingQueue<BftFrame>()
    private val closed = AtomicBoolean(false)
    private val writerLock = Any()
    private val ready = java.util.concurrent.CountDownLatch(1)
    private val outbound = LinkedBlockingQueue<ByteArray>()
    private val proxiedStreams = ConcurrentHashMap<Long, ProxiedStream>()
    private val upstreamThreads = java.util.concurrent.CopyOnWriteArrayList<Thread>()
    private val serverThread = Thread(::serve, "bft-test-peer").apply { isDaemon = true }
    @Volatile private var outboundWriterThread: Thread? = null
    @Volatile private var failure: Throwable? = null
    @Volatile private var keys: BftObfuscationKeys? = null
    @Volatile private var outboundNoise: BftNoiseCipherState? = null
    @Volatile private var outboundRecords: BftObfuscatedRecordWriter? = null

    init {
        serverThread.start()
    }

    fun connect(onStateChanged: ((BftTunnel.State, BftTunnelCloseReason?) -> Unit)? = null): BftTunnel {
        val connection = BftVectors.objectAt(BftVectors.root, "connectionString")
        val handshake = BftVectors.objectAt(BftVectors.root, "handshake")
        val keyVector = BftVectors.objectAt(BftVectors.root, "obfsKeys")
        val settings = BftConnectionSettings(
            BftVectors.hexAt(connection, "staticPublicKey"), BftVectors.hexAt(connection, "obfsKey"),
            "proxy.example.org", 8443
        )
        val handshakeOptions = BftClientHandshakeOptions(
            random = ZeroRandom,
            timestampSeconds = BftVectors.longAt(handshake, "timestamp"),
            salt = BftVectors.hexAt(keyVector, "salt"),
            ephemeralPrivateKey = BftVectors.hexAt(handshake, "clientEphemeralPrivate"),
            firstRecordPaddingLength = BftVectors.intAt(handshake, "clientPadLen")
        )
        return BftTunnel.connect(
            clientInput, clientOutput, settings.format(),
            BftTunnelOptions(
                randomSource = ZeroRandom,
                helloTimeoutMillis = 5_000,
                keepAliveIdleMillis = keepAliveIdleMillis,
                keepAliveTimeoutMillis = keepAliveTimeoutMillis,
                onStateChanged = onStateChanged
            ),
            closeTransport = ::close,
            timeSource = timeSource,
            handshakeOptions = handshakeOptions
        )
    }

    fun send(vararg frames: BftFrame) {
        check(ready.await(2, TimeUnit.SECONDS)) { "BFT test peer did not complete the handshake" }
        check(failure == null) { "BFT test peer failed" }
        val plaintext = BftFrameCodec.encodeMany(frames.toList())
        synchronized(writerLock) {
            if (closed.get()) {
                plaintext.fill(0)
                check(!closed.get()) { "BFT test peer is closed" }
            }
            outbound.put(plaintext)
        }
    }

    fun receive(timeoutMillis: Long = 2_000): BftFrame {
        val frame = incoming.poll(timeoutMillis, TimeUnit.MILLISECONDS)
        if (frame != null) return frame
        failure?.let { throw AssertionError("BFT test peer failed", it) }
        throw AssertionError("Timed out waiting for a client BFT frame")
    }

    fun receiveUntil(timeoutMillis: Long = 2_000, predicate: (BftFrame) -> Boolean): BftFrame {
        val deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMillis)
        while (true) {
            val remaining = TimeUnit.NANOSECONDS.toMillis(deadline - System.nanoTime()).coerceAtLeast(1)
            val frame = receive(remaining)
            if (predicate(frame)) return frame
            frame.body.fill(0)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        runCatching { clientInput.close() }
        runCatching { clientOutput.close() }
        runCatching { serverInput.close() }
        runCatching { serverOutput.close() }
        serverThread.interrupt()
        outboundWriterThread?.interrupt()
        if (serverThread !== Thread.currentThread()) runCatching { serverThread.join(1_000) }
        outboundNoise?.close()
        keys?.close()
        tlsSpkiSha256Override?.fill(0)
        proxiedStreams.values.forEach { runCatching { it.socket.close() }; synchronized(it.monitor) { it.monitor.notifyAll() } }
        upstreamThreads.forEach { it.interrupt() }
        upstreamThreads.forEach { if (it !== Thread.currentThread()) runCatching { it.join(1_000) } }
        while (incoming.isNotEmpty()) incoming.poll()?.body?.fill(0)
    }

    private fun serve() {
        var clientNoise: BftNoiseCipherState? = null
        try {
            val salt = ByteArray(32)
            readExactly(serverInput, salt)
            val connection = BftVectors.objectAt(BftVectors.root, "connectionString")
            val key = BftVectors.hexAt(connection, "obfsKey")
            val derived = BftObfuscationKeys.derive(key, salt)
            keys = derived
            val firstRecordReader = BftObfuscatedRecordReader(serverInput, derived, clientToServer = true)
            val firstPayload = firstRecordReader.read() ?: throw EOFException("Missing vector client flight")
            firstPayload.fill(0)
            salt.fill(0)

            val handshake = BftVectors.objectAt(BftVectors.root, "handshake")
            val transport = BftVectors.arrayAt(BftVectors.root, "transport").map { it as Map<String, Any?> }
            val serverFirstRecord = BftVectors.hexAt(handshake, "serverFirstRecord")
            val helloRecord = BftVectors.hexAt(transport.single {
                BftVectors.stringAt(it, "direction") == "s2c" && BftVectors.longAt(it, "noiseN") == 0L
            }, "record")
            val c2s = BftVectors.hexAt(handshake, "c2sKey")
            val s2c = BftVectors.hexAt(handshake, "s2cKey")
            val helloRecordToSend = tlsSpkiSha256Override?.let { pin ->
                rewriteHelloPin(helloRecord, derived, s2c, pin)
            } ?: helloRecord
            serverOutput.write(serverFirstRecord)
            serverOutput.write(helloRecordToSend)
            serverOutput.flush()
            serverFirstRecord.fill(0)
            helloRecord.fill(0)
            if (helloRecordToSend !== helloRecord) helloRecordToSend.fill(0)

            clientNoise = BftNoiseCipherState(c2s)
            outboundNoise = BftNoiseCipherState(s2c, initialCounter = 1)
            c2s.fill(0)
            s2c.fill(0)
            outboundRecords = BftObfuscatedRecordWriter(serverOutput, derived, clientToServer = false, random = ZeroRandom, initialSequence = 2)
            outboundWriterThread = Thread(::writeOutbound, "bft-test-peer-writer").apply {
                isDaemon = true
                start()
            }
            val reader = BftObfuscatedRecordReader(serverInput, derived, clientToServer = true, initialSequence = 1)
            ready.countDown()

            while (!closed.get()) {
                val encrypted = reader.read() ?: break
                val plaintext = clientNoise.decrypt(encrypted)
                encrypted.fill(0)
                try {
                    for (frame in BftFrameCodec.decode(plaintext)) {
                        if (upstreamPort == null) incoming.put(frame.copy(body = frame.body.copyOf()))
                        proxyFrame(frame)
                        frame.body.fill(0)
                    }
                } finally {
                    plaintext.fill(0)
                }
            }
        } catch (error: Throwable) {
            if (!closed.get() && error !is InterruptedException) failure = error
        } finally {
            ready.countDown()
            clientNoise?.close()
            proxiedStreams.values.forEach { runCatching { it.socket.close() } }
        }
    }

    private fun writeOutbound() {
        try {
            while (!closed.get()) {
                val plaintext = outbound.take()
                try {
                    val encrypted = checkNotNull(outboundNoise).encrypt(plaintext)
                    try {
                        checkNotNull(outboundRecords).write(encrypted, 0)
                    } finally {
                        encrypted.fill(0)
                    }
                } finally {
                    plaintext.fill(0)
                }
            }
        } catch (error: InterruptedException) {
            Thread.currentThread().interrupt()
        } catch (error: Throwable) {
            if (!closed.get()) failure = error
        } finally {
            while (outbound.isNotEmpty()) outbound.poll()?.fill(0)
        }
    }

    private fun proxyFrame(frame: BftFrame) {
        if (upstreamPort == null && upstreamPorts.isEmpty()) return
        when (frame.type) {
            BftFrameType.OPEN -> {
                val serviceId = frame.body.firstOrNull()?.toInt()?.and(0xff) ?: return
                val port = upstreamPorts[serviceId] ?: upstreamPort ?: return
                val socket = Socket().apply {
                    tcpNoDelay = true
                    connect(InetSocketAddress(InetAddress.getByAddress(byteArrayOf(127, 0, 0, 1)), port), 5_000)
                }
                val stream = ProxiedStream(socket)
                proxiedStreams[frame.streamId] = stream
                Thread({ pumpUpstream(frame.streamId, stream) }, "bft-test-upstream-${frame.streamId}")
                    .apply { isDaemon = true; upstreamThreads += this; start() }
            }
            BftFrameType.DATA -> {
                val stream = proxiedStreams[frame.streamId] ?: return
                if (frame.body.size > stream.receiveWindow) {
                    send(BftFrame.reset(frame.streamId, BftResetCode.FLOW_CONTROL))
                    stream.socket.close()
                    proxiedStreams.remove(frame.streamId)
                    return
                }
                stream.receiveWindow -= frame.body.size
                stream.socket.getOutputStream().write(frame.body)
                stream.socket.getOutputStream().flush()
                stream.receiveWindow += frame.body.size
                stream.pendingWindowUpdate += frame.body.size
                if (stream.pendingWindowUpdate >= INITIAL_WINDOW / 4) {
                    val update = stream.pendingWindowUpdate
                    stream.pendingWindowUpdate = 0
                    send(BftFrame.windowUpdate(frame.streamId, update.toLong()))
                }
            }
            BftFrameType.WINDOW_UPDATE -> {
                val stream = proxiedStreams[frame.streamId] ?: return
                synchronized(stream.monitor) {
                    stream.sendWindow += readU32(frame.body)
                    stream.monitor.notifyAll()
                }
            }
            BftFrameType.CLOSE -> {
                val stream = proxiedStreams[frame.streamId] ?: return
                stream.clientClosed = true
                runCatching { stream.socket.shutdownOutput() }
                if (stream.upstreamClosed) {
                    proxiedStreams.remove(frame.streamId)
                    stream.socket.close()
                }
            }
            BftFrameType.RESET -> {
                proxiedStreams.remove(frame.streamId)?.let { stream ->
                    synchronized(stream.monitor) { stream.monitor.notifyAll() }
                    stream.socket.close()
                }
            }
            BftFrameType.PING -> send(BftFrame.pong(frame.body.copyOf()))
            else -> Unit
        }
    }

    private fun pumpUpstream(streamId: Long, stream: ProxiedStream) {
        val buffer = ByteArray(BftFrameCodec.MAX_BODY_LENGTH)
        try {
            while (!closed.get()) {
                val limit: Int
                synchronized(stream.monitor) {
                    while (!closed.get() && stream.sendWindow == 0L && proxiedStreams[streamId] === stream) {
                        stream.monitor.wait()
                    }
                    if (closed.get() || proxiedStreams[streamId] !== stream) return
                    limit = minOf(buffer.size.toLong(), stream.sendWindow).toInt()
                    stream.sendWindow -= limit
                }
                val count = stream.socket.getInputStream().read(buffer, 0, limit)
                if (count < 0) {
                    synchronized(stream.monitor) { stream.upstreamClosed = true }
                    send(BftFrame.close(streamId))
                    if (stream.clientClosed) {
                        proxiedStreams.remove(streamId)
                        stream.socket.close()
                    }
                    return
                }
                synchronized(stream.monitor) { stream.sendWindow += limit - count }
                if (count > 0) send(BftFrame.data(streamId, buffer.copyOf(count)))
            }
        } catch (_: SocketException) {
        } catch (error: Throwable) {
            if (!closed.get() && proxiedStreams.remove(streamId, stream)) {
                runCatching { send(BftFrame.reset(streamId, BftResetCode.UPSTREAM_FAILED)) }
                runCatching { stream.socket.close() }
            }
        } finally {
            buffer.fill(0)
        }
    }

    private inner class ProxiedStream(val socket: Socket) {
        val monitor = Object()
        var sendWindow = INITIAL_WINDOW.toLong()
        var receiveWindow = INITIAL_WINDOW.toLong()
        var pendingWindowUpdate = 0
        @Volatile var clientClosed = false
        @Volatile var upstreamClosed = false
    }

    private fun readExactly(input: java.io.InputStream, destination: ByteArray) {
        var offset = 0
        while (offset < destination.size) {
            val count = input.read(destination, offset, destination.size - offset)
            if (count < 0) throw EOFException("Truncated test peer handshake")
            if (count == 0) continue
            offset += count
        }
    }

    private fun rewriteHelloPin(
        record: ByteArray,
        keys: BftObfuscationKeys,
        s2cKey: ByteArray,
        pin: ByteArray
    ): ByteArray {
        require(pin.size == 32)
        val encryptedHello = checkNotNull(
            BftObfuscatedRecordReader(ByteArrayInputStream(record), keys, clientToServer = false, initialSequence = 1).read()
        )
        val decodedHello = BftNoiseCipherState(s2cKey).use { it.decrypt(encryptedHello) }
        encryptedHello.fill(0)
        val frames = BftFrameCodec.decode(decodedHello)
        decodedHello.fill(0)
        val patchedFrames = frames.map { frame ->
            if (frame.type != BftFrameType.HELLO) frame
            else {
                val hello = BftFrameCodec.parseHello(frame.body)
                BftFrame.hello(hello.copy(tlsSpkiSha256 = pin.copyOf()))
            }
        }
        val patchedHello = BftFrameCodec.encodeMany(patchedFrames)
        val encryptedPatchedHello = BftNoiseCipherState(s2cKey).use { it.encrypt(patchedHello) }
        patchedHello.fill(0)
        val output = ByteArrayOutputStream()
        BftObfuscatedRecordWriter(output, keys, clientToServer = false, random = ZeroRandom, initialSequence = 1)
            .write(encryptedPatchedHello, paddingLength = 0)
        encryptedPatchedHello.fill(0)
        return output.toByteArray()
    }

    private fun readU32(bytes: ByteArray): Long =
        ((bytes[0].toLong() and 0xff) shl 24) or ((bytes[1].toLong() and 0xff) shl 16) or
            ((bytes[2].toLong() and 0xff) shl 8) or (bytes[3].toLong() and 0xff)

    private object ZeroRandom : BftRandomSource {
        override fun fill(destination: ByteArray) = destination.fill(0)
    }

    companion object {
        private const val INITIAL_WINDOW = 262144
    }
}
