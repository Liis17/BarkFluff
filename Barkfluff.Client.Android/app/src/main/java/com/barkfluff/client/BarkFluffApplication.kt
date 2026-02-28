package com.barkfluff.client

import android.app.Application
import com.barkfluff.client.grpc.RealtimeService

class BarkFluffApplication : Application() {

    lateinit var realtimeService: RealtimeService
        private set

    override fun onCreate() {
        super.onCreate()
        realtimeService = RealtimeService(applicationContext)
    }

    override fun onTerminate() {
        realtimeService.shutdown()
        super.onTerminate()
    }
}
