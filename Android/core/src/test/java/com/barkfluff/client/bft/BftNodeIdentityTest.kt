package com.barkfluff.client.bft

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BftNodeIdentityTest {
    private val saved = listOf(
        BftServiceEndpoint(1, "https://beacon.old.example:443"),
        BftServiceEndpoint(2, "https://Identity.Old.Example:443"),
        BftServiceEndpoint(3, "users.old.example:443"),
        BftServiceEndpoint(4, "https://messages.old.example:443")
    )

    @Test
    fun `endpoint scheme and hostname casing normalize before node comparison`() {
        assertTrue(
            BftNodeIdentity.matches(
                saved,
                mapOf(
                    2 to "identity.old.example",
                    3 to "https://USERS.old.example:443",
                    4 to "messages.old.example:443"
                )
            )
        )
    }

    @Test
    fun `any beacon endpoint mismatch rejects the node`() {
        assertFalse(
            BftNodeIdentity.matches(
                saved,
                mapOf(2 to "identity.old.example:443", 3 to "users.other.example:443", 4 to "messages.old.example:443")
            )
        )
    }

    @Test
    fun `missing or malformed beacon endpoints fail closed`() {
        assertFalse(BftNodeIdentity.matches(saved, emptyMap()))
        assertFalse(BftNodeIdentity.matches(saved, mapOf(2 to "not a host")))
        assertFalse(BftNodeIdentity.matches(saved, mapOf(99 to "identity.old.example:443")))
    }

    @Test
    fun `session policy validates authenticated users and keeps proxy selection address empty`() {
        assertTrue(BftProxySessionPolicy.hasSession("access", null))
        assertTrue(BftProxySessionPolicy.hasSession(null, "refresh"))
        assertTrue(BftProxySessionPolicy.hasSession(null, null, userId = 42L))
        assertFalse(BftProxySessionPolicy.hasSession(null, null))
        assertFalse(BftProxySessionPolicy.mayApplyServerInfo(proxyEnabled = true, hasSession = true))
        assertTrue(BftProxySessionPolicy.mayApplyServerInfo(proxyEnabled = true, hasSession = false))
        assertEquals("", BftProxySessionPolicy.beaconAddressForSelection(true, "https://bft.internal:443"))
        assertEquals("https://beacon.example:443", BftProxySessionPolicy.beaconAddressForSelection(false, "https://beacon.example:443"))
        assertTrue(BftProxySessionPolicy.needsServerSelectionForDirectBeacon(""))
        assertFalse(BftProxySessionPolicy.needsServerSelectionForDirectBeacon("https://beacon.example:443"))
    }
}
