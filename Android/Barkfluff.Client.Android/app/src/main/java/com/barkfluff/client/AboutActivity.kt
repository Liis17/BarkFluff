package com.barkfluff.client

import android.os.Bundle
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityAboutBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.refreshServerInfoFromBeacon
import com.google.android.material.divider.MaterialDivider
import kotlinx.coroutines.launch

class AboutActivity : AppCompatActivity() {

    private lateinit var binding: ActivityAboutBinding
    private lateinit var globalParam: GlobalParam
    private val grpcManager = GrpcManager()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityAboutBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)

        setupToolbar()
        fillAppInfo()
        fillDeviceInfo()
        fillServerInfo()
        refreshServerInfo()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun fillAppInfo() {
        binding.textAppName.text = GlobalParam.getAppName()
        binding.textAppVersion.text = GlobalParam.getAppVersion(this)
    }

    private fun fillDeviceInfo() {
        binding.textDeviceName.text = GlobalParam.getDeviceName()
        binding.textDeviceId.text = globalParam.deviceId
        binding.textOsVersion.text = GlobalParam.getOsVersion()
    }

    private fun fillServerInfo() {
        val services = listOf(
            "Beacon" to globalParam.socketBeacon,
            "Identity" to globalParam.socketIdentity,
            "Users" to globalParam.socketUsers,
            "Files" to globalParam.socketFiles,
            "Messages" to globalParam.socketMessages,
            "Updates" to globalParam.socketUpdates,
            "Onliner" to globalParam.socketOnliner,
            "FastAuth" to globalParam.socketFastAuth,
            "Calls" to globalParam.socketCalls,
            "LiveKit" to globalParam.livekitUrl
        )

        val container = binding.serverInfoContainer
        container.removeAllViews()

        services.forEachIndexed { index, (name, address) ->
            if (index > 0) {
                val divider = MaterialDivider(this)
                divider.layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply {
                    marginStart = resources.getDimensionPixelSize(android.R.dimen.app_icon_size) / 3
                }
                container.addView(divider)
            }

            val row = LinearLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    resources.getDimensionPixelSize(android.R.dimen.app_icon_size)
                ).apply {
                    height = (56 * resources.displayMetrics.density).toInt()
                }
                orientation = LinearLayout.HORIZONTAL
                gravity = android.view.Gravity.CENTER_VERTICAL
                setPadding(
                    (16 * resources.displayMetrics.density).toInt(), 0,
                    (16 * resources.displayMetrics.density).toInt(), 0
                )
            }

            val nameView = TextView(this).apply {
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
                text = name
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodyLarge)
                setTextColor(getColorFromAttr(com.google.android.material.R.attr.colorOnSurface))
            }

            val addressView = TextView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                )
                text = address.ifBlank { "—" }
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodySmall)
                setTextColor(getColorFromAttr(com.google.android.material.R.attr.colorOnSurfaceVariant))
                setTextIsSelectable(true)
            }

            row.addView(nameView)
            row.addView(addressView)
            container.addView(row)
        }
    }

    private fun refreshServerInfo() {
        lifecycleScope.launch {
            if (refreshServerInfoFromBeacon(grpcManager, globalParam)) {
                fillServerInfo()
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }

    private fun getColorFromAttr(attr: Int): Int {
        val typedArray = obtainStyledAttributes(intArrayOf(attr))
        val color = typedArray.getColor(0, 0)
        typedArray.recycle()
        return color
    }
}
