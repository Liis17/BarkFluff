package com.barkfluff.client.notifications

import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.barkfluff.client.BarkFluffApplication

class MarkAsReadReceiver : BroadcastReceiver() {

    companion object {
        private const val TAG = "MarkAsReadReceiver"
    }

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != NotificationHelper.ACTION_MARK_AS_READ) return

        val chatId = intent.getStringExtra(NotificationHelper.EXTRA_CHAT_ID) ?: return
        val messageId = intent.getLongExtra(NotificationHelper.EXTRA_MESSAGE_ID, 0)

        Log.d(TAG, "Mark as read: chatId=$chatId, messageId=$messageId")

        // Dismiss the notification
        val manager = context.getSystemService(NotificationManager::class.java)
        manager.cancel(chatId.hashCode())

        // Mark the message as read via gRPC
        if (messageId > 0) {
            val app = context.applicationContext as BarkFluffApplication
            app.realtimeService.markAsRead(messageId)
        }
    }
}
