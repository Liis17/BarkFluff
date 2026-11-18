package com.barkfluff.client.calls

import org.junit.Assert.assertEquals
import org.junit.Test

class LiveKitHttpUrlTest {
    @Test
    fun `wss url maps to https with same host`() {
        val url = livekitHttpUrl("wss://calls2.barkfluff.com")

        assertEquals("https", url.scheme)
        assertEquals("calls2.barkfluff.com", url.host)
        assertEquals(443, url.port)
    }

    @Test
    fun `ws url maps to http keeping port`() {
        val url = livekitHttpUrl("ws://livekit:7880")

        assertEquals("http", url.scheme)
        assertEquals("livekit", url.host)
        assertEquals(7880, url.port)
    }
}
