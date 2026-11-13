package com.barkfluff.client.bft

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.util.concurrent.Callable
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicReference
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.Condition

class BftMuxConnectionTest {
    @Test
    fun slowReaderOnOneStreamDoesNotStopAnotherStream() = BftMuxTestPeer().use { peer ->
        val tunnel = peer.connect()
        try {
            val slow = tunnel.openStream(4)
            assertEquals(BftFrameType.OPEN, peer.receive().type)
            val fast = tunnel.openStream(5)
            assertEquals(BftFrameType.OPEN, peer.receive().type)

            val slowBytes = ByteArray(BftFrameCodec.MAX_BODY_LENGTH) { (it * 17).toByte() }
            peer.send(BftFrame.data(slow.streamId, slowBytes))
            peer.send(BftFrame.data(fast.streamId, "quick".toByteArray()))

            assertArrayEquals("quick".toByteArray(), readExactly(fast.input, 5))
            assertEquals(slowBytes.size, slow.input.available())
            assertArrayEquals(slowBytes, readExactly(slow.input, slowBytes.size))
        } finally {
            tunnel.close()
        }
    }

    @Test
    fun consumedBytesAccumulateIntoQuarterWindowUpdatesAndOutboundWindowBlocks() = BftMuxTestPeer().use { peer ->
        val tunnel = peer.connect()
        val executor = Executors.newSingleThreadExecutor()
        try {
            val incoming = tunnel.openStream(4)
            peer.receive()
            val threshold = tunnel.hello.initialWindow / 4
            var left = threshold
            while (left > 0) {
                val amount = minOf(left, BftFrameCodec.MAX_BODY_LENGTH)
                peer.send(BftFrame.data(incoming.streamId, ByteArray(amount) { 0x33 }))
                left -= amount
            }
            readExactly(incoming.input, threshold)
            val update = peer.receiveUntil { it.type == BftFrameType.WINDOW_UPDATE }
            assertEquals(incoming.streamId, update.streamId)
            assertEquals(threshold.toLong(), readU32(update.body))

            val outgoing = tunnel.openStream(4)
            peer.receive()
            val input = ByteArray(tunnel.hello.initialWindow + 1) { 0x55 }
            val write = executor.submit(Callable { outgoing.output.write(input) })
            var sent = 0
            while (sent < tunnel.hello.initialWindow) {
                val frame = peer.receiveUntil { it.type == BftFrameType.DATA && it.streamId == outgoing.streamId }
                sent += frame.body.size
                frame.body.fill(0)
            }
            assertEquals(tunnel.hello.initialWindow, sent)
            assertFalse("write must wait for peer window credit", write.isDone)
            peer.send(BftFrame.windowUpdate(outgoing.streamId, 1))
            write.get(2, TimeUnit.SECONDS)
            val lastByte = peer.receiveUntil { it.type == BftFrameType.DATA && it.streamId == outgoing.streamId }
            assertArrayEquals(byteArrayOf(0x55), lastByte.body)
        } finally {
            tunnel.close()
            executor.shutdownNow()
            executor.awaitTermination(2, TimeUnit.SECONDS)
        }
    }

    @Test
    fun closeAfterRemoteCloseIsGracefulAndIncompleteDisposeResets() = BftMuxTestPeer().use { peer ->
        val tunnel = peer.connect()
        try {
            val graceful = tunnel.openStream(4)
            peer.receive()
            peer.send(BftFrame.data(graceful.streamId, "last".toByteArray()), BftFrame.close(graceful.streamId))
            assertArrayEquals("last".toByteArray(), readExactly(graceful.input, 4))
            assertEquals(-1, graceful.input.read())
            graceful.close()
            val close = peer.receiveUntil { it.type == BftFrameType.CLOSE }
            assertEquals(graceful.streamId, close.streamId)

            val abandoned = tunnel.openStream(4)
            peer.receive()
            abandoned.close()
            val reset = peer.receiveUntil { it.type == BftFrameType.RESET && it.streamId == abandoned.streamId }
            assertEquals(BftResetCode.CANCEL.id, reset.body[0].toInt() and 0xff)
            peer.send(
                BftFrame.data(abandoned.streamId, byteArrayOf(1)),
                BftFrame.ping(byteArrayOf(0, 1, 2, 3, 4, 5, 6, 7))
            )
            peer.receiveUntil { it.type == BftFrameType.PONG }
            assertEquals(BftTunnel.State.READY, tunnel.state)

            val raced = tunnel.openStream(4)
            peer.receive()
            peer.send(BftFrame.close(raced.streamId), BftFrame.data(raced.streamId, byteArrayOf(9)))
            val streamClosed = peer.receiveUntil { it.type == BftFrameType.RESET && it.streamId == raced.streamId }
            assertEquals(BftResetCode.STREAM_CLOSED.id, streamClosed.body[0].toInt() and 0xff)
            assertEquals(BftTunnel.State.READY, tunnel.state)
        } finally {
            tunnel.close()
        }
    }

