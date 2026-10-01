package com.barkfluff.client.notifications

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.barkfluff.client.di.MessageGatewayEntryPoint
import dagger.hilt.android.EntryPointAccessors
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class MarkAsReadReceiver : BroadcastReceiver() {

    companion object {
        private const val TAG = "MarkAsReadReceiver"
    }

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != NotificationHelper.ACTION_MARK_AS_READ) return

        val target = NotificationHelper.currentTarget(context, intent) ?: return
        if (target.kind != NotificationHelper.KIND_REGULAR) return
        val chatId = target.chatId
        val messageId = target.eventId.toLongOrNull()?.takeIf { it > 0 } ?: return

        Log.d(TAG, "Mark as read: chatId=$chatId, messageId=$messageId")

        // Dismiss the notification (через NotificationHelper чтобы синхронизировать пул)
        NotificationHelper.dismissForChat(context, chatId, messageId)

        // Mark the message as read via gRPC
        if (messageId > 0) {
            val pendingResult = goAsync()
            CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
                try {
                    if (NotificationHelper.currentTarget(context, intent) == null) return@launch
                    EntryPointAccessors.fromApplication(
                        context.applicationContext,
                        MessageGatewayEntryPoint::class.java,
                    ).messageGateway().markAsRead(listOf(messageId))
                } finally {
                    pendingResult.finish()
                }
            }
        }
    }
}
