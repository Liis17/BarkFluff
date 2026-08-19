package com.barkfluff.client

import android.app.Application
import android.app.DownloadManager
import android.content.Intent
import android.net.ConnectivityManager
import android.net.Network
import android.util.Log
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.calls.CallExtras
import com.barkfluff.client.calls.IncomingCallActivity
import com.barkfluff.client.calls.CallRepository
import com.barkfluff.client.calls.CallTelecomManager
import com.barkfluff.client.calls.CallTelecomRegistry
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.notifications.NotificationHelper
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
import dagger.hilt.android.HiltAndroidApp
import java.io.File
import javax.inject.Inject
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.launch
import java.util.concurrent.TimeUnit

/**
 * Зависимости приходят из Hilt ([AppModule]) — свойства ниже делегируют к синглтонам графа,
 * поэтому существующие касты (application as BarkFluffApplication).* не меняются.
 */
@HiltAndroidApp
class BarkFluffApplication : Application() {

    @Inject lateinit var grpcManager: GrpcManager

    @Inject lateinit var chatCacheRepository: ChatCacheRepository

    @Inject lateinit var chatDraftRepository: ChatDraftRepository

    @Inject lateinit var realtimeService: RealtimeService

    @Inject lateinit var signalStore: BarkFluffSignalStore

    @Inject lateinit var prekeyManager: PrekeyManager

    @Inject lateinit var privateChatRepository: PrivateChatRepository

    @Inject lateinit var secretChatRepository: SecretChatRepository

    @Inject lateinit var callRepository: CallRepository

    @Inject lateinit var callEventsService: CallEventsService

    private val applicationScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var callEventsUiJob: Job? = null
    private lateinit var connectivityManager: ConnectivityManager
    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            applicationScope.launch(Dispatchers.IO) { chatDraftRepository.flushAll() }
        }
    }

    @Volatile
    private var presentedIncomingCallId: String? = null

    fun markCallPresented(callId: String) {
        if (callId.isNotBlank() && presentedIncomingCallId == null) {
            presentedIncomingCallId = callId
        }
    }

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
        System.loadLibrary("sqlcipher")
        // Применяем выбранный язык приложения до создания UI.
        // Для "system" сбрасывается override → используется системная локаль.
        LocaleManager.apply(GlobalParam(this).appLanguage)
        // Чистим APK обновления, оставшийся в Downloads после установки прошлой версии
        cleanupPendingUpdate()
        // Apply Material You dynamic colors system-wide (Android 12+)
        DynamicColors.applyToActivitiesIfAvailable(this)
        NotificationHelper.createChannels(this)
        CallTelecomManager.registerPhoneAccount(this)
        connectivityManager = getSystemService(ConnectivityManager::class.java)
        connectivityManager.registerDefaultNetworkCallback(networkCallback)

        // E2E-инфраструктура (приватные + секретные чаты) приходит из Hilt (AppModule).

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
                applicationScope.launch(Dispatchers.IO) { chatDraftRepository.flushAll() }
                startCallEventsUiBridge()
                callEventsService.resume()
            }

            override fun onStop(owner: LifecycleOwner) {
                cameFromBackground = true
                realtimeService.pause()
                stopCallEventsUiBridge()
                if (!CallTelecomRegistry.hasActiveCall()) {
                    callEventsService.pause()
                }
            }
        })
    }

    override fun onTerminate() {
        connectivityManager.unregisterNetworkCallback(networkCallback)
        realtimeService.shutdown()
        stopCallEventsUiBridge()
        callEventsService.shutdown()
        applicationScope.cancel()
        grpcManager.shutdown()
        super.onTerminate()
    }

    private fun startCallEventsUiBridge() {
        if (callEventsUiJob?.isActive == true) return
        callEventsUiJob = applicationScope.launch {
            callEventsService.events.collect { event ->
                handleCallEvent(event)
            }
        }
    }

    private fun stopCallEventsUiBridge() {
        callEventsUiJob?.cancel()
        callEventsUiJob = null
    }

    private fun handleCallEvent(event: CallsApiOuterClass.CallEvent) {
        when (event.eventCase) {
            CallsApiOuterClass.CallEvent.EventCase.INCOMING -> presentIncomingCall(event.incoming)
            CallsApiOuterClass.CallEvent.EventCase.ACCEPTED -> acceptIncomingCall(event.accepted.callId)
            CallsApiOuterClass.CallEvent.EventCase.REJECTED -> dismissIncomingCall(event.rejected.callId)
            CallsApiOuterClass.CallEvent.EventCase.ENDED -> dismissIncomingCall(event.ended.callId)
            else -> Unit
        }
    }

    private fun presentIncomingCall(event: CallsApiOuterClass.IncomingCallEvent) {
        if (event.callId.isBlank() || presentedIncomingCallId == event.callId || CallTelecomRegistry.isAnsweringOrActive(event.callId)) return
        presentedIncomingCallId = event.callId

        val mediaType = if (event.mediaType == CallsApiOuterClass.CallMediaType.CALL_MEDIA_VIDEO) {
            "video"
        } else {
            "audio"
        }
        val displayName = getString(
            if (event.chatId.isNotBlank()) R.string.call_group_title else R.string.call_incoming_title
        )

        CallTelecomManager.reportIncomingCall(
            context = applicationContext,
            callId = event.callId,
            callerName = displayName,
            mediaType = mediaType,
            callerUserId = event.callerUserId,
            chatId = event.chatId,
            chatTitle = displayName
        )

        NotificationHelper.showIncomingCallNotification(
            context = applicationContext,
            callId = event.callId,
            callerName = displayName,
            mediaType = mediaType,
            callerUserId = event.callerUserId,
            chatId = event.chatId,
            chatTitle = displayName
        )

        try {
            startActivity(Intent(this, IncomingCallActivity::class.java).apply {
                putExtra(CallExtras.EXTRA_CALL_ID, event.callId)
                putExtra(CallExtras.EXTRA_CALLER_NAME, displayName)
                putExtra(CallExtras.EXTRA_CALLER_USER_ID, event.callerUserId)
                putExtra(CallExtras.EXTRA_CHAT_ID, event.chatId)
                putExtra(CallExtras.EXTRA_CHAT_TITLE, displayName)
                putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
            })
        } catch (e: Exception) {
            Log.w("BarkFluffApplication", "Failed to open incoming call UI", e)
        }
    }

    private fun acceptIncomingCall(callId: String) {
        if (callId.isBlank()) return
        val localAnsweringOrActive = CallTelecomRegistry.isAnsweringOrActive(callId)
        val hadIncomingUi = presentedIncomingCallId == callId
        if (hadIncomingUi) {
            presentedIncomingCallId = null
        }
        NotificationHelper.clearIncomingCallAlert(applicationContext, callId)
        if (!localAnsweringOrActive && (hadIncomingUi || CallTelecomRegistry.hasConnection(callId))) {
            dismissIncomingCall(callId)
        }
    }
    private fun dismissIncomingCall(callId: String) {
        if (callId.isBlank()) return
        if (presentedIncomingCallId == callId) {
            presentedIncomingCallId = null
        }
        NotificationHelper.dismissCall(applicationContext, callId)
        sendBroadcast(Intent(CallExtras.ACTION_DISMISS_INCOMING_CALL).apply {
            setPackage(packageName)
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
        })
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
