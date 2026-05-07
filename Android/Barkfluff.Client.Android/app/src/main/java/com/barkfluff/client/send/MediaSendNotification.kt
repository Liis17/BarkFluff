package com.barkfluff.client.send

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.os.Build
import androidx.core.app.NotificationCompat
import com.barkfluff.client.R

/**
 * Помощник для построения уведомлений об отправке медиа в чат.
 */
object MediaSendNotification {

    const val CHANNEL_ID = "media_send_channel"
    const val FOREGROUND_NOTIFICATION_ID = 4242

    fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val mgr = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            if (mgr.getNotificationChannel(CHANNEL_ID) == null) {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    "Отправка медиа",
                    NotificationManager.IMPORTANCE_LOW
                ).apply {
                    description = "Прогресс отправки фото и видео в чат"
                    setShowBadge(false)
                    enableVibration(false)
                    setSound(null, null)
                }
                mgr.createNotificationChannel(channel)
            }
        }
    }

    fun build(
        context: Context,
        title: String,
        text: String,
        progress: Int,
        indeterminate: Boolean
    ) = NotificationCompat.Builder(context, CHANNEL_ID)
        .setSmallIcon(R.drawable.ic_send_filled)
        .setContentTitle(title)
        .setContentText(text)
        .setProgress(100, progress.coerceIn(0, 100), indeterminate)
        .setOngoing(true)
        .setOnlyAlertOnce(true)
        .setCategory(NotificationCompat.CATEGORY_PROGRESS)
        .setPriority(NotificationCompat.PRIORITY_LOW)
        .setSilent(true)
        .build()
}
