package com.barkfluff.client

import android.app.Application
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.notifications.NotificationHelper

class BarkFluffApplication : Application() {

    lateinit var realtimeService: RealtimeService
        private set

    override fun onCreate() {
        super.onCreate()
        NotificationHelper.createChannels(this)
        realtimeService = RealtimeService(applicationContext)
    }

    override fun onTerminate() {
        realtimeService.shutdown()
        super.onTerminate()
    }
}
