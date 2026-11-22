package com.barkfluff.client.utils

import coil.network.HttpException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.test.runTest
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ExpiredFileUrlRefresherTest {

    private val cache = mutableMapOf("file-1" to EXPIRED)
    private var fetches = 0

    private fun refresher(fetch: suspend (String) -> String? = { fetches++; FRESH }) = ExpiredFileUrlRefresher(
        fileIdForUrl = { url -> cache.entries.firstOrNull { it.value == url }?.key },
        currentUrl = { cache[it] },
        fetchUrl = fetch,
        onRefreshed = { fileId, expired, fresh ->
            if (fresh != null) cache[fileId] = fresh else if (cache[fileId] == expired) cache.remove(fileId)
        },
    )

    @Test
    fun `expired cached link is replaced with a new one`() = runTest {
        assertEquals(FRESH, refresher().refresh(EXPIRED))
        assertEquals(FRESH, cache["file-1"])
        assertEquals(1, fetches)
    }

    @Test
    fun `link outside the URL cache is not refreshed`() = runTest {
        assertNull(refresher().refresh("https://files/download/unknown"))
        assertEquals(0, fetches)
    }

    @Test
    fun `parallel 404 for the same link requests a new link once`() = runTest {
        val gate = CompletableDeferred<Unit>()
        val refresher = refresher { fetches++; gate.await(); FRESH }

        val first = async { refresher.refresh(EXPIRED) }
        val second = async { refresher.refresh(EXPIRED) }
        testScheduler.runCurrent()
        gate.complete(Unit)

        assertEquals(FRESH, first.await())
        assertEquals(FRESH, second.await())
        assertEquals(1, fetches)
    }

    @Test
    fun `failed request drops the expired link from the cache`() = runTest {
        assertNull(refresher { fetches++; throw IllegalStateException("Files unavailable") }.refresh(EXPIRED))
        assertNull(cache["file-1"])
        assertEquals(1, fetches)
    }

    @Test
    fun `same link from the server is not retried`() = runTest {
        assertNull(refresher { fetches++; EXPIRED }.refresh(EXPIRED))
    }

    @Test
    fun `only HTTP 404 counts as an expired link`() {
        assertTrue(HttpException(response(404)).isHttpNotFound())
        assertFalse(HttpException(response(500)).isHttpNotFound())
        assertFalse(IllegalStateException().isHttpNotFound())
    }

    private fun response(code: Int) = Response.Builder()
        .request(Request.Builder().url(EXPIRED).build())
        .protocol(Protocol.HTTP_1_1)
        .code(code)
        .message("")
        .build()

    private companion object {
        const val EXPIRED = "https://files/download/expired"
        const val FRESH = "https://files/download/fresh"
    }
}
