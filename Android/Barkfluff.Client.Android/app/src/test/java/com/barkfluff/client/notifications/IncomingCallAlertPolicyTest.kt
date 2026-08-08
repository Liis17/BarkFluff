package com.barkfluff.client.notifications

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class IncomingCallAlertPolicyTest {

    @Test
    fun `missing notification permission is reported before fullscreen access`() {
        assertEquals(
            IncomingCallAlertIssue.NOTIFICATIONS_DISABLED,
            IncomingCallAlertPolicy.issue(
                notificationsEnabled = false,
                channelImportance = IncomingCallAlertPolicy.MIN_ALERTING_CHANNEL_IMPORTANCE,
                fullScreenIntentEnabled = false
            )
        )
    }

    @Test
    fun `missing fullscreen access is reported when notifications can be posted`() {
        assertEquals(
            IncomingCallAlertIssue.FULL_SCREEN_INTENT_DISABLED,
            IncomingCallAlertPolicy.issue(
                notificationsEnabled = true,
                channelImportance = IncomingCallAlertPolicy.MIN_ALERTING_CHANNEL_IMPORTANCE,
                fullScreenIntentEnabled = false
            )
        )
    }

    @Test
    fun `incoming call alert is ready when all required access is available`() {
        assertNull(
            IncomingCallAlertPolicy.issue(
                notificationsEnabled = true,
                channelImportance = IncomingCallAlertPolicy.MIN_ALERTING_CHANNEL_IMPORTANCE,
                fullScreenIntentEnabled = true
            )
        )
    }
}
