package com.barkfluff.client.grpc

import kotlin.random.Random
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ReconnectPolicyTest {

    @Test
    fun coldStartAndShortBackgroundReuseChannels() {
        assertFalse(shouldRecreateChannelsOnResume(networkChanged = false, idleMs = 5))
        assertFalse(shouldRecreateChannelsOnResume(networkChanged = false, idleMs = 10_000))
    }

    @Test
    fun networkChangeOrLongBackgroundRecreateChannels() {
        assertTrue(shouldRecreateChannelsOnResume(networkChanged = true, idleMs = 1_000))
        assertTrue(shouldRecreateChannelsOnResume(networkChanged = false, idleMs = STALE_CONNECTION_AFTER_BACKGROUND_MS))
    }

    @Test
    fun jitteredBackoffStaysWithinUpperHalf() {
        val random = Random(42)
        val samples = List(1_000) { jitteredBackoff(30_000, random) }

        assertTrue(samples.all { it in 15_000..30_000 })
        assertTrue("jitter must spread reconnects", samples.toSet().size > 100)
        assertEquals(0L, jitteredBackoff(0, random))
    }
}
