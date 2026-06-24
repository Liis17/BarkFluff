package com.barkfluff.client.calls

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.telecom.Connection
import android.telecom.ConnectionRequest
import android.telecom.ConnectionService
import android.telecom.DisconnectCause
import android.telecom.PhoneAccount
import android.telecom.PhoneAccountHandle
import android.telecom.TelecomManager
import android.telecom.VideoProfile
import com.barkfluff.client.notifications.NotificationHelper

class BarkFluffConnectionService : ConnectionService() {

    override fun onCreateIncomingConnection(
        connectionManagerPhoneAccount: PhoneAccountHandle?,
        request: ConnectionRequest
    ): Connection {
        val extras = request.extras
        val callId = extras.getString(CallExtras.EXTRA_CALL_ID).orEmpty()
        if (callId.isBlank()) {
            return Connection.createFailedConnection(DisconnectCause(DisconnectCause.ERROR))
        }

        val mediaType = extras.getString(CallExtras.EXTRA_MEDIA_TYPE).orEmpty().ifBlank { "audio" }
        val connection = BarkFluffConnection(
            context = applicationContext,
            callId = callId,
            callerName = extras.getString(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "BarkFluff" },
            callerUserId = extras.getLong(CallExtras.EXTRA_CALLER_USER_ID, 0L),
            chatId = extras.getString(CallExtras.EXTRA_CHAT_ID).orEmpty(),
            chatTitle = extras.getString(CallExtras.EXTRA_CHAT_TITLE).orEmpty(),
            mediaType = mediaType,
            videoState = extras.getInt(
                TelecomManager.EXTRA_INCOMING_VIDEO_STATE,
                if (mediaType.equals("video", ignoreCase = true)) VideoProfile.STATE_BIDIRECTIONAL else VideoProfile.STATE_AUDIO_ONLY
            )
        )
        CallTelecomRegistry.put(callId, connection)
        return connection
    }

    override fun onCreateIncomingConnectionFailed(
        connectionManagerPhoneAccount: PhoneAccountHandle?,
        request: ConnectionRequest
    ) {
        val callId = request.extras.getString(CallExtras.EXTRA_CALL_ID).orEmpty()
        if (callId.isNotBlank()) {
            NotificationHelper.dismissCall(applicationContext, callId)
        }
    }

    private class BarkFluffConnection(
        private val context: Context,
        private val callId: String,
        private val callerName: String,
        private val callerUserId: Long,
        private val chatId: String,
        private val chatTitle: String,
        private val mediaType: String,
        videoState: Int
    ) : Connection() {

        init {
            setAddress(
                Uri.fromParts(PhoneAccount.SCHEME_SIP, callerUserId.takeIf { it > 0L }?.toString() ?: callId, null),
                TelecomManager.PRESENTATION_ALLOWED
            )
            setCallerDisplayName(callerName, TelecomManager.PRESENTATION_ALLOWED)
            setConnectionProperties(PROPERTY_SELF_MANAGED)
            setAudioModeIsVoip(true)
            setVideoState(videoState)
            setRinging()
        }

        override fun onShowIncomingCallUi() {
            NotificationHelper.showIncomingCallNotification(
                context = context,
                callId = callId,
                callerName = callerName,
                mediaType = mediaType,
                callerUserId = callerUserId,
                chatId = chatId,
                chatTitle = chatTitle
            )
        }

        override fun onAnswer() {
            answerFromTelecom()
        }

        override fun onAnswer(videoState: Int) {
            answerFromTelecom()
        }

        override fun onReject() {
            rejectFromTelecom()
        }

        override fun onDisconnect() {
            rejectFromTelecom()
        }

        override fun onAbort() {
            rejectFromTelecom()
        }

        private fun answerFromTelecom() {
            context.startActivity(baseIncomingIntent().apply {
                action = CallExtras.ACTION_ACCEPT_CALL
            })
        }

        private fun rejectFromTelecom() {
            CallTelecomRegistry.disconnect(callId, DisconnectCause.REJECTED)
            context.sendBroadcast(Intent(context, CallActionReceiver::class.java).apply {
                action = CallExtras.ACTION_REJECT_CALL
                putExtra(CallExtras.EXTRA_CALL_ID, callId)
            })
        }

        private fun baseIncomingIntent(): Intent {
            return Intent(context, IncomingCallActivity::class.java).apply {
                putExtra(CallExtras.EXTRA_CALL_ID, callId)
                putExtra(CallExtras.EXTRA_CALLER_NAME, callerName)
                putExtra(CallExtras.EXTRA_CALLER_USER_ID, callerUserId)
                putExtra(CallExtras.EXTRA_CHAT_ID, chatId)
                putExtra(CallExtras.EXTRA_CHAT_TITLE, chatTitle)
                putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
            }
        }
    }
}