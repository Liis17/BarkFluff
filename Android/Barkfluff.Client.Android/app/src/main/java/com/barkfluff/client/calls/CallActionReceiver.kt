package com.barkfluff.client.calls

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class CallActionReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != CallExtras.ACTION_REJECT_CALL) return

        val callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        if (callId.isBlank()) return

        val pendingResult = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            try {
                val app = context.applicationContext as BarkFluffApplication
                if (app.grpcManager.callsClient == null) {
                    val callsAddress = GlobalParam(context).socketCalls
                    if (callsAddress.isNotBlank()) {
                        app.grpcManager.createCallsClient(callsAddress, context, includeDeviceInfo = true)
                    }
                }
                app.callRepository.reject(callId)
                NotificationHelper.dismissCall(context, callId)
            } catch (e: Exception) {
                Log.e("CallActionReceiver", "Failed to reject call $callId", e)
            } finally {
                pendingResult.finish()
            }
        }
    }
}
