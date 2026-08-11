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
}
