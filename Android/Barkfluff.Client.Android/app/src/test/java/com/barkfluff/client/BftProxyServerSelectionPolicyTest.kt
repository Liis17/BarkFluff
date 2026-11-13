package com.barkfluff.client

import com.barkfluff.client.bft.BftProxySessionPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BftProxyServerSelectionPolicyTest {
    @Test
    fun `authenticated proxy selection never reapplies beacon endpoints`() {
        assertFalse(BftProxySessionPolicy.mayApplyServerInfo(proxyEnabled = true, hasSession = true))
        assertTrue(BftProxySessionPolicy.mayApplyServerInfo(proxyEnabled = true, hasSession = false))
        assertTrue(BftProxySessionPolicy.mayApplyServerInfo(proxyEnabled = false, hasSession = true))
    }

    @Test
    fun `first proxy selection never persists the synthetic beacon authority`() {
        assertEquals("", BftProxySessionPolicy.beaconAddressForSelection(true, "https://bft.internal:443"))
        assertEquals(
            "https://beacon.example:443",
            BftProxySessionPolicy.beaconAddressForSelection(false, "https://beacon.example:443")
        )
        assertTrue(BftProxySessionPolicy.needsServerSelectionForDirectBeacon(""))
        assertFalse(BftProxySessionPolicy.needsServerSelectionForDirectBeacon("https://beacon.example:443"))
    }
}
