package com.barkfluff.client.security

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TlsVariantPolicyTest {
    @Test
    fun `debug permits local cleartext transport`() {
        assertTrue(TlsVariantPolicy.allowCleartext)
        assertEquals(
            TlsEndpoint("localhost", 8080, TlsEndpoint.HTTP),
            TlsEndpoint.parseAddress("http://localhost:8080", TlsVariantPolicy.allowCleartext)
        )
    }
}
