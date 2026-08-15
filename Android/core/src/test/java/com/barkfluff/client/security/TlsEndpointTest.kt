package com.barkfluff.client.security

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class TlsEndpointTest {

    @Test
    fun `adds HTTPS to a host and port without scheme`() {
        assertEquals(
            TlsEndpoint("beacon.example.test", 443),
            TlsEndpoint.parseAddress("Beacon.Example.Test:443")
        )
    }

    @Test
    fun `rejects cleartext endpoint`() {
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.parseAddress("http://beacon.example.test:80")
        }
    }

    @Test
    fun `rejects endpoint credentials and paths`() {
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.parseAddress("https://user@beacon.example.test:443/path")
        }
    }

    @Test
    fun `requires an explicit port`() {
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.parseAddress("beacon.example.test")
        }
    }

    @Test
    fun `rejects URL credentials and a missing host`() {
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.requireUrl("https://token@files.example.test/media")
        }
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.requireUrl("https://:443/media")
        }
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.requireUrl("https://files.example.test:0/media")
        }
    }

    @Test
    fun `normalizes a TLS URL endpoint with its default port`() {
        assertEquals(
            TlsEndpoint("livekit.example.test", 443),
            TlsEndpoint.parseUrlEndpoint("wss://LiveKit.Example.Test/rtc")
        )
    }
}
