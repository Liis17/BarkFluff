package com.barkfluff.client.calls

import android.content.ComponentName
import android.content.Context
import android.net.Uri
import android.os.Bundle
import android.telecom.PhoneAccount
import android.telecom.PhoneAccountHandle
import android.telecom.TelecomManager
import android.telecom.VideoProfile
import android.util.Log
import com.barkfluff.client.R

object CallTelecomManager {
    private const val TAG = "CallTelecomManager"
    private const val PHONE_ACCOUNT_ID = "barkfluff_calls"

    fun registerPhoneAccount(context: Context) {
        runCatching {
            val telecomManager = context.getSystemService(TelecomManager::class.java)
            val handle = phoneAccountHandle(context)
            val label = context.getString(R.string.app_name)
            val account = PhoneAccount.builder(handle, label)
                .setCapabilities(PhoneAccount.CAPABILITY_SELF_MANAGED)
                .setShortDescription(label)
                .addSupportedUriScheme(PhoneAccount.SCHEME_SIP)
                .build()

            telecomManager.registerPhoneAccount(account)
        }.onFailure {
            Log.w(TAG, "Failed to register self-managed phone account", it)
        }
    }

    fun reportIncomingCall(
        context: Context,
        callId: String,
        callerName: String,
        mediaType: String,
        callerUserId: Long,
        chatId: String,
        chatTitle: String
    ): Boolean {
        if (callId.isBlank()) return false

        return runCatching {
            val appContext = context.applicationContext
            registerPhoneAccount(appContext)

            val telecomManager = appContext.getSystemService(TelecomManager::class.java)
            val handle = phoneAccountHandle(appContext)
            if (!telecomManager.isIncomingCallPermitted(handle)) {
                Log.w(TAG, "Incoming call is not permitted by Telecom: callId=$callId")
                return false
            }

            val videoState = if (mediaType.equals("video", ignoreCase = true)) {
                VideoProfile.STATE_BIDIRECTIONAL
            } else {
                VideoProfile.STATE_AUDIO_ONLY
            }
            val address = Uri.fromParts(
                PhoneAccount.SCHEME_SIP,
                callerUserId.takeIf { it > 0L }?.toString() ?: callId,
                null
            )

            val extras = Bundle().apply {
                putString(CallExtras.EXTRA_CALL_ID, callId)
                putString(CallExtras.EXTRA_CALLER_NAME, callerName)
                putLong(CallExtras.EXTRA_CALLER_USER_ID, callerUserId)
                putString(CallExtras.EXTRA_CHAT_ID, chatId)
                putString(CallExtras.EXTRA_CHAT_TITLE, chatTitle)
                putString(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
                putParcelable(TelecomManager.EXTRA_INCOMING_CALL_ADDRESS, address)
                putInt(TelecomManager.EXTRA_INCOMING_VIDEO_STATE, videoState)
            }

            telecomManager.addNewIncomingCall(handle, extras)
            true
        }.onFailure {
            Log.w(TAG, "Failed to report incoming call to Telecom: callId=$callId", it)
        }.getOrDefault(false)
    }

    fun phoneAccountHandle(context: Context): PhoneAccountHandle {
        return PhoneAccountHandle(
            ComponentName(context.applicationContext, BarkFluffConnectionService::class.java),
            PHONE_ACCOUNT_ID
        )
    }
}