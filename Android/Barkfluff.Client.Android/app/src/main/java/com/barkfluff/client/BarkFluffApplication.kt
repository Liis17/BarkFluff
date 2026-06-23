package com.barkfluff.client

import android.app.Application
import android.app.DownloadManager
import android.util.Log
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import com.barkfluff.client.calls.CallRepository
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.notifications.RealtimeSideEffectsImpl
import com.barkfluff.client.repository.PrivateChatRepository
import com.barkfluff.client.repository.SecretChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.LocaleManager
import com.barkfluff.client.utils.StickerCache
import com.barkfluff.client.widget.WidgetRefreshWorker
import com.google.android.material.color.DynamicColors
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import java.io.File
import java.util.concurrent.TimeUnit

class BarkFluffApplication : Application() {

    lateinit var grpcManager: GrpcManager
        private set

    lateinit var realtimeService: RealtimeService
        private set

    lateinit var signalStore: BarkFluffSignalStore
        private set

    lateinit var prekeyManager: PrekeyManager
        private set

    lateinit var privateChatRepository: PrivateChatRepository
        private set

    lateinit var secretChatRepository: SecretChatRepository
        private set

    lateinit var callRepository: CallRepository
        private set

    /**
     * Флаг: приложение было свёрнуто и снова развёрнуто.
     * Устанавливается в true при уходе в фон, сбрасывается компонентами при обработке.
     */
    @Volatile
    var cameFromBackground: Boolean = false

    /**
     * Отложенный deep link URI, который будет обработан после инициализации gRPC в MainActivity.
     */
    @Volatile
    var pendingDeepLink: android.net.Uri? = null

    override fun onCreate() {
        super.onCreate()
        // Применяем выбранный язык приложения до создания UI.
        // Для "system" сбрасывается override → используется системная локаль.
        LocaleManager.apply(GlobalParam(this).appLanguage)
        // Чистим APK обновления, оставшийся в Downloads после установки прошлой версии
        cleanupPendingUpdate()
        // Apply Material You dynamic colors system-wide (Android 12+)
        DynamicColors.applyToActivitiesIfAvailable(this)
        NotificationHelper.createChannels(this)
        grpcManager = GrpcManager()
        realtimeService = RealtimeService(
            applicationContext,
            grpcManager,
            RealtimeSideEffectsImpl(applicationContext, grpcManager)
        )

        // E2E-инфраструктура (приватные + секретные чаты)
        signalStore = BarkFluffSignalStore(applicationContext)
        prekeyManager = PrekeyManager(applicationContext, signalStore)
        privateChatRepository = PrivateChatRepository(applicationContext, grpcManager)
        secretChatRepository = SecretChatRepository(applicationContext, grpcManager, signalStore)
        callRepository = CallRepository(grpcManager)

        // Инициализируем персистентный кэш URL файлов
        AvatarLoader.initializeCache(this)

        // Инициализируем кэш медиафайлов
        FileCache.init(this)

        // Инициализируем кэш стикеров
        StickerCache.init(this)

        // Periodic refresh App Widget'ов раз в 30 минут — fallback когда приложение убито
        scheduleWidgetRefreshWorker()

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

    private fun scheduleWidgetRefreshWorker() {
        try {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()
            val request = PeriodicWorkRequestBuilder<WidgetRefreshWorker>(30, TimeUnit.MINUTES)
                .setConstraints(constraints)
                .build()
            WorkManager.getInstance(this).enqueueUniquePeriodicWork(
                "widget-refresh",
                ExistingPeriodicWorkPolicy.KEEP,
                request
            )
        } catch (e: Exception) {
            Log.w("BarkFluffApplication", "Failed to schedule widget refresh worker", e)
        }
    }

    private fun cleanupPendingUpdate() {
        try {
            val gp = GlobalParam(this)
            val path = gp.pendingUpdateApkPath
            val downloadId = gp.pendingUpdateDownloadId

            if (path != null) {
                runCatching { File(path).takeIf { it.exists() }?.delete() }
            }

            if (downloadId > 0) {
                runCatching {
                    val dm = getSystemService(DOWNLOAD_SERVICE) as DownloadManager
                    dm.remove(downloadId)
                }
            }

            runCatching {
                File(cacheDir, "update_pending.apk").takeIf { it.exists() }?.delete()
            }

            if (path != null || downloadId > 0) {
                gp.clearPendingUpdate()
            }
        } catch (e: Exception) {
            Log.w("BarkFluffApplication", "cleanupPendingUpdate failed", e)
        }
    }
}
