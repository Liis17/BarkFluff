package com.barkfluff.client

import com.barkfluff.client.data.ServerDataElement
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class ServerSelectionPolicyTest {
    private val server = ServerDataElement(ip = "https://node.example.test:443")

    @Test
    fun `list state distinguishes content empty and error`() {
        assertEquals(
            ServerListState.CONTENT,
            ServerSelectionPolicy.listState(Result.success(listOf(server))),
        )
        assertEquals(
            ServerListState.EMPTY,
            ServerSelectionPolicy.listState(Result.success(emptyList())),
        )
        assertEquals(
            ServerListState.ERROR,
            ServerSelectionPolicy.listState(Result.failure(IllegalStateException())),
        )
    }

    @Test
    fun `timeout returns null without waiting for a stalled request`() = runTest {
        val result = ServerSelectionPolicy.withTimeout(100L) {
            delay(1_000L)
            "response"
        }

        assertNull(result)
    }

    @Test
    fun `normalization exposes invalid endpoint as null`() {
        assertEquals(
            "https://node.example.test:443",
            ServerSelectionPolicy.normalizeEndpoint("node.example.test:443") { input ->
                "https://$input"
            },
        )
        assertNull(
            ServerSelectionPolicy.normalizeEndpoint("bad") {
                throw IllegalArgumentException("invalid endpoint")
            },
        )
        assertNull(
            ServerSelectionPolicy.normalizeEndpoint("node.example.test:443") { "" },
        )
    }
}
