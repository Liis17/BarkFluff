package com.barkfluff.client.notifications

internal enum class IncomingCallAlertIssue {
    NOTIFICATIONS_DISABLED,
    CHANNEL_NOT_ALERTING,
    FULL_SCREEN_INTENT_DISABLED
}

internal object IncomingCallAlertPolicy {
    // NotificationManager.IMPORTANCE_HIGH, kept here as a platform-independent value for tests.
    const val MIN_ALERTING_CHANNEL_IMPORTANCE = 4

    fun issue(
        notificationsEnabled: Boolean,
        channelImportance: Int,
        fullScreenIntentEnabled: Boolean
    ): IncomingCallAlertIssue? {
        if (!notificationsEnabled) return IncomingCallAlertIssue.NOTIFICATIONS_DISABLED
        if (channelImportance < MIN_ALERTING_CHANNEL_IMPORTANCE) {
            return IncomingCallAlertIssue.CHANNEL_NOT_ALERTING
        }
        if (!fullScreenIntentEnabled) return IncomingCallAlertIssue.FULL_SCREEN_INTENT_DISABLED
        return null
    }
}
