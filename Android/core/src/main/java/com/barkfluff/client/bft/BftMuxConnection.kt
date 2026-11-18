package com.barkfluff.client.bft

import java.io.EOFException
import java.io.IOException
import java.io.InterruptedIOException
import java.net.SocketTimeoutException
import java.util.ArrayDeque
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.Condition
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

internal interface BftTimeSource {
    fun nowMillis(): Long
    @Throws(InterruptedException::class)
    fun await(condition: Condition, timeoutMillis: Long)
}

internal object BftSystemTimeSource : BftTimeSource {
    override fun nowMillis(): Long = TimeUnit.NANOSECONDS.toMillis(System.nanoTime())
    override fun await(condition: Condition, timeoutMillis: Long) {
        condition.await(timeoutMillis, TimeUnit.MILLISECONDS)
    }
}

internal class BftMuxStreamState(
    val streamId: Long,
    val serviceId: Int,
    initialWindow: Int
) {
    var sendWindow = initialWindow.toLong()
    var receiveWindow = initialWindow.toLong()
    var bufferedBytes = 0
    var pendingOutboundBytes = 0
    var pendingWindowUpdateBytes = 0
    val incoming = ArrayDeque<ByteArray>()
    var currentRead: ByteArray? = null
    var currentReadOffset = 0
    val outgoing = ArrayDeque<BftFrame>()
    var inRoundRobin = false
    var remoteClosed = false
    var localClosed = false
    var localCloseSent = false
    var reset = false
    var retryableAfterGoAway = false
    var failure: Throwable? = null
    var readTimeoutMillis = 0
}