    @Test
    fun clientMuxSequenceVectorsRejectServerFramesAndIgnoreLateFramesAfterReset() {
        val sequences = BftVectors.arrayAt(BftVectors.root, "muxSequences").map { it as Map<String, Any?> }
            .filter { BftVectors.stringAt(it, "side") == "client" }
        assertEquals(3, sequences.size)
        for (sequence in sequences) {
            BftMuxTestPeer().use { peer ->
                val tunnel = peer.connect()
                try {
                val frames = BftVectors.arrayAt(sequence, "frames").map { raw ->
                    BftFrameCodec.decode(BftVectors.hex(raw)).single()
                }
                val offendingFrames = if (frames.firstOrNull()?.type == BftFrameType.HELLO) frames.drop(1) else frames
                peer.send(*offendingFrames.toTypedArray())
                val result = tunnel.completion.get(2, TimeUnit.SECONDS)
                assertEquals(BftTunnelCloseReason.Kind.PROTOCOL_ERROR, result.kind)
                } finally {
                    tunnel.close()
                }
            }
        }
    }

    @Test
    fun goAwayMarksUnprocessedStreamsRetryableAndPreventsNewOpens() = BftMuxTestPeer().use { peer ->
        val tunnel = peer.connect()
        try {
            val stream = tunnel.openStream(4)
            peer.receive()
            peer.send(BftFrame.goAway(0, BftGoAwayCode.SHUTDOWN))
            waitUntil { stream.canRetryAfterGoAway }
            assertTrue(stream.canRetryAfterGoAway)
            assertEquals(0L, tunnel.goAway?.lastStreamId)
            val error = org.junit.Assert.assertThrows(BftGoAwayException::class.java) { tunnel.openStream(4) }
            assertTrue(error.retryable)
            assertEquals(BftTunnel.State.READY, tunnel.state)
        } finally {
            tunnel.close()
        }
    }

    @Test
    fun pingTimeoutUsesTheInjectedClock() = BftMuxTestPeer(
        timeSource = AdvancingTimeSource(), keepAliveIdleMillis = 30, keepAliveTimeoutMillis = 20
    ).use { peer ->
        val tunnel = peer.connect()
        try {
            val ping = peer.receiveUntil { it.type == BftFrameType.PING }
            assertEquals(8, ping.body.size)
            val reason = tunnel.completion.get(2, TimeUnit.SECONDS)
            assertEquals(BftTunnelCloseReason.Kind.NETWORK_ERROR, reason.kind)
            assertTrue(reason.cause is SocketTimeoutException)
        } finally {
            tunnel.close()
        }
    }

    @Test
    fun tunnelCloseInterruptsBlockedReadAndWindowBlockedWrite() = BftMuxTestPeer().use { peer ->
        val tunnel = peer.connect()
        val executor = Executors.newFixedThreadPool(2)
        try {
            val readStream = tunnel.openStream(4)
            peer.receive()
            val enteredRead = CountDownLatch(1)
            val read = executor.submit(Callable {
                enteredRead.countDown()
                readStream.input.read()
            })
            assertTrue(enteredRead.await(1, TimeUnit.SECONDS))

            val writeStream = tunnel.openStream(4)
            peer.receive()
            val write = executor.submit(Callable {
                writeStream.output.write(ByteArray(tunnel.hello.initialWindow + 1))
            })
            var sent = 0
            while (sent < tunnel.hello.initialWindow) {
                sent += peer.receiveUntil { it.type == BftFrameType.DATA && it.streamId == writeStream.streamId }.body.size
            }
            assertFalse(write.isDone)

            tunnel.close()
            assertThrowsIo { read.get(2, TimeUnit.SECONDS) }
            assertThrowsIo { write.get(2, TimeUnit.SECONDS) }
        } finally {
            tunnel.close()
            executor.shutdownNow()
            executor.awaitTermination(2, TimeUnit.SECONDS)
        }
    }

