package com.barkfluff.client.utils

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AvatarLoaderPolicyTest {

    @Test
    fun `expired cached URL is refreshed once for each cached URL`() {
        val guard = AvatarUrlRefreshGuard()

        assertTrue(guard.tryBegin("avatar-file", "https://files/expired-1"))
        assertFalse(guard.tryBegin("avatar-file", "https://files/expired-1"))
        assertTrue(guard.tryBegin("avatar-file", "https://files/expired-2"))
    }

    @Test
    fun `URL lookup retries once after an empty or failed response`() = runTest {
        var calls = 0
        var delays = 0

        val url = requestAvatarUrlWithSingleRetry(
            getUrl = {
                calls++
                if (calls == 1) null else "https://files/fresh"
            },
            retryDelay = { delays++ },
        )

        assertEquals("https://files/fresh", url)
        assertEquals(2, calls)
        assertEquals(1, delays)
    }

    @Test
    fun `failed URL retry stops after one retry`() = runTest {
        var calls = 0
        var delays = 0

        val url = requestAvatarUrlWithSingleRetry(
            getUrl = { calls++; error("offline") },
            retryDelay = { delays++ },
        )

        assertNull(url)
        assertEquals(2, calls)
        assertEquals(1, delays)
    }

    @Test
    fun `recycled image views reject a result tagged for the previous chat`() {
        assertFalse(isCurrentAvatarRequest("new-avatar", "new-avatar", "old-avatar"))
        assertTrue(isCurrentAvatarRequest("same-avatar", "same-avatar", "same-avatar"))
    }
}
