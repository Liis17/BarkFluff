package com.barkfluff.client

import android.os.Bundle
import android.text.TextUtils
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityAboutBinding
import com.barkfluff.client.utils.ServicePingChecker
import com.barkfluff.client.utils.ServicePingResult
import com.google.android.material.divider.MaterialDivider
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.supervisorScope

class AboutActivity : AppCompatActivity() {

    private lateinit var binding: ActivityAboutBinding
    private lateinit var globalParam: GlobalParam
    private val servicePingChecker by lazy { ServicePingChecker(applicationContext) }
    private var pingResults = emptyMap<String, ServicePingResult>()
    private var isChecking = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityAboutBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)

        setupToolbar()
        fillAppInfo()
        fillDeviceInfo()
        setupServerInfo()
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

    private fun serverServices(): List<ServiceDefinition> = listOf(
        ServiceDefinition(getString(R.string.about_service_beacon), globalParam.socketBeacon),
        ServiceDefinition(getString(R.string.about_service_identity), globalParam.socketIdentity),
        ServiceDefinition(getString(R.string.about_service_users), globalParam.socketUsers),
        ServiceDefinition(getString(R.string.about_service_files), globalParam.socketFiles),
        ServiceDefinition(
            getString(R.string.about_service_files_media),
            globalParam.socketFilesMedia,
            fileEndpoint = true
        ),
        ServiceDefinition(getString(R.string.about_service_messages), globalParam.socketMessages),
        ServiceDefinition(getString(R.string.about_service_updates), globalParam.socketUpdates),
        ServiceDefinition(getString(R.string.about_service_onliner), globalParam.socketOnliner),
        ServiceDefinition(getString(R.string.about_service_fast_auth), globalParam.socketFastAuth),
        ServiceDefinition(getString(R.string.about_service_calls), globalParam.socketCalls),
        ServiceDefinition(getString(R.string.about_service_livekit), globalParam.livekitUrl, pingable = false)
    )

    private fun fillServerInfo() {
        val container = binding.serverInfoContainer
        container.removeAllViews()
        val services = serverServices()

        services.forEachIndexed { index, service ->
            if (index > 0) {
                val divider = MaterialDivider(this)
                divider.layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply {
                    marginStart = dp(16)
                }
                container.addView(divider)
            }

            val row = LinearLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                )
                minimumHeight = dp(56)
                orientation = LinearLayout.HORIZONTAL
                gravity = android.view.Gravity.CENTER_VERTICAL
                setPadding(
                    dp(16), dp(8), dp(16), dp(8)
                )
            }

            val details = LinearLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
                orientation = LinearLayout.VERTICAL
            }

            val nameView = TextView(this).apply {
                text = service.name
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodyLarge)
                setTextColor(getColorFromAttr(com.google.android.material.R.attr.colorOnSurface))
            }

            val addressView = TextView(this).apply {
                text = service.address.ifBlank { "—" }
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodySmall)
                setTextColor(getColorFromAttr(com.google.android.material.R.attr.colorOnSurfaceVariant))
                setTextIsSelectable(true)
                maxLines = 2
                ellipsize = TextUtils.TruncateAt.END
            }

            details.addView(nameView)
            details.addView(addressView)
            row.addView(details)

            val statusView = TextView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply {
                    marginStart = dp(12)
                }
                gravity = android.view.Gravity.END
                maxLines = 2
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodySmall)
                val (statusText, statusColor) = serviceStatus(service)
                text = statusText
                setTextColor(statusColor)
            }
            row.addView(statusView)
            container.addView(row)
        }
    }

    private fun serviceStatus(service: ServiceDefinition): Pair<String, Int> {
        val secondaryColor = getColorFromAttr(com.google.android.material.R.attr.colorOnSurfaceVariant)
        if (!service.pingable) {
            return getString(R.string.about_service_not_checked) to secondaryColor
        }
        if (service.address.isBlank()) {
            return getString(R.string.about_service_not_configured) to secondaryColor
        }
        if (isChecking) {
            return getString(R.string.about_service_checking) to secondaryColor
        }

        val result = pingResults[service.name]
            ?: return getString(R.string.about_service_not_checked) to secondaryColor
        return if (result.available) {
            getString(R.string.about_service_available, result.responseTimeMs) to
                ContextCompat.getColor(this, R.color.success)
        } else {
            getString(R.string.about_service_unavailable, result.responseTimeMs) to
                ContextCompat.getColor(this, R.color.error)
        }
    }

    private fun setupServerInfo() {
        val showServerAddresses = globalParam.showServerAddressesInAbout
        binding.serverInfoCard.visibility = if (showServerAddresses) View.VISIBLE else View.GONE
        if (!showServerAddresses) {
            return
        }

        fillServerInfo()
        binding.pingServerButton.setOnClickListener { pingServer() }
    }

    private fun pingServer() {
        if (isChecking) {
            return
        }

        isChecking = true
        pingResults = emptyMap()
        binding.pingServerButton.isEnabled = false
        binding.textPingResult.visibility = View.VISIBLE
        binding.textPingResult.text = getString(R.string.about_ping_checking)
        fillServerInfo()

        lifecycleScope.launch {
            try {
                val servicesToCheck = serverServices().filter {
                    it.pingable && it.address.isNotBlank()
                }
                val results = supervisorScope {
                    servicesToCheck.map { service ->
                        async(Dispatchers.IO) {
                            service.name to servicePingChecker.check(service.address, service.fileEndpoint)
                        }
                    }.awaitAll().toMap()
                }
                pingResults = results
                binding.textPingResult.text = if (servicesToCheck.isEmpty()) {
                    getString(R.string.about_ping_no_services)
                } else {
                    val availableCount = results.values.count { it.available }
                    getString(R.string.about_ping_result, availableCount, servicesToCheck.size)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                pingResults = emptyMap()
                binding.textPingResult.text = getString(R.string.about_ping_failed)
            } finally {
                isChecking = false
                binding.pingServerButton.isEnabled = true
                fillServerInfo()
            }
        }
    }

    override fun onDestroy() {
        // ConnectionPool.evictAll() закрывает TLS-сокеты синхронно (сетевой I/O),
        // поэтому с главного потока это падает с NetworkOnMainThreadException.
        Thread { servicePingChecker.close() }.start()
        super.onDestroy()
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private data class ServiceDefinition(
        val name: String,
        val address: String,
        val pingable: Boolean = true,
        val fileEndpoint: Boolean = false
    )

    private fun getColorFromAttr(attr: Int): Int {
        val typedArray = obtainStyledAttributes(intArrayOf(attr))
        val color = typedArray.getColor(0, 0)
        typedArray.recycle()
        return color
    }
}
