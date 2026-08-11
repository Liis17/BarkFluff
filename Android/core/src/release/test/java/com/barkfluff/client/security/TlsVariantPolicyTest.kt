package com.barkfluff.client.security

import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Test

class TlsVariantPolicyTest {
    @Test
    fun `release rejects cleartext transport`() {
        assertFalse(TlsVariantPolicy.allowCleartext)
        assertThrows(IllegalArgumentException::class.java) {
            TlsEndpoint.parseAddress("http://localhost:8080", TlsVariantPolicy.allowCleartext)
        }
    }
}
