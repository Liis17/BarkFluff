package com.barkfluff.client.notifications

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.barkfluff.client.di.OutgoingQueueEntryPoint
import dagger.hilt.android.EntryPointAccessors
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CancellationException
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

        Log.d(TAG, "Queue read range: chatId=$chatId, cutoff=$messageId")
        val pendingResult = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            try {
                acceptMarkAsRead(context.applicationContext, intent)
            } finally {
                pendingResult.finish()
            }
        }
    }

    internal suspend fun acceptMarkAsRead(
        context: Context,
        intent: Intent,
        enqueue: suspend (MessageNotificationTarget) -> Unit = { target ->
            EntryPointAccessors.fromApplication(
                context.applicationContext,
                OutgoingQueueEntryPoint::class.java,
            ).outgoingMessageQueue().enqueueChatReadRange(
                target.chatId,
                target.eventId.toLong(),
                target.scopeId,
            )
        },
    ) {
        val target = NotificationHelper.currentTarget(context, intent) ?: return
        if (target.kind != NotificationHelper.KIND_REGULAR ||
            (target.eventId.toLongOrNull() ?: 0) <= 0) return
        try {
            if (NotificationHelper.currentTarget(context, intent) != target) return
            enqueue(target)
            if (NotificationHelper.currentTarget(context, intent) == target) {
                // A durable row now owns this cutoff; remove only incoming entries at or below it.
                NotificationHelper.dismissReadMessages(context, target)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.e(TAG, "Unable to persist chat read range", e)
            if (NotificationHelper.currentTarget(context, intent) == target) {
                NotificationHelper.showReadError(context, target)
            }
        }
    }
}
