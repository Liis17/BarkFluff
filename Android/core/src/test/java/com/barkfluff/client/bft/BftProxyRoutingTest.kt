package com.barkfluff.client.bft

import com.barkfluff.client.security.TlsPinLookup
import com.barkfluff.client.security.TlsPin
import com.barkfluff.client.security.TlsTransportFactory
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.Request
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.IOException

class BftProxyRoutingTest {
    @Test
    fun `endpoint map resolves every service and does not resolve hosts`() {
        val endpoints = (1..12).map { id -> BftServiceEndpoint(id, "https://service$id.barkfluff.invalid:8443") }

        endpoints.forEach { endpoint ->
            assertEquals(endpoint.serviceId, BftServiceRouter.serviceIdForEndpoint(endpoint.address, endpoints))
            assertEquals(
                endpoint.serviceId,
                BftServiceRouter.serviceIdForHttpUrl(endpoint.address.toHttpUrl(), endpoints)
            )
        }
        assertEquals(12, BftServiceRouter.serviceIdForHttpUrl("https://storage.barkfluff.com/versions".toHttpUrl(), emptyList()))
        assertEquals(null, BftServiceRouter.serviceIdForHttpUrl("https://images.example.net/a.png".toHttpUrl(), endpoints))
        assertEquals(null, BftServiceRouter.serviceIdForEndpoint("https://missing.example.invalid", endpoints))
    }

    @Test
    fun `unmapped BarkFluff HTTP endpoint is rejected instead of routed direct`() {
        val error = assertThrows(IllegalArgumentException::class.java) {
            BftServiceRouter.serviceIdForHttpUrl("https://unlisted.barkfluff.com/api".toHttpUrl(), emptyList())
        }
        assertTrue(error.message.orEmpty().contains("unavailable through the proxy"))
    }

    @Test
    fun `unmapped self hosted subdomain on another port is rejected`() {
        val endpoints = listOf(
            BftServiceEndpoint(2, "https://identity.example.test:443"),
            BftServiceEndpoint(3, "https://users.example.test:443")
        )
        assertThrows(IllegalArgumentException::class.java) {
            BftServiceRouter.serviceIdForHttpUrl("https://media.example.test:9443/avatar".toHttpUrl(), endpoints)
        }
    }

    @Test
    fun `saved IP literal is treated as owned but another IP remains direct`() {
        val endpoints = listOf(BftServiceEndpoint(5, "https://192.0.2.10:8443"))
        assertThrows(IllegalArgumentException::class.java) {
            BftServiceRouter.serviceIdForHttpUrl("https://192.0.2.10:9443/file".toHttpUrl(), endpoints)
        }
        assertEquals(null, BftServiceRouter.serviceIdForHttpUrl("https://192.0.2.11:9443/file".toHttpUrl(), endpoints))
    }

    @Test
    fun `single label service host remains owned on another port`() {
        val endpoints = listOf(
            BftServiceEndpoint(2, "https://identity:443"),
            BftServiceEndpoint(3, "https://users:443")
        )
        assertThrows(IllegalArgumentException::class.java) {
            BftServiceRouter.serviceIdForHttpUrl("https://identity:9443/avatar".toHttpUrl(), endpoints)
        }
    }

    @Test
    fun `unmapped foreign domain remains outside the proxy`() {
        val endpoints = listOf(
            BftServiceEndpoint(2, "https://identity.example.test:443"),
            BftServiceEndpoint(3, "https://users.example.test:443")
        )
        assertEquals(null, BftServiceRouter.serviceIdForHttpUrl("https://images.example.net/a.png".toHttpUrl(), endpoints))
    }

