package com.barkfluff.client.calls

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.telecom.DisconnectCause
import android.util.Log
import com.barkfluff.client.domain.gateway.CallGateway
import com.barkfluff.client.notifications.NotificationHelper
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class CallActionReceiver : BroadcastReceiver() {

    @EntryPoint
    @InstallIn(SingletonComponent::class)
    interface Dependencies {
        fun callGateway(): CallGateway
    }

    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action
        if (action != CallExtras.ACTION_REJECT_CALL && action != CallExtras.ACTION_END_CALL) return

        val callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        if (callId.isBlank()) return

        val pendingResult = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            try {
                val gateway = EntryPointAccessors.fromApplication(
                    context.applicationContext,
                    Dependencies::class.java,
                ).callGateway()
                if (action == CallExtras.ACTION_END_CALL) {
                    gateway.end(callId)
                    CallTelecomRegistry.disconnect(callId, DisconnectCause.LOCAL)
                    CallForegroundService.stop(context)
                } else {
                    gateway.reject(callId)
                    CallTelecomRegistry.disconnect(callId, DisconnectCause.REJECTED)
                }
                NotificationHelper.dismissCall(context, callId)
            } catch (e: Exception) {
                Log.e("CallActionReceiver", "Failed to handle call action $action for $callId", e)
            } finally {
                pendingResult.finish()
            }
        }
    }
}
