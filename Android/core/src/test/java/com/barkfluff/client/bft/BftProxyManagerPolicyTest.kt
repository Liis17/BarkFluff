package com.barkfluff.client.bft

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeout

class BftProxyManagerPolicyTest {
    @Test
    fun `active session import enables proxy after matching validation`() {
        val link = connectionString()
        val store = MemoryStore()
        val peer = BftMuxTestPeer()
        var validationCalls = 0
        val manager = BftProxyManager(
            store,
            { peer.connect() },
            connectionValidator = { validationCalls++ }
        )
        try {
            manager.saveConnectionStringAndEnable(link)
            assertTrue(manager.settings.enabled)
            assertEquals(BftProxyStatus.CONNECTED, manager.settings.status)
            assertEquals(1, validationCalls)
            assertTrue(store.read()?.enabled == true)
        } finally {
            manager.setPersistentNetworkDemand(false)
            peer.close()
        }
    }

    @Test
    fun `active session tunnel mismatch stays enabled but every request fails closed`() {
        val link = connectionString()
        val store = MemoryStore(link, enabled = true)
        val peer = BftMuxTestPeer()
        var connectorCalls = 0
        val manager = BftProxyManager(
            store,
            { connectorCalls++; peer.connect() },
            connectionValidator = { throw BftProxyException(BftProxyErrorCode.SERVER_MISMATCH) }
        )
        try {
            val first = assertThrows(BftProxyException::class.java) { manager.openSocketStreams(1) }
            assertEquals(BftProxyErrorCode.SERVER_MISMATCH, first.code)
            assertTrue(manager.isEnabled)
            assertEquals(BftProxyStatus.SERVER_MISMATCH, manager.settings.status)

            val second = assertThrows(BftProxyException::class.java) { manager.openSocketStreams(1) }
            assertEquals(BftProxyErrorCode.SERVER_MISMATCH, second.code)
            assertEquals("mismatch gates requests before opening a second tunnel", 1, connectorCalls)
        } finally {
            manager.setPersistentNetworkDemand(false)
            peer.close()
        }
    }

    @Test
    fun `each new tunnel is checked and a changed node blocks requests`() {
        val store = MemoryStore(connectionString(), enabled = true)
        val firstPeer = BftMuxTestPeer()
        val secondPeer = BftMuxTestPeer()
        var connectorCalls = 0
        var validationCalls = 0
        val manager = BftProxyManager(
            store,
            {
                if (++connectorCalls == 1) firstPeer.connect() else secondPeer.connect()
            },
            connectionValidator = {
                validationCalls++
                if (validationCalls == 2) throw BftProxyException(BftProxyErrorCode.SERVER_MISMATCH)
            }
        )
        try {
            val streams = manager.openSocketStreams(1)
            manager.onNetworkChanged()
            streams.close()

            val failure = assertThrows(BftProxyException::class.java) { manager.openSocketStreams(1) }
            assertEquals(BftProxyErrorCode.SERVER_MISMATCH, failure.code)
            assertEquals(2, connectorCalls)
            assertEquals(2, validationCalls)
            assertTrue(manager.isEnabled)
            assertEquals(BftProxyStatus.SERVER_MISMATCH, manager.settings.status)
        } finally {
            manager.setPersistentNetworkDemand(false)
            firstPeer.close()
            secondPeer.close()
        }
    }

    @Test
    fun `new active session import stays saved but disabled when node mismatches`() {
        val link = connectionString()
        val store = MemoryStore()
        val peer = BftMuxTestPeer()
        val manager = BftProxyManager(
            store,
            { peer.connect() },
            connectionValidator = { throw BftProxyException(BftProxyErrorCode.SERVER_MISMATCH) }
        )
        try {
            val error = assertThrows(BftProxyException::class.java) { manager.saveConnectionStringAndEnable(link) }
            assertEquals(BftProxyErrorCode.SERVER_MISMATCH, error.code)
            assertTrue(manager.settings.configured)
            assertFalse(manager.settings.enabled)
            assertFalse(store.read()?.enabled ?: true)
            assertEquals(BftProxyStatus.SERVER_MISMATCH, manager.settings.status)
        } finally {
            manager.setPersistentNetworkDemand(false)
            peer.close()
        }
    }