    @Test
    fun `enabled proxy failure invokes tunnel connector without resolving origin`() {
        val link = connectionString("Proxy test", "proxy.invalid")
        val store = MemoryStore(link, "Proxy test", true)
        var tunnelConnections = 0
        val manager = BftProxyManager(store, {
            tunnelConnections++
            throw IOException("Exit offline")
        })
        val transport = TlsTransportFactory(object : TlsPinLookup {
            override fun pinFor(host: String): TlsPin? = null
        }, manager)
        val client = transport.createOkHttpClientForService(
            "https://never-resolve.barkfluff.com/api".toHttpUrl(),
            serviceId = 4
        )

        val failure = assertThrows(IOException::class.java) {
            client.newCall(Request.Builder().url("https://never-resolve.barkfluff.com/api").build()).execute()
        }
        assertEquals("failed request attempted only the BFT connector", 1, tunnelConnections)
        assertTrue(generateSequence(failure as Throwable?) { it.cause }.any { it.message.orEmpty().contains("proxy") || it.message.orEmpty().contains("Exit offline") })
        client.connectionPool.evictAll()
        client.dispatcher.executorService.shutdownNow()
    }

    @Test
    fun `request created on direct route cannot execute after proxy is enabled`() {
        val store = MemoryStore()
        var tunnelConnections = 0
        val manager = BftProxyManager(store, {
            tunnelConnections++
            throw IOException("Exit offline")
        })
        val transport = TlsTransportFactory(object : TlsPinLookup {
            override fun pinFor(host: String): TlsPin? = null
        }, manager)
        val url = "https://never-resolve.barkfluff.com/api".toHttpUrl()
        val client = transport.createOkHttpClient(url)
        val call = client.newCall(Request.Builder().url(url).build())

        manager.saveConnectionString(connectionString("Proxy test", "proxy.invalid"), enable = true)
        val failure = assertThrows(IOException::class.java) { call.execute() }
        assertTrue(failure.message.orEmpty().contains("configuration changed"))
        assertEquals("stale direct call did not open a socket or tunnel", 0, tunnelConnections)
        client.dispatcher.executorService.shutdownNow()
        client.connectionPool.evictAll()
    }

    @Test
    fun `configuration and network changes advance persisted transport generation`() {
        val store = MemoryStore()
        val manager = BftProxyManager(store, { throw IOException("not used") })
        var generation = manager.transportGeneration
        val first = connectionString("First", "first.invalid")
        val second = connectionString("Second", "second.invalid")

        manager.saveConnectionString(first, enable = false)
        assertEquals(++generation, manager.transportGeneration)
        manager.setEnabled(true)
        assertEquals(++generation, manager.transportGeneration)
        manager.setEnabled(false)
        assertEquals(++generation, manager.transportGeneration)
        manager.saveConnectionString(second, enable = false)
        assertEquals(++generation, manager.transportGeneration)
        manager.onNetworkChanged()
        assertEquals(++generation, manager.transportGeneration)

        val reloaded = BftProxyManager(store, { throw IOException("not used") })
        assertTrue(reloaded.settings.configured)
        assertEquals("Second", reloaded.settings.name)
        assertEquals("second.invalid", reloaded.settings.host)
        assertEquals(false, store.read()?.enabled)
        assertNotNull(store.read()?.connectionString)
    }

    private fun connectionString(name: String, host: String) = BftConnectionString.format(
        BftConnectionSettings(
            staticPublicKey = ByteArray(32) { (it + 1).toByte() },
            obfsKey = ByteArray(32) { (it + 2).toByte() },
            host = host,
            port = 443,
            token = "link-token",
            name = name
        )
    )

    private class MemoryStore(
        connectionString: String? = null,
        name: String = "",
        enabled: Boolean = false
    ) : BftProxyManager.BftProxySettingsStore {
        private var value = connectionString?.let { BftProxyManager.BftProxyStoredSettings(it, name, enabled) }

        override fun read(): BftProxyManager.BftProxyStoredSettings? = value
        override fun write(connectionString: String, name: String, enabled: Boolean) {
            value = BftProxyManager.BftProxyStoredSettings(connectionString, name, enabled)
        }
        override fun clear() { value = null }
    }
}
