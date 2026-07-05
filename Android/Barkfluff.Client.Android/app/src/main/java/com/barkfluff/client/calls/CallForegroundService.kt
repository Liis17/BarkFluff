package com.barkfluff.client.calls

import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import com.barkfluff.client.notifications.NotificationHelper

class CallForegroundService : Service() {

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopForegroundCompat()
                stopSelf()
                return START_NOT_STICKY
            }
            ACTION_UPDATE, ACTION_START -> startOrUpdate(intent)
        }
        return START_STICKY
    }

    private fun startOrUpdate(intent: Intent) {
        val callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        if (callId.isBlank()) {
            stopSelf()
            return
        }

        val title = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "Звонок" }
        val mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()
        val livekitUrl = intent.getStringExtra(CallExtras.EXTRA_LIVEKIT_URL).orEmpty()
        val accessToken = intent.getStringExtra(CallExtras.EXTRA_ACCESS_TOKEN).orEmpty()
        val cameraEnabled = intent.getBooleanExtra(EXTRA_CAMERA_ENABLED, mediaType.equals("video", ignoreCase = true))
        val screenShareEnabled = intent.getBooleanExtra(EXTRA_SCREEN_SHARE_ENABLED, false)

        val notification = NotificationHelper.buildOngoingCallNotification(
            context = this,
            callId = callId,
            title = title,
            mediaType = mediaType,
            livekitUrl = livekitUrl,
            accessToken = accessToken
        )

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, foregroundType(cameraEnabled, screenShareEnabled))
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun foregroundType(cameraEnabled: Boolean, screenShareEnabled: Boolean): Int {
        var type = ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE or ServiceInfo.FOREGROUND_SERVICE_TYPE_PHONE_CALL
        if (cameraEnabled) type = type or ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA
        if (screenShareEnabled) type = type or ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
        return type
    }

    private fun stopForegroundCompat() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE)
        } else {
            @Suppress("DEPRECATION")
            stopForeground(true)
        }
    }

    companion object {
        private const val ACTION_START = "com.barkfluff.client.calls.ACTION_FOREGROUND_START"
        private const val ACTION_UPDATE = "com.barkfluff.client.calls.ACTION_FOREGROUND_UPDATE"
        private const val ACTION_STOP = "com.barkfluff.client.calls.ACTION_FOREGROUND_STOP"
        private const val EXTRA_CAMERA_ENABLED = "extra_camera_enabled"
        private const val EXTRA_SCREEN_SHARE_ENABLED = "extra_screen_share_enabled"
        private const val NOTIFICATION_ID = 41014

        fun start(
            context: Context,
            callId: String,
            title: String,
            mediaType: String,
            livekitUrl: String,
            accessToken: String,
            cameraEnabled: Boolean,
            screenShareEnabled: Boolean = false
        ) {
            val intent = baseIntent(context, ACTION_START, callId, title, mediaType, livekitUrl, accessToken)
                .putExtra(EXTRA_CAMERA_ENABLED, cameraEnabled)
                .putExtra(EXTRA_SCREEN_SHARE_ENABLED, screenShareEnabled)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun update(
            context: Context,
            callId: String,
            title: String,
            mediaType: String,
            livekitUrl: String,
            accessToken: String,
            cameraEnabled: Boolean,
            screenShareEnabled: Boolean
        ) {
            val intent = baseIntent(context, ACTION_UPDATE, callId, title, mediaType, livekitUrl, accessToken)
                .putExtra(EXTRA_CAMERA_ENABLED, cameraEnabled)
                .putExtra(EXTRA_SCREEN_SHARE_ENABLED, screenShareEnabled)
            context.startService(intent)
        }

        fun stop(context: Context) {
            context.startService(Intent(context, CallForegroundService::class.java).setAction(ACTION_STOP))
        }

        private fun baseIntent(
            context: Context,
            action: String,
            callId: String,
            title: String,
            mediaType: String,
            livekitUrl: String,
            accessToken: String
        ): Intent = Intent(context, CallForegroundService::class.java).apply {
            this.action = action
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(CallExtras.EXTRA_CALLER_NAME, title)
            putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
            putExtra(CallExtras.EXTRA_LIVEKIT_URL, livekitUrl)
            putExtra(CallExtras.EXTRA_ACCESS_TOKEN, accessToken)
        }
    }
}
