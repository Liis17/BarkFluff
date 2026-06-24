package com.barkfluff.client.calls

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.Bundle
import android.telecom.DisconnectCause
import android.view.WindowManager
import android.widget.ImageView
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.launch

class IncomingCallActivity : AppCompatActivity() {

    private lateinit var callId: String
    private lateinit var callerName: String
    private lateinit var mediaType: String
    private var callerUserId: Long = 0L
    private var dismissReceiverRegistered = false
    private var actionTaken = false

    private val dismissReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != CallExtras.ACTION_DISMISS_INCOMING_CALL) return
            if (intent.getStringExtra(CallExtras.EXTRA_CALL_ID) == callId) {
                NotificationHelper.dismissCall(this@IncomingCallActivity, callId)
                finish()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        configureLockScreenPresentation()

        callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        callerName = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "BarkFluff" }
        mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()
        callerUserId = intent.getLongExtra(CallExtras.EXTRA_CALLER_USER_ID, 0L)

        if (callId.isBlank()) {
            finish()
            return
        }

        (application as BarkFluffApplication).markCallPresented(callId)

        setContentView(R.layout.activity_incoming_call)
        bindViews()
        loadCallerInfo()
        registerDismissReceiver()

        if (intent.action == CallExtras.ACTION_ACCEPT_CALL) {
            acceptCall()
        }
    }

    private fun configureLockScreenPresentation() {
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setShowWhenLocked(true)
            setTurnScreenOn(true)
        } else {
            @Suppress("DEPRECATION")
            window.addFlags(
                WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED or
                    WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON
            )
        }
    }

    override fun onDestroy() {
        if (dismissReceiverRegistered) {
            unregisterReceiver(dismissReceiver)
            dismissReceiverRegistered = false
        }
        super.onDestroy()
    }

    private fun registerDismissReceiver() {
        val filter = IntentFilter(CallExtras.ACTION_DISMISS_INCOMING_CALL)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(dismissReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("DEPRECATION")
            registerReceiver(dismissReceiver, filter)
        }
        dismissReceiverRegistered = true
    }

    private fun bindViews() {
        findViewById<TextView>(R.id.callerName).text = callerName
        findViewById<TextView>(R.id.avatarInitials).text = initialsOf(callerName)
        findViewById<TextView>(R.id.callType).text =
            if (mediaType.equals("video", ignoreCase = true)) "Видеозвонок" else "Аудиозвонок"

        findViewById<ImageView>(R.id.rejectButton).setOnClickListener { rejectCall() }
        findViewById<ImageView>(R.id.acceptButton).setOnClickListener { acceptCall() }
    }

    /**
     * Подтягивает реальное имя и аватар звонящего по userId через профиль.
     * При отсутствии userId/профиля остаются инициалы из bindViews().
     */
    private fun loadCallerInfo() {
        if (callerUserId <= 0L) return

        val app = application as BarkFluffApplication
        val repository = ChatRepository(this, app.grpcManager)
        val avatarImage = findViewById<ImageView>(R.id.avatarImage)
        val avatarInitials = findViewById<TextView>(R.id.avatarInitials)

        lifecycleScope.launch {
            val user = repository.getUserData(callerUserId).getOrNull() ?: return@launch

            val displayName = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
            if (displayName.isNotBlank()) {
                callerName = displayName
                findViewById<TextView>(R.id.callerName).text = displayName
                avatarInitials.text = initialsOf(displayName)
            }

            val avatarFileId = user.profilePictureFileId
            if (avatarFileId.isNotBlank()) {
                AvatarLoader.loadByFileId(
                    imageView = avatarImage,
                    placeholderView = avatarInitials,
                    fileId = avatarFileId,
                    displayName = callerName,
                    userId = callerUserId,
                    size = 0
                ) {
                    repository.getFileDownloadUrl(avatarFileId).getOrNull()
                }
            }
        }
    }

    private fun initialsOf(name: String): String {
        val parts = name.trim().split(Regex("\\s+")).filter { it.isNotBlank() }
        return when {
            parts.isEmpty() -> "?"
            parts.size == 1 -> parts[0].take(1).uppercase()
            else -> (parts[0].take(1) + parts[1].take(1)).uppercase()
        }
    }

    private fun acceptCall() {
        if (actionTaken) return
        actionTaken = true
        lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch
            CallTelecomRegistry.markAnswering(callId)
            val response = (application as BarkFluffApplication).callRepository.accept(callId)
            response.onSuccess {
                CallTelecomRegistry.markActive(callId)
                NotificationHelper.clearIncomingCallAlert(this@IncomingCallActivity, callId)
                startActivity(Intent(this@IncomingCallActivity, CallActivity::class.java).apply {
                    putExtra(CallExtras.EXTRA_CALL_ID, callId)
                    putExtra(CallExtras.EXTRA_CALLER_NAME, callerName)
                    putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
                    putExtra(CallExtras.EXTRA_LIVEKIT_URL, it.livekitUrl)
                    putExtra(CallExtras.EXTRA_ACCESS_TOKEN, it.accessToken)
                })
                finish()
            }.onFailure {
                CallTelecomRegistry.clearAnswering(callId)
                actionTaken = false
                Toast.makeText(this@IncomingCallActivity, "Не удалось принять звонок", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun rejectCall() {
        if (actionTaken) return
        actionTaken = true
        lifecycleScope.launch {
            if (ensureCallsClient()) {
                (application as BarkFluffApplication).callRepository.reject(callId)
            }
            CallTelecomRegistry.disconnect(callId, DisconnectCause.REJECTED)
            NotificationHelper.dismissCall(this@IncomingCallActivity, callId)
            finish()
        }
    }

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val globalParam = GlobalParam(this)
        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) {
            Toast.makeText(this, "Сервер звонков не настроен", Toast.LENGTH_SHORT).show()
            return false
        }

        return app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true).isSuccess
    }
}
