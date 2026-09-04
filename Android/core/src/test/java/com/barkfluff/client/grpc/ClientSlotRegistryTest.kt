package com.barkfluff.client.grpc

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors

class ClientSlotRegistryTest {

    @Test
    fun `lazy creation is normalized and idempotent`() {
        val created = mutableListOf<String>()
        val registry = registry(endpoint = { "https://Users.Example:443/path" }, created = created)

        assertEquals("users.example:443", registry.get(Service.USERS))
        assertEquals("users.example:443", registry.get(Service.USERS))
        assertEquals(listOf("users.example:443"), created)
    }

    @Test
    fun `recreate swaps client before closing old one`() {
        val created = mutableListOf<String>()
        val closed = mutableListOf<String>()
        var endpoint = "one.example:443"
        val registry = ClientSlotRegistry<Service, String>(
            endpointFor = { endpoint },
            normalize = { it.substringBefore(":") + ":443" },
            clientFactory = { _, normalized -> "client-$normalized-${created.size}".also { created += it } },
            closeAction = { closed += it },
        )

        assertEquals("client-one.example:443-0", registry.get(Service.USERS))
        endpoint = "two.example:443"
        registry.recreate()

        assertEquals("client-two.example:443-1", registry.get(Service.USERS))
        assertEquals(listOf("client-one.example:443-0"), closed)
    }

    @Test
    fun `next lazy read replaces a slot when endpoint changes`() {
        val created = mutableListOf<String>()
        val closed = mutableListOf<String>()
        var endpoint = "one.example:443"
        val registry = ClientSlotRegistry<Service, String>(
            endpointFor = { endpoint },
            normalize = { it.substringBefore(":") + ":443" },
            clientFactory = { _, normalized -> "client-$normalized-${created.size}".also { created += it } },
            closeAction = { closed += it },
        )

        assertEquals("client-one.example:443-0", registry.get(Service.USERS))
        endpoint = "two.example:443"

        assertEquals("client-two.example:443-1", registry.get(Service.USERS))
        assertEquals(listOf("client-one.example:443-0"), closed)
    }

    @Test
    fun `shutdown is terminal and closes every client`() {
        val closed = mutableListOf<String>()
        val registry = ClientSlotRegistry<Service, String>(
            endpointFor = { "service.example:443" },
            normalize = { it },
            clientFactory = { key, _ -> key.name },
            closeAction = { closed += it },
        )
        registry.get(Service.USERS)
        registry.get(Service.MESSAGES)

        registry.shutdown()

        assertTrue(registry.isShutdown)
        assertNull(registry.get(Service.USERS))
        assertEquals(setOf("USERS", "MESSAGES"), closed.toSet())
    }

    @Test
    fun `concurrent lazy reads create one client`() {
        val executor = Executors.newFixedThreadPool(8)
        val started = CountDownLatch(1)
        val release = CountDownLatch(1)
        var createCount = 0
        val registry = ClientSlotRegistry<Service, String>(
            endpointFor = { "service.example:443" },
            normalize = { it },
            clientFactory = { _, _ ->
                synchronized(this) { createCount++ }
                started.countDown()
                release.await()
                "client"
            },
        )

        val futures = (0 until 8).map { executor.submit<String?> { registry.get(Service.USERS) } }
        started.await()
        release.countDown()
        futures.forEach { assertEquals("client", it.get()) }
        executor.shutdownNow()

        assertEquals(1, createCount)
    }

    private fun registry(endpoint: () -> String, created: MutableList<String>) =
        ClientSlotRegistry<Service, String>(
            endpointFor = { endpoint() },
            normalize = { it.removePrefix("https://").substringBefore("/").lowercase() },
            clientFactory = { _, normalized -> normalized.also { created += it } },
        )

    private enum class Service { USERS, MESSAGES }
}
