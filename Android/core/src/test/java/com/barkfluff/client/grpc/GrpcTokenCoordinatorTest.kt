package com.barkfluff.client.grpc

import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.coroutineScope
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class GrpcTokenCoordinatorTest {

    @Test
    fun `valid token is returned without refreshing`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = "access",
            accessTokenExpiration = 2_000_000L,
            refreshToken = "refresh",
            refreshTokenExpiration = 3_000_000L,
        )
        var refreshCalls = 0
        val coordinator = coordinator(store, now = { 1_000_000L }) {
            refreshCalls += 1
            TokenRefreshResult("new", 4_000_000L, "refresh", 3_000_000L)
        }

        assertTrue(coordinator.ensureValid())
        assertEquals(0, refreshCalls)
        assertEquals("access", store.accessToken)
    }

    @Test
    fun `expiring token is refreshed and persisted`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = "old-access",
            accessTokenExpiration = 1_100_000L,
            refreshToken = "old-refresh",
            refreshTokenExpiration = 3_000_000L,
        )
        val coordinator = coordinator(store, now = { 1_000_000L }) {
            TokenRefreshResult("new-access", 5_000_000L, "new-refresh", 6_000_000L)
        }

        assertTrue(coordinator.ensureValid())
        assertEquals("new-access", store.accessToken)
        assertEquals(5_000_000L, store.accessTokenExpiration)
        assertEquals("new-refresh", store.refreshToken)
        assertEquals(6_000_000L, store.refreshTokenExpiration)
    }

    @Test
    fun `concurrent refreshes share one identity request`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = "old-access",
            accessTokenExpiration = 0L,
            refreshToken = "refresh",
            refreshTokenExpiration = 3_000_000L,
        )
        var refreshCalls = 0
        val coordinator = coordinator(store, now = { 1_000_000L }) {
            refreshCalls += 1
            delay(20)
            TokenRefreshResult("new-access", 5_000_000L, "refresh", 3_000_000L)
        }

        val results = coroutineScope {
            listOf(
                async { coordinator.ensureValid() },
                async { coordinator.ensureValid() },
                async { coordinator.ensureValid() },
            ).map { it.await() }
        }

        assertTrue(results.all { it })
        assertEquals(1, refreshCalls)
    }

    @Test
    fun `force refresh ignores a currently valid access token`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = "access",
            accessTokenExpiration = 2_000_000L,
            refreshToken = "refresh",
            refreshTokenExpiration = 3_000_000L,
        )
        var refreshCalls = 0
        val coordinator = coordinator(store, now = { 1_000_000L }) {
            refreshCalls += 1
            TokenRefreshResult("new-access", 5_000_000L, "refresh", 3_000_000L)
        }

        assertTrue(coordinator.ensureValid(forceRefresh = true))
        assertEquals(1, refreshCalls)
    }

    @Test
    fun `concurrent force refreshes share one identity request`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = "access",
            accessTokenExpiration = 2_000_000L,
            refreshToken = "refresh",
            refreshTokenExpiration = 3_000_000L,
        )
        var refreshCalls = 0
        val coordinator = coordinator(store, now = { 1_000_000L }) {
            refreshCalls += 1
            delay(20)
            TokenRefreshResult("new-access", 5_000_000L, "refresh", 3_000_000L)
        }

        val results = coroutineScope {
            listOf(
                async { coordinator.ensureValid(forceRefresh = true) },
                async { coordinator.ensureValid(forceRefresh = true) },
                async { coordinator.ensureValid(forceRefresh = true) },
            ).map { it.await() }
        }

        assertTrue(results.all { it })
        assertEquals(1, refreshCalls)
    }

    @Test
    fun `coordinator instances share the process refresh mutex`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = "old-access",
            accessTokenExpiration = 0L,
            refreshToken = "refresh-${System.nanoTime()}",
            refreshTokenExpiration = 3_000_000L,
        )
        var refreshCalls = 0
        fun newCoordinator() = coordinator(store, now = { 1_000_000L }) {
            refreshCalls += 1
            delay(20)
            // Keep the access value stable to prove refresh-token rotation alone closes the
            // concurrent force-refresh window.
            TokenRefreshResult("old-access", 5_000_000L, "rotated-refresh", 3_000_000L)
        }
        val first = newCoordinator()
        val second = newCoordinator()

        val results = coroutineScope {
            listOf(async { first.ensureValid() }, async { second.ensureValid() }).map { it.await() }
        }

        assertTrue(results.all { it })
        assertEquals(1, refreshCalls)
    }

    @Test
    fun `missing refresh credentials fail without invoking identity`() = runBlocking {
        val store = FakeTokenStore(
            accessToken = null,
            accessTokenExpiration = 0L,
            refreshToken = null,
            refreshTokenExpiration = 0L,
        )
        var identityCalls = 0
        val coordinator = GrpcTokenCoordinator(
            store = store,
            ensureIdentityClient = { identityCalls += 1; true },
            refreshAccessToken = { _, _ -> Result.success(TokenRefreshResult("new", 1L, "r", 2L)) },
            nowMillis = { 1_000_000L },
        )

        assertFalse(coordinator.ensureValid())
        assertEquals(0, identityCalls)
    }

    private fun coordinator(
        store: FakeTokenStore,
        now: () -> Long,
        refresh: suspend () -> TokenRefreshResult,
    ): GrpcTokenCoordinator = GrpcTokenCoordinator(
        store = store,
        ensureIdentityClient = { true },
        refreshAccessToken = { _, _ -> Result.success(refresh()) },
        nowMillis = now,
    )

    private class FakeTokenStore(
        override var accessToken: String?,
        override var accessTokenExpiration: Long,
        override var refreshToken: String?,
        override var refreshTokenExpiration: Long,
        override val identityAddress: String = "identity.test",
    ) : TokenStore
}