    @Test
    fun `active session node check failure keeps imported proxy disabled`() {
        val link = connectionString()
        val store = MemoryStore()
        val peer = BftMuxTestPeer()
        val manager = BftProxyManager(
            store,
            { peer.connect() },
            connectionValidator = {
                throw BftProxyException(BftProxyErrorCode.SERVER_IDENTITY_UNAVAILABLE)
            }
        )
        try {
            val error = assertThrows(BftProxyException::class.java) {
                manager.saveConnectionStringAndEnable(link)
            }
            assertEquals(BftProxyErrorCode.SERVER_IDENTITY_UNAVAILABLE, error.code)
            assertTrue(manager.settings.configured)
            assertFalse(manager.settings.enabled)
            assertFalse(store.read()?.enabled ?: true)
            assertFalse(manager.isEnabled)
        } finally {
            manager.setPersistentNetworkDemand(false)
            peer.close()
        }
    }

    @Test
    fun `manual retry clears rejected key cooldown`() {
        val link = connectionString()
        val store = MemoryStore(link, enabled = true)
        val peer = BftMuxTestPeer()
        val secondAttempt = CountDownLatch(1)
        var connectorCalls = 0
        val manager = BftProxyManager(store, {
            if (++connectorCalls == 1) {
                throw BftTunnelConnectException(
                    "test connection failure",
                    BftTunnelCloseReason(BftTunnelCloseReason.Kind.AUTH_FAILED, IOException()),
                    IOException()
                )
            }
            peer.connect().also { secondAttempt.countDown() }
        })
        try {
            val failure = assertThrows(BftProxyException::class.java) { manager.openSocketStreams(1) }
            assertEquals(BftProxyErrorCode.KEY_REJECTED, failure.code)
            assertEquals(BftProxyStatus.KEY_REJECTED, manager.settings.status)

            manager.retryConnection()
            assertTrue("manual retry did not start another tunnel", secondAttempt.await(5, TimeUnit.SECONDS))
            runBlocking {
                withTimeout(5_000) { manager.state.first { it.status == BftProxyStatus.CONNECTED } }
            }
            assertEquals(2, connectorCalls)
        } finally {
            manager.setPersistentNetworkDemand(false)
            peer.close()
        }
    }

    @Test
    fun `encrypted settings read failure leaves proxy unconfigured`() {
        val brokenStore = object : BftProxyManager.BftProxySettingsStore {
            override fun read(): BftProxyManager.BftProxyStoredSettings? = error("keystore unavailable")
            override fun write(connectionString: String, name: String, enabled: Boolean) = Unit
            override fun clear() = Unit
        }
        val manager = BftProxyManager(brokenStore, { error("not used") })
        assertFalse(manager.settings.configured)
        assertFalse(manager.isEnabled)
    }

    @Test
    fun `retry time reads monotonic nanoseconds`() {
        var nowNanos = 1_250_000_000L
        val clock = { monotonicTimeMillis { nowNanos } }

        assertEquals(1_250L, clock())
        nowNanos += TimeUnit.MILLISECONDS.toNanos(90)
        assertEquals(1_340L, clock())
    }

    private fun connectionString(): String = BftConnectionString.format(
        BftConnectionSettings(ByteArray(32) { 1 }, ByteArray(32) { 2 }, "proxy.example", 443, "link-token", "Test")
    )

    private class MemoryStore(
        connectionString: String? = null,
        enabled: Boolean = false
    ) : BftProxyManager.BftProxySettingsStore {
        private var value = connectionString?.let { BftProxyManager.BftProxyStoredSettings(it, "Test", enabled) }
        override fun read() = value
        override fun write(connectionString: String, name: String, enabled: Boolean) {
            value = BftProxyManager.BftProxyStoredSettings(connectionString, name, enabled)
        }
        override fun clear() { value = null }
    }
}
