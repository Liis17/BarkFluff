package com.barkfluff.client

import android.content.pm.PackageInstaller
import org.junit.Assert.assertEquals
import org.junit.Test

@Suppress("NewApi")
class UpdateInstallPermissionStateTest {

    @Test
    fun `enabled fullscreen access is granted to the install session`() {
        assertEquals(
            PackageInstaller.SessionParams.PERMISSION_STATE_GRANTED,
            fullScreenIntentPermissionState(canUseFullScreenIntent = true)
        )
    }

    @Test
    fun `disabled fullscreen access is denied to the install session`() {
        assertEquals(
            PackageInstaller.SessionParams.PERMISSION_STATE_DENIED,
            fullScreenIntentPermissionState(canUseFullScreenIntent = false)
        )
    }
}
