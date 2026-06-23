package com.barkfluff.client.calls

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.Bundle
import android.view.Gravity
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import com.google.android.material.button.MaterialButton
import kotlinx.coroutines.launch

class IncomingCallActivity : AppCompatActivity() {

    private lateinit var callId: String
    private lateinit var callerName: String
    private lateinit var mediaType: String
    private var dismissReceiverRegistered = false

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

        callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        callerName = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "BarkFluff" }
        mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()

        if (callId.isBlank()) {
            finish()
            return
        }

        setContentView(buildContent())
        registerDismissReceiver()

        if (intent.action == CallExtras.ACTION_ACCEPT_CALL) {
            acceptCall()
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

    private fun buildContent(): LinearLayout {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(dp(24), dp(24), dp(24), dp(24))
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        }

        root.addView(TextView(this).apply {
            text = callerName
            gravity = Gravity.CENTER
            setTextAppearance(android.R.style.TextAppearance_Material_Large)
            setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurface))
        })

        root.addView(TextView(this).apply {
            text = if (mediaType.equals("video", ignoreCase = true)) "Видеозвонок" else "Аудиозвонок"
            gravity = Gravity.CENTER
            setPadding(0, dp(8), 0, dp(32))
            setTextAppearance(android.R.style.TextAppearance_Material_Body1)
            setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
        })

        val actions = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
        }

        actions.addView(MaterialButton(this).apply {
            text = "Отклонить"
            setIconResource(R.drawable.ic_close)
            setBackgroundColor(resolveColor(android.R.attr.colorError))
            setOnClickListener { rejectCall() }
        })

        actions.addView(MaterialButton(this).apply {
            text = "Ответить"
            setIconResource(R.drawable.ic_phone)
            setOnClickListener { acceptCall() }
            (layoutParams as? LinearLayout.LayoutParams)?.marginStart = dp(16)
        })

        root.addView(actions)
        return root
    }

    private fun acceptCall() {
        lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch
            val response = (application as BarkFluffApplication).callRepository.accept(callId)
            response.onSuccess {
                NotificationHelper.dismissCall(this@IncomingCallActivity, callId)
                startActivity(Intent(this@IncomingCallActivity, CallActivity::class.java).apply {
                    putExtra(CallExtras.EXTRA_CALL_ID, callId)
                    putExtra(CallExtras.EXTRA_CALLER_NAME, callerName)
                    putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
                    putExtra(CallExtras.EXTRA_LIVEKIT_URL, it.livekitUrl)
                    putExtra(CallExtras.EXTRA_ACCESS_TOKEN, it.accessToken)
                })
                finish()
            }.onFailure {
                Toast.makeText(this@IncomingCallActivity, "Не удалось принять звонок", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun rejectCall() {
        lifecycleScope.launch {
            if (ensureCallsClient()) {
                (application as BarkFluffApplication).callRepository.reject(callId)
            }
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

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun resolveColor(attr: Int): Int {
        val out = android.util.TypedValue()
        theme.resolveAttribute(attr, out, true)
        return out.data
    }
}
