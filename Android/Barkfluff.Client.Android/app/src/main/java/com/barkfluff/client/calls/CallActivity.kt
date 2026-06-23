package com.barkfluff.client.calls

import android.os.Bundle
import android.view.Gravity
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import com.google.android.material.button.MaterialButton
import kotlinx.coroutines.launch

class CallActivity : AppCompatActivity() {

    private lateinit var callId: String

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        if (callId.isBlank()) {
            finish()
            return
        }

        setContentView(buildContent())
    }

    private fun buildContent(): LinearLayout {
        val callerName = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "Звонок" }
        val mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()

        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(dp(24), dp(24), dp(24), dp(24))
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )

            addView(TextView(this@CallActivity).apply {
                text = callerName
                gravity = Gravity.CENTER
                setTextAppearance(android.R.style.TextAppearance_Material_Large)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurface))
            })

            addView(TextView(this@CallActivity).apply {
                text = if (mediaType.equals("video", ignoreCase = true)) {
                    "Соединение видеозвонка..."
                } else {
                    "Соединение аудиозвонка..."
                }
                gravity = Gravity.CENTER
                setPadding(0, dp(8), 0, dp(32))
                setTextAppearance(android.R.style.TextAppearance_Material_Body1)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
            })

            addView(MaterialButton(this@CallActivity).apply {
                text = "Завершить"
                setIconResource(R.drawable.ic_close)
                setBackgroundColor(resolveColor(android.R.attr.colorError))
                setOnClickListener { endCallAndClose() }
            })
        }
    }

    private fun endCallAndClose() {
        lifecycleScope.launch {
            if (ensureCallsClient()) {
                (application as BarkFluffApplication).callRepository.end(callId)
            }
            NotificationHelper.dismissCall(this@CallActivity, callId)
            finish()
        }
    }

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val globalParam = GlobalParam(this)
        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) return false

        return app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true).isSuccess
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun resolveColor(attr: Int): Int {
        val out = android.util.TypedValue()
        theme.resolveAttribute(attr, out, true)
        return out.data
    }
}
