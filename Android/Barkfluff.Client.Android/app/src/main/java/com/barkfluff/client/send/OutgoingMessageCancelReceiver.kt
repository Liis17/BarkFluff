package com.barkfluff.client.send

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.barkfluff.client.di.OutgoingQueueEntryPoint
import dagger.hilt.android.EntryPointAccessors
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/** Cancels one durable operation from the foreground upload notification. */
class OutgoingMessageCancelReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != ACTION_CANCEL) return
        val operationId = intent.getStringExtra(EXTRA_OPERATION_ID).orEmpty()
        if (operationId.isBlank()) return

        val pendingResult = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            try {
                EntryPointAccessors.fromApplication(
                    context.applicationContext,
                    OutgoingQueueEntryPoint::class.java,
                ).outgoingMessageQueue().cancel(operationId)
            } finally {
                pendingResult.finish()
            }
        }
    }

    companion object {
        const val ACTION_CANCEL = "com.barkfluff.client.send.CANCEL_OUTGOING_MESSAGE"
        const val EXTRA_OPERATION_ID = "operation_id"
    }
}