    @Test
    fun closeNotificationCanReenterCloseAndOpenStream() = BftMuxTestPeer().use { peer ->
        val callbackFinished = CountDownLatch(1)
        val callbackFailure = AtomicReference<Throwable?>()
        val tunnelReference = AtomicReference<BftTunnel?>()
        val tunnel = peer.connect { state, _ ->
            if (state == BftTunnel.State.CLOSED) {
                try {
                    val closedTunnel = checkNotNull(tunnelReference.get())
                    val startedAt = System.nanoTime()
                    closedTunnel.close()
                    assertTrue(
                        "close callback blocked while closing the tunnel",
                        System.nanoTime() - startedAt < TimeUnit.MILLISECONDS.toNanos(1_500)
                    )
                    org.junit.Assert.assertThrows(IOException::class.java) { closedTunnel.openStream(4) }
                } catch (error: Throwable) {
                    callbackFailure.set(error)
                } finally {
                    callbackFinished.countDown()
                }
            }
        }
        tunnelReference.set(tunnel)

        peer.send(BftFrame.goAway(0, BftGoAwayCode.PROTOCOL_ERROR))

        assertTrue("close callback did not finish", callbackFinished.await(3, TimeUnit.SECONDS))
        callbackFailure.get()?.let { throw AssertionError("close callback failed", it) }
        assertEquals(BftTunnelCloseReason.Kind.PROTOCOL_ERROR, tunnel.completion.get(2, TimeUnit.SECONDS).kind)
    }

    @Test
    fun virtualSocketSoTimeoutActuallyLimitsStreamRead(): Unit = BftMuxTestPeer().use { peer ->
        val tunnel = peer.connect()
        try {
            val socket = BftSocketFactory(tunnel, 4).createSocket()
            socket.soTimeout = 50
            socket.connect(InetSocketAddress.createUnresolved("bft-origin.invalid", 443))
            peer.receiveUntil { it.type == BftFrameType.OPEN }
            org.junit.Assert.assertThrows(SocketTimeoutException::class.java) { socket.getInputStream().read() }
            socket.close()
            peer.receiveUntil { it.type == BftFrameType.RESET }
        } finally {
            tunnel.close()
        }
    }

    private fun readExactly(input: java.io.InputStream, size: Int): ByteArray {
        val result = ByteArray(size)
        var offset = 0
        while (offset < size) {
            val count = input.read(result, offset, size - offset)
            if (count < 0) throw AssertionError("Unexpected BFT stream EOF")
            offset += count
        }
        return result
    }

    private fun readU32(bytes: ByteArray): Long =
        ((bytes[0].toLong() and 0xff) shl 24) or ((bytes[1].toLong() and 0xff) shl 16) or
            ((bytes[2].toLong() and 0xff) shl 8) or (bytes[3].toLong() and 0xff)

    private fun waitUntil(predicate: () -> Boolean) {
        val deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(2)
        while (!predicate()) {
            if (System.nanoTime() >= deadline) throw AssertionError("Condition was not reached")
            Thread.yield()
        }
    }

    private fun assertThrowsIo(action: () -> Unit) {
        try {
            action()
        } catch (_: IOException) {
            return
        } catch (error: java.util.concurrent.ExecutionException) {
            if (error.cause is IOException) return
            throw error
        }
        throw AssertionError("Expected IOException")
    }

    private class AdvancingTimeSource : BftTimeSource {
        @Volatile private var currentTime = 1_800_000_000_000L
        override fun nowMillis(): Long = currentTime

        override fun await(condition: Condition, timeoutMillis: Long) {
            val signalled = condition.await(timeoutMillis, TimeUnit.MILLISECONDS)
            if (!signalled) currentTime += timeoutMillis
        }
    }
}
