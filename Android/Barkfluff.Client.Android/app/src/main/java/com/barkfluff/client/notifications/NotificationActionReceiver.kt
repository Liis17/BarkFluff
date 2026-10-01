package com.barkfluff.client.notifications

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import android.widget.Toast
import androidx.core.app.RemoteInput
import com.barkfluff.client.R
import com.barkfluff.client.di.OutgoingQueueEntryPoint
import com.barkfluff.client.send.NotificationReplyMetadata
import com.barkfluff.client.send.SendJob
import dagger.hilt.android.EntryPointAccessors
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

internal data class MessageNotificationTarget(
    val scopeId: String,
    val kind: String,
    val chatId: String,
    val eventId: String,
    val threadId: String = chatId
)

/** Only accepts text into the durable outbox; all network work belongs to the worker. */
class NotificationActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val target = NotificationHelper.currentTarget(context, intent) ?: return
        when (intent.action) {
            NotificationHelper.ACTION_HIDE -> NotificationHelper.hideEncryptedNotification(context, target)
            NotificationHelper.ACTION_REPLY -> {
                if (target.kind != NotificationHelper.KIND_REGULAR ||
                    (target.eventId.toLongOrNull() ?: 0) <= 0 || replyText(intent).isNullOrBlank()) return
                val pendingResult = goAsync()
                CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
                    try {
                        acceptReply(context.applicationContext, intent)
                    } finally {
                        pendingResult.finish()
                    }
                }
            }
        }
    }

    internal suspend fun acceptReply(
        context: Context,
        intent: Intent,
        enqueue: suspend (SendJob) -> Unit = { request ->
            EntryPointAccessors.fromApplication(context.applicationContext, OutgoingQueueEntryPoint::class.java)
                .outgoingMessageQueue().enqueue(request)
            Unit
        }
    ) {
        val target = NotificationHelper.currentTarget(context, intent) ?: return
        if (target.kind != NotificationHelper.KIND_REGULAR) return
        val messageId = target.eventId.toLongOrNull()?.takeIf { it > 0 } ?: return
        val text = replyText(intent)?.takeIf(String::isNotBlank) ?: return
        try {
            enqueue(SendJob(target.chatId, target.chatId, text, emptyList(),
                replyId = 0, draftGeneration = null,
                notificationReply = NotificationReplyMetadata(target.scopeId, messageId)))
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.e("NotificationReply", "Unable to accept reply: ${e::class.java.simpleName}")
            if (NotificationHelper.currentTarget(context, intent) != null) {
                withContext(Dispatchers.Main) {
                    NotificationHelper.showReplyError(context, target)
                    val strings = NotificationHelper.localizedContext(context)
                    Toast.makeText(strings, strings.getString(R.string.notification_reply_failed), Toast.LENGTH_LONG).show()
                }
            }
            return
        }
        try {
            NotificationHelper.dismissAcceptedReply(context, target)
        } catch (e: Exception) {
            Log.e("NotificationReply", "Unable to close accepted reply notification: ${e::class.java.simpleName}")
        }
    }

    private fun replyText(intent: Intent) = RemoteInput.getResultsFromIntent(intent)
        ?.getCharSequence(NotificationHelper.REPLY_INPUT_KEY)?.toString()
}
