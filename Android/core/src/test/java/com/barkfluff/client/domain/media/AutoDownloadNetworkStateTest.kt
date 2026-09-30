package com.barkfluff.client.domain.media

import org.junit.Assert.assertEquals
import org.junit.Test

class AutoDownloadNetworkStateTest {
    @Test
    fun `wifi including vpn qualifies only without a cellular transport`() {
        val state = AutoDownloadNetworkState()
        state.available("vpn")
        assertEquals(AutoDownloadNetwork.UNAVAILABLE, state.network.value)
        state.capabilities("vpn", wifi = true, cellular = false)
        assertEquals(AutoDownloadNetwork.WIFI, state.network.value)
        state.capabilities("vpn", wifi = true, cellular = true)
        assertEquals(AutoDownloadNetwork.OTHER, state.network.value)
        state.capabilities("vpn", wifi = false, cellular = true)
        assertEquals(AutoDownloadNetwork.OTHER, state.network.value)
        state.capabilities("vpn", wifi = false, cellular = false)
        assertEquals(AutoDownloadNetwork.OTHER, state.network.value)
        state.lost("vpn")
        assertEquals(AutoDownloadNetwork.UNAVAILABLE, state.network.value)
    }

    @Test
    fun `late events for the old default network cannot replace the current state`() {
        val state = AutoDownloadNetworkState()
        state.available("old")
        state.available("new")
        state.capabilities("new", wifi = true, cellular = false)
        state.lost("old")
        state.capabilities("old", wifi = false, cellular = true)
        assertEquals(AutoDownloadNetwork.WIFI, state.network.value)
    }
}
