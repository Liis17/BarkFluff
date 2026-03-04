package com.barkfluff.client

import android.app.Application
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.color.DynamicColors

class BarkFluffApplication : Application() {

    lateinit var grpcManager: GrpcManager
        private set

    lateinit var realtimeService: RealtimeService
        private set

    /**
     * Флаг: приложение было свёрнуто и снова развёрнуто.
     * Устанавливается в true при уходе в фон, сбрасывается компонентами при обработке.
     */
    @Volatile
    var cameFromBackground: Boolean = false

    override fun onCreate() {
        super.onCreate()
        // Apply Material You dynamic colors system-wide (Android 12+)
        DynamicColors.applyToActivitiesIfAvailable(this)
        NotificationHelper.createChannels(this)
        grpcManager = GrpcManager()
        realtimeService = RealtimeService(applicationContext, grpcManager)
        
        // Инициализируем персистентный кэш URL файлов
        AvatarLoader.initializeCache(this)

        // Подписываемся на lifecycle всего приложения (foreground/background)
        // resume() вызывается когда ЛЮБАЯ activity приложения выходит на передний план
        // pause() вызывается когда ВСЕ activity приложения уходят в фон
        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) {
                realtimeService.resume()
            }

            override fun onStop(owner: LifecycleOwner) {
                cameFromBackground = true
                realtimeService.pause()
            }
        })
    }

    override fun onTerminate() {
        realtimeService.shutdown()
        grpcManager.shutdown()
        super.onTerminate()
    }
}