internal class BftMuxConnection(
    private val session: BftNoiseSession,
    private val closeTransport: () -> Unit,
    private val random: BftRandomSource,
    private val time: BftTimeSource,
    private val helloTimeoutMillis: Long,
    private val keepAliveIdleMillis: Long,
    private val keepAliveTimeoutMillis: Long,
    private val connected: (() -> Unit)?
) : AutoCloseable {
    private val lock = ReentrantLock()
    private val changed = lock.newCondition()
    private val writerReady = lock.newCondition()
    private val streams = LinkedHashMap<Long, BftMuxStreamState>()
    private val controlFrames = ArrayDeque<BftFrame>()
    private val roundRobin = ArrayDeque<Long>()
    private val notifications = ArrayDeque<() -> Unit>()
    private val closedCompletion = CompletableFuture<BftTunnelCloseReason>()
    private var readerThread: Thread? = null
    private var writerThread: Thread? = null
    private var keepAliveThread: Thread? = null
    private var closed = false
    private var ready = false
    private var hello: BftHello? = null
    private var maxOpenedStreamId = 0L
    private var receivedGoAway: BftGoAway? = null
    private var pendingPing: ByteArray? = null
    private var pendingPingAt = 0L
    private var lastReceivedAt = time.nowMillis()
    private var closeReason: BftTunnelCloseReason? = null
    private var pendingAbortCleanup: (() -> Unit)? = null
    private var pendingAbortReason: BftTunnelCloseReason? = null
    private var drainingNotifications = false
    private var rrCursor = 0

    val completion: CompletableFuture<BftTunnelCloseReason> get() = closedCompletion
    val state: BftTunnel.State
        get() = lock.withLock { if (closed) BftTunnel.State.CLOSED else if (ready) BftTunnel.State.READY else BftTunnel.State.CONNECTING }
    val helloInfo: BftHello? get() = lock.withLock { hello }
    val terminalReason: BftTunnelCloseReason? get() = lock.withLock { closeReason }
    val goAway: BftGoAway? get() = lock.withLock { receivedGoAway }
    fun isRetryableAfterGoAway(state: BftMuxStreamState): Boolean = lock.withLock { state.retryableAfterGoAway }

    fun start(token: String?): BftHello {
        lock.withLock {
            if (token != null) controlFrames.addLast(BftFrame.auth(token))
            readerThread = thread("bft-reader", ::readerLoop)
            writerThread = thread("bft-writer", ::writerLoop)
            keepAliveThread = thread("bft-keepalive", ::keepAliveLoop)
            readerThread!!.start()
            writerThread!!.start()
            keepAliveThread!!.start()
            if (token != null) writerReady.signal()
        }
        try {
            val deadline = time.nowMillis() + helloTimeoutMillis
            return lock.withLock {
                while (!ready && !closed) {
                    val remaining = deadline - time.nowMillis()
                    if (remaining <= 0) {
                        abortLocked(BftTunnelCloseReason(BftTunnelCloseReason.Kind.NETWORK_ERROR, SocketTimeoutException("Timed out waiting for BFT HELLO")))
                        break
                    }
                    awaitCondition(changed, remaining)
                }
                if (!ready) throw IOException("BFT tunnel did not receive HELLO", closeReason?.cause)
                checkNotNull(hello)
            }
        } finally {
            runPendingAbortCleanup()
            drainNotifications()
        }
    }

    fun openStream(serviceId: Int): BftTunnelStream {
        lock.withLock {
            ensureOpen()
            if (!ready) throw IOException("BFT tunnel is not ready")
            val serverInfo = checkNotNull(hello)
            if (serviceId !in serverInfo.serviceIds) throw BftServiceUnavailableException(serviceId)
            val goAway = receivedGoAway
            if (goAway != null) throw BftGoAwayException(goAway.lastStreamId, retryable = true)
            if (streams.size >= serverInfo.maxStreams) throw BftStreamLimitException()
            if (maxOpenedStreamId >= 0xffff_ffffL) throw BftProtocolException("BFT stream ID space is exhausted")
            val state = BftMuxStreamState(++maxOpenedStreamId, serviceId, serverInfo.initialWindow)
            streams[state.streamId] = state
            enqueueControlLocked(BftFrame.open(state.streamId, serviceId))
            return BftTunnelStream(this, state)
        }
    }

    fun setReadTimeout(state: BftMuxStreamState, timeoutMillis: Int) {
        require(timeoutMillis >= 0) { "timeout must be non-negative" }
        lock.withLock {
            state.readTimeoutMillis = timeoutMillis
            changed.signalAll()
        }
    }

    fun read(state: BftMuxStreamState, destination: ByteArray, offset: Int, length: Int): Int {
        if (length == 0) return 0
        val startNanos = System.nanoTime()
        var timeoutMillis = 0
        lock.lock()
        try {
            timeoutMillis = state.readTimeoutMillis
            val deadline = if (timeoutMillis == 0) Long.MAX_VALUE else startNanos + TimeUnit.MILLISECONDS.toNanos(timeoutMillis.toLong())
            while (true) {
                state.failure?.let { throw IOException("BFT stream was reset or the tunnel closed", it) }
                if (state.currentRead == null && state.incoming.isNotEmpty()) {
                    state.currentRead = state.incoming.removeFirst()
                    state.currentReadOffset = 0
                }
                val current = state.currentRead
                if (current != null) {
                    val count = minOf(length, current.size - state.currentReadOffset)
                    current.copyInto(destination, offset, state.currentReadOffset, state.currentReadOffset + count)
                    state.currentReadOffset += count
                    state.bufferedBytes -= count
                    state.receiveWindow += count
                    if (!state.remoteClosed && !state.reset && !closed) {
                        state.pendingWindowUpdateBytes += count
                        if (state.pendingWindowUpdateBytes >= (hello?.initialWindow ?: 262144) / 4) {
                            val increment = state.pendingWindowUpdateBytes.toLong()
                            state.pendingWindowUpdateBytes = 0
                            enqueueControlLocked(BftFrame.windowUpdate(state.streamId, increment))
                        }
                    }
                    if (state.currentReadOffset == current.size) {
                        current.fill(0)
                        state.currentRead = null
                        state.currentReadOffset = 0
                    }
                    closeIfFinishedLocked(state)
                    changed.signalAll()
                    return count
                }
                if (state.remoteClosed || state.reset) return -1
                if (closed) throw IOException("BFT tunnel is closed", closeReason?.cause)
                val remaining = if (timeoutMillis == 0) Long.MAX_VALUE else deadline - System.nanoTime()
                if (remaining <= 0) throw SocketTimeoutException("BFT stream read timed out")
                try {
                    if (timeoutMillis == 0) changed.await()
                    else changed.awaitNanos(remaining)
                } catch (error: InterruptedException) {
                    Thread.currentThread().interrupt()
                    throw InterruptedIOException("BFT stream read interrupted").also { it.initCause(error) }
                }
                timeoutMillis = state.readTimeoutMillis
            }
        } finally {
            lock.unlock()
        }
    }

    fun available(state: BftMuxStreamState): Int = lock.withLock { state.bufferedBytes }

    fun write(state: BftMuxStreamState, source: ByteArray, offset: Int, length: Int) {
        var written = 0
        lock.lock()
        try {
            while (written < length) {
                ensureOpen()
                state.failure?.let { throw IOException("BFT stream was reset", it) }
                if (state.localClosed) throw IOException("BFT stream write side is closed")
                val window = minOf(state.sendWindow, (checkNotNull(hello).initialWindow - state.pendingOutboundBytes).toLong())
                val amount = minOf(window, BftFrameCodec.MAX_BODY_LENGTH.toLong(), (length - written).toLong()).toInt()
                if (amount > 0) {
                    val chunk = source.copyOfRange(offset + written, offset + written + amount)
                    state.sendWindow -= amount
                    state.pendingOutboundBytes += amount
                    state.outgoing.addLast(BftFrame.data(state.streamId, chunk))
                    addToRoundRobinLocked(state)
                    written += amount
                    continue
                }
                try {
                    changed.await()
                } catch (error: InterruptedException) {
                    Thread.currentThread().interrupt()
                    throw InterruptedIOException("BFT stream write interrupted").also { it.initCause(error) }
                }
            }
        } finally {
            lock.unlock()
        }
    }

    fun completeWrite(state: BftMuxStreamState) {
        lock.withLock {
            if (state.localClosed || state.reset || closed) return
            state.localClosed = true
            state.outgoing.addLast(BftFrame.close(state.streamId))
            addToRoundRobinLocked(state)
            closeIfFinishedLocked(state)
            changed.signalAll()
        }
    }

    fun reset(state: BftMuxStreamState, code: BftResetCode) {
        lock.withLock {
            if (state.reset || closed) return
            markResetLocked(state, IOException("BFT stream reset ($code)"), sendReset = true, code = code)
        }
    }

    fun disposeStream(state: BftMuxStreamState) {
        lock.withLock {
            if (state.reset || closed || streams[state.streamId] !== state) return
            if (state.remoteClosed && state.bufferedBytes == 0) {
                if (!state.localClosed) {
                    state.localClosed = true
                    state.outgoing.addLast(BftFrame.close(state.streamId))
                    addToRoundRobinLocked(state)
                }
                closeIfFinishedLocked(state)
            } else {
                markResetLocked(state, IOException("BFT stream was disposed before graceful completion"), sendReset = true, code = BftResetCode.CANCEL)
            }
        }
    }

    private fun readerLoop() {
        try {
            while (true) {
                val message = session.readMessage() ?: throw EOFException("BFT peer closed the tunnel")
                lock.withLock {
                    lastReceivedAt = time.nowMillis()
                    changed.signalAll()
                }
                val frames = try { BftFrameCodec.decode(message) } finally { message.fill(0) }
                for (frame in frames) {
                    try {
                        processIncoming(frame)
                    } finally {
                        frame.body.fill(0)
                    }
                }
            }
        } catch (error: Throwable) {
            if (error is InterruptedException && Thread.currentThread().isInterrupted) return
            val receivedShutdown = lock.withLock {
                receivedGoAway?.takeIf { it.code == BftGoAwayCode.SHUTDOWN }
            }
            val reason = when {
                receivedShutdown != null -> BftTunnelCloseReason(BftTunnelCloseReason.Kind.GOAWAY, error, receivedShutdown)
                error is BftProtocolException || error is BftRecordException ->
                    BftTunnelCloseReason(BftTunnelCloseReason.Kind.PROTOCOL_ERROR, error)
                else -> BftTunnelCloseReason(BftTunnelCloseReason.Kind.NETWORK_ERROR, error)
            }
            abort(reason)
        }
    }

    private fun processIncoming(frame: BftFrame) {
        try {
            lock.withLock {
                if (closed) return
                if (!ready) {
                    when (frame.type) {
                        BftFrameType.HELLO -> {
                            hello = BftFrameCodec.parseHello(frame.body)
                            ready = true
                            changed.signalAll()
                            connected?.let { callback -> notifications.addLast(callback) }
                        }
                        BftFrameType.GOAWAY -> {
                            val goAway = parseGoAway(frame)
                            receivedGoAway = goAway
                            val reason = if (goAway.code == BftGoAwayCode.AUTH_FAILED) {
                                BftTunnelCloseReason(BftTunnelCloseReason.Kind.AUTH_FAILED, IOException("BFT authentication failed"), goAway)
                            } else {
                                BftTunnelCloseReason(BftTunnelCloseReason.Kind.GOAWAY, IOException("Exit sent GOAWAY before HELLO"), goAway)
                            }
                            abortLocked(reason)
                        }
                        else -> throw BftProtocolException("HELLO must be the first server frame")
                    }
                    return
                }

                when (frame.type) {
                    BftFrameType.AUTH, BftFrameType.HELLO, BftFrameType.OPEN ->
                        throw BftProtocolException("Unexpected ${frame.type} frame from Exit")
                    BftFrameType.DATA, BftFrameType.WINDOW_UPDATE, BftFrameType.CLOSE, BftFrameType.RESET -> processStreamFrameLocked(frame)
                    BftFrameType.PING -> enqueueControlLocked(BftFrame.pong(frame.body.copyOf()), urgent = true)
                    BftFrameType.PONG -> {
                        val ping = pendingPing
                        if (ping != null && MessageDigestSupport.constantTimeEquals(ping, frame.body)) {
                            ping.fill(0)
                            pendingPing = null
                            changed.signalAll()
                        }
                    }
                    BftFrameType.GOAWAY -> receiveGoAwayLocked(parseGoAway(frame))
                }
            }
        } finally {
            runPendingAbortCleanup()
            drainNotifications()
        }
    }

    private fun processStreamFrameLocked(frame: BftFrame) {
        if (frame.streamId > maxOpenedStreamId) throw BftProtocolException("Frame references a stream that was never opened")
        val state = streams[frame.streamId] ?: return
        if (state.reset) return
        when (frame.type) {
            BftFrameType.WINDOW_UPDATE -> {
                if (state.localClosed) return
                val increment = readU32(frame.body)
                if (state.sendWindow + increment > Int.MAX_VALUE) {
                    markResetLocked(state, BftProtocolException("BFT send window overflow"), sendReset = true, BftResetCode.FLOW_CONTROL)
                } else {
                    state.sendWindow += increment
                    changed.signalAll()
                }
            }
            BftFrameType.RESET -> markResetLocked(
                state,
                IOException("Exit reset BFT stream (${BftResetCode.fromId(frame.body[0].toInt() and 0xff)})"),
                sendReset = false
            )
            BftFrameType.DATA, BftFrameType.CLOSE -> {
                if (state.remoteClosed) {
                    markResetLocked(state, BftProtocolException("Exit sent DATA/CLOSE after closing its write side"), true, BftResetCode.STREAM_CLOSED)
                    return
                }
                if (frame.type == BftFrameType.CLOSE) {
                    state.remoteClosed = true
                    closeIfFinishedLocked(state)
                    changed.signalAll()
                } else if (frame.body.size > state.receiveWindow) {
                    markResetLocked(state, BftProtocolException("Exit exceeded the BFT receive window"), true, BftResetCode.FLOW_CONTROL)
                } else {
                    state.receiveWindow -= frame.body.size
                    state.bufferedBytes += frame.body.size
                    state.incoming.addLast(frame.body.copyOf())
                    changed.signalAll()
                }
            }
            else -> throw BftProtocolException("Unexpected BFT stream frame")
        }
    }

    private fun receiveGoAwayLocked(goAway: BftGoAway) {
        if (receivedGoAway != null) throw BftProtocolException("Exit sent more than one GOAWAY")
        receivedGoAway = goAway
        for ((id, state) in streams.toMap()) {
            if (id > goAway.lastStreamId) {
                state.retryableAfterGoAway = true
                state.failure = BftGoAwayException(goAway.lastStreamId, retryable = true)
                clearStreamBuffers(state)
                streams.remove(id)
            }
        }
        changed.signalAll()
        if (goAway.code != BftGoAwayCode.SHUTDOWN) {
            val kind = if (goAway.code == BftGoAwayCode.AUTH_FAILED) BftTunnelCloseReason.Kind.AUTH_FAILED
            else if (goAway.code == BftGoAwayCode.PROTOCOL_ERROR) BftTunnelCloseReason.Kind.PROTOCOL_ERROR
            else BftTunnelCloseReason.Kind.GOAWAY
            abortLocked(BftTunnelCloseReason(kind, IOException("Exit sent GOAWAY (${goAway.code})"), goAway))
        }
    }

    private fun parseGoAway(frame: BftFrame): BftGoAway = BftGoAway(readU32(frame.body), BftGoAwayCode.fromId(frame.body[4].toInt() and 0xff))

    private fun markResetLocked(
        state: BftMuxStreamState,
        error: Throwable,
        sendReset: Boolean,
        code: BftResetCode = BftResetCode.INTERNAL
    ) {
        if (state.reset) return
        state.reset = true
        state.failure = error
        if (sendReset) enqueueControlLocked(BftFrame.reset(state.streamId, code))
        streams.remove(state.streamId)
        clearStreamBuffers(state)
        changed.signalAll()
    }

    private fun clearStreamBuffers(state: BftMuxStreamState) {
        state.currentRead?.fill(0)
        state.currentRead = null
        state.currentReadOffset = 0
        while (state.incoming.isNotEmpty()) state.incoming.removeFirst().fill(0)
        while (state.outgoing.isNotEmpty()) state.outgoing.removeFirst().body.fill(0)
        state.bufferedBytes = 0
        state.pendingOutboundBytes = 0
        state.pendingWindowUpdateBytes = 0
    }

    private fun closeIfFinishedLocked(state: BftMuxStreamState) {
        if (state.localClosed && state.remoteClosed && state.localCloseSent && state.bufferedBytes == 0) {
            streams.remove(state.streamId)
            changed.signalAll()
        }
    }

    private fun enqueueControlLocked(frame: BftFrame, urgent: Boolean = false) {
        if (closed) {
            frame.body.fill(0)
            return
        }
        if (urgent) controlFrames.addFirst(frame) else controlFrames.addLast(frame)
        writerReady.signal()
    }

    private fun addToRoundRobinLocked(state: BftMuxStreamState) {
        if (state.inRoundRobin) return
        state.inRoundRobin = true
        roundRobin.addLast(state.streamId)
        writerReady.signal()
    }

    private data class PendingData(val state: BftMuxStreamState, val length: Int)

    private fun writerLoop() {
        try {
            while (true) {
                val frames: List<BftFrame>
                val message: ByteArray
                val completedData = ArrayList<PendingData>()
                val completedClose = ArrayList<BftMuxStreamState>()
                lock.lock()
                try {
                    while (!closed && controlFrames.isEmpty() && roundRobin.isEmpty()) writerReady.await()
                    if (closed) return
                    val selected = takeNextFramesLocked(completedData, completedClose)
                    if (selected.isEmpty()) continue
                    frames = selected
                    message = BftFrameCodec.encodeMany(frames)
                    frames.forEach { it.body.fill(0) }
                } finally {
                    lock.unlock()
                }
                try {
                    session.writeMessage(message)
                    lock.withLock {
                        completedData.forEach { pending -> pending.state.pendingOutboundBytes -= pending.length }
                        completedClose.forEach { it.localCloseSent = true; closeIfFinishedLocked(it) }
                        changed.signalAll()
                    }
                } finally {
                    message.fill(0)
                }
            }
        } catch (error: Throwable) {
            if (error is InterruptedException && Thread.currentThread().isInterrupted) return
            abort(BftTunnelCloseReason(BftTunnelCloseReason.Kind.NETWORK_ERROR, error))
        }
    }

    private fun takeNextFramesLocked(
        completedData: MutableList<PendingData>,
        completedClose: MutableList<BftMuxStreamState>
    ): List<BftFrame> {
        val frames = ArrayList<BftFrame>()
        var used = 0
        while (controlFrames.isNotEmpty()) {
            val next = controlFrames.first()
            val size = BftFrameCodec.HEADER_LENGTH + next.body.size
            if (used + size > BftFrameCodec.MAX_MESSAGE_LENGTH) break
            controlFrames.removeFirst()
            frames += next
            used += size
        }
        val visits = roundRobin.size
        repeat(visits) {
            if (used >= BftFrameCodec.MAX_MESSAGE_LENGTH || roundRobin.isEmpty()) return@repeat
            val id = roundRobin.removeFirst()
            val state = streams[id]
            if (state == null || state.reset || state.outgoing.isEmpty()) {
                if (state != null) state.inRoundRobin = false
                return@repeat
            }
            val next = state.outgoing.first()
            val size = BftFrameCodec.HEADER_LENGTH + next.body.size
            if (used + size <= BftFrameCodec.MAX_MESSAGE_LENGTH) {
                state.outgoing.removeFirst()
                frames += next
                used += size
                if (next.type == BftFrameType.DATA) completedData += PendingData(state, next.body.size)
                if (next.type == BftFrameType.CLOSE) completedClose += state
            }
            if (state.outgoing.isEmpty()) state.inRoundRobin = false else roundRobin.addLast(id)
        }
        return frames
    }

    private fun keepAliveLoop() {
        try {
            lock.lock()
            try {
                while (!closed) {
                    val ping = pendingPing
                    val waitMillis = if (ping != null) {
                        pendingPingAt + keepAliveTimeoutMillis - time.nowMillis()
                    } else {
                        lastReceivedAt + keepAliveIdleMillis - time.nowMillis()
                    }
                    if (waitMillis > 0) {
                        awaitCondition(changed, waitMillis)
                        continue
                    }
                    if (pendingPing != null) {
                        abortLocked(BftTunnelCloseReason(BftTunnelCloseReason.Kind.NETWORK_ERROR, SocketTimeoutException("BFT PONG timed out")))
                    } else {
                        val payload = ByteArray(8).also(random::fill)
                        pendingPing = payload.copyOf()
                        pendingPingAt = time.nowMillis()
                        enqueueControlLocked(BftFrame.ping(payload))
                    }
                }
            } finally {
                lock.unlock()
            }
        } catch (error: InterruptedException) {
            Thread.currentThread().interrupt()
        } catch (error: Throwable) {
            abort(BftTunnelCloseReason(BftTunnelCloseReason.Kind.NETWORK_ERROR, error))
        } finally {
            runPendingAbortCleanup()
            drainNotifications()
        }
    }

    private fun awaitCondition(condition: Condition, timeoutMillis: Long) {
        try {
            time.await(condition, timeoutMillis.coerceAtLeast(1))
        } catch (error: InterruptedException) {
            Thread.currentThread().interrupt()
            throw InterruptedIOException("BFT operation interrupted").also { it.initCause(error) }
        }
    }

    private fun ensureOpen() {
        if (closed) throw IOException("BFT tunnel is closed", closeReason?.cause)
    }

    private fun thread(name: String, action: () -> Unit): Thread = Thread(action, name).apply { isDaemon = true }

    private fun abort(reason: BftTunnelCloseReason) {
        lock.withLock { abortLocked(reason) }
        runPendingAbortCleanup()
        drainNotifications()
    }

    private fun abortLocked(reason: BftTunnelCloseReason) {
        if (closed) return
        closed = true
        closeReason = reason
        controlFrames.forEach { it.body.fill(0) }
        controlFrames.clear()
        streams.values.forEach { state ->
            state.failure = reason.cause ?: IOException("BFT tunnel closed")
            clearStreamBuffers(state)
        }
        streams.clear()
        roundRobin.clear()
        pendingPing?.fill(0)
        pendingPing = null
        changed.signalAll()
        writerReady.signalAll()
        val abortingThread = Thread.currentThread()
        pendingAbortReason = reason
        pendingAbortCleanup = {
            runCatching(closeTransport)
            runCatching { session.close() }
            readerThread?.takeIf { it !== abortingThread }?.interrupt()
            writerThread?.takeIf { it !== abortingThread }?.interrupt()
            keepAliveThread?.takeIf { it !== abortingThread }?.interrupt()
        }
    }

    private fun runPendingAbortCleanup() {
        val pending = lock.withLock {
            val cleanup = pendingAbortCleanup ?: return
            val reason = checkNotNull(pendingAbortReason)
            pendingAbortCleanup = null
            pendingAbortReason = null
            cleanup to reason
        }
        try {
            pending.first()
        } finally {
            lock.withLock { notifications.addLast { closedCompletion.complete(pending.second) } }
        }
    }

    private fun drainNotifications() {
        val shouldDrain = lock.withLock {
            if (drainingNotifications) false else {
                drainingNotifications = true
                true
            }
        }
        if (!shouldDrain) return

        try {
            while (true) {
                val notification = lock.withLock {
                    if (notifications.isEmpty()) {
                        drainingNotifications = false
                        null
                    } else {
                        notifications.removeFirst()
                    }
                } ?: return
                notification()
            }
        } catch (error: Throwable) {
            lock.withLock { drainingNotifications = false }
            throw error
        }
    }

    override fun close() {
        abort(BftTunnelCloseReason(BftTunnelCloseReason.Kind.LOCAL_CLOSE, null))
        val threads = listOfNotNull(readerThread, writerThread, keepAliveThread)
        threads.filter { it !== Thread.currentThread() }.forEach {
            try { it.join(1000) } catch (_: InterruptedException) { Thread.currentThread().interrupt(); return }
        }
    }

    companion object {
        private fun readU32(bytes: ByteArray): Long =
            ((bytes[0].toLong() and 0xff) shl 24) or ((bytes[1].toLong() and 0xff) shl 16) or
                ((bytes[2].toLong() and 0xff) shl 8) or (bytes[3].toLong() and 0xff)
    }
}

internal object MessageDigestSupport {
    fun constantTimeEquals(a: ByteArray, b: ByteArray): Boolean {
        if (a.size != b.size) return false
        var difference = 0
        for (index in a.indices) difference = difference or (a[index].toInt() xor b[index].toInt())
        return difference == 0
    }
}
