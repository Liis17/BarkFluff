package com.barkfluff.clientv2

import android.app.Application
import com.barkfluff.clientv2.di.AppContainer

/**
 * Application V2. Создаёт ручной DI-контейнер ([AppContainer]) с не-UI слоем из :core.
 */
class BarkFluffV2Application : Application() {

    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }
}
