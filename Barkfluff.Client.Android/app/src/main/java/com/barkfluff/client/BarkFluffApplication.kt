package com.barkfluff.client

import android.app.Application
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.notifications.NotificationHelper
import com.google.android.material.color.DynamicColors

class BarkFluffApplication : Application() {

    lateinit var realtimeService: RealtimeService
        private set

    override fun onCreate() {
        super.onCreate()
        // Apply Material You dynamic colors system-wide (Android 12+)
        DynamicColors.applyToActivitiesIfAvailable(this)
        NotificationHelper.createChannels(this)
        realtimeService = RealtimeService(applicationContext)
    }

    override fun onTerminate() {
        realtimeService.shutdown()
        super.onTerminate()
    }
}
