package com.barkfluff.messenger

import android.app.Application
import dagger.hilt.android.HiltAndroidApp

@HiltAndroidApp
class BarkFluffApp : Application() {
    override fun onCreate() {
        super.onCreate()
    }
}
