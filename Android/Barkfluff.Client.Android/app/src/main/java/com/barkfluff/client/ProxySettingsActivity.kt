package com.barkfluff.client

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Bundle
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.bft.BftProxyManager
import com.barkfluff.client.bft.BftProxyStatus
import com.barkfluff.client.bft.BftProxySessionPolicy
import com.barkfluff.client.databinding.ActivityProxySettingsBinding
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@AndroidEntryPoint
class ProxySettingsActivity : AppCompatActivity() {
    private lateinit var binding: ActivityProxySettingsBinding
    private val proxy by lazy { BftProxyManager.get(applicationContext) }
    private var renderingSwitch = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityProxySettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)
        binding.toolbar.setNavigationOnClickListener { onBackPressedDispatcher.onBackPressed() }
        binding.buttonPaste.setOnClickListener(::pasteLink)
        binding.buttonDelete.setOnClickListener(::confirmDelete)
        binding.buttonRetry.setOnClickListener { proxy.retryConnection() }
        binding.switchEnabled.setOnCheckedChangeListener { _, enabled ->
            if (renderingSwitch) return@setOnCheckedChangeListener
            if (enabled) {
                val state = proxy.settings
                val host = state.host
                val address = if (host != null && state.port != null) {
                    val formattedHost = if (':' in host) getString(R.string.bft_proxy_ipv6_host_format, host) else host
                    getString(R.string.bft_proxy_address_format, formattedHost, state.port)
                } else {
                    getString(R.string.bft_proxy_not_configured)
                }
                val connectionDialog = BftProxyConnectionDialog.show(
                    this,
                    state.name ?: getString(R.string.bft_proxy_name_unknown),
                    address,
                )
                lifecycleScope.launch {
                    val activeSession = GlobalParam(applicationContext).let {
                        BftProxySessionPolicy.hasSession(it.accessToken, it.refreshToken, it.userId)
                    }
                    val result = withContext(Dispatchers.IO) { runCatching { proxy.setEnabled(true) } }
                    result.onFailure { error ->
                        connectionDialog?.showError(
                            BftProxyLinkImporter.proxyErrorMessage(this@ProxySettingsActivity, error)
                                ?: getString(R.string.bft_proxy_enable_requires_configuration)
                        )
                        render(proxy.settings)
                    }.onSuccess {
                        connectionDialog?.dismiss()
                        if (!activeSession) BftProxyLinkImporter.restartAtNodeSelection(this@ProxySettingsActivity)
                    }
                }
            } else {
                runCatching { proxy.setEnabled(false) }
                    .onFailure {
                        Toast.makeText(this, R.string.bft_proxy_enable_requires_configuration, Toast.LENGTH_LONG).show()
                        render(proxy.settings)
                    }
                    .onSuccess {
                        if (BftProxySessionPolicy.needsServerSelectionForDirectBeacon(GlobalParam(applicationContext).socketBeacon)) {
                            Toast.makeText(this, R.string.bft_proxy_no_direct_beacon, Toast.LENGTH_LONG).show()
                            BftProxyLinkImporter.restartAtNodeSelection(this)
                        }
                    }
            }
        }
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                proxy.state.collect(::render)
            }
        }
    }

    private fun pasteLink(@Suppress("UNUSED_PARAMETER") view: android.view.View) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val clip: ClipData? = clipboard.primaryClip
        val value = clip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.toString()
        if (value.isNullOrBlank()) {
            Toast.makeText(this, R.string.bft_proxy_clipboard_empty, Toast.LENGTH_SHORT).show()
            return
        }
        BftProxyLinkImporter.confirm(
            this,
            value,
            onImported = { activeSession ->
                if (!activeSession) BftProxyLinkImporter.restartAtNodeSelection(this)
            }
        )
    }

    private fun confirmDelete(@Suppress("UNUSED_PARAMETER") view: android.view.View) {
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.bft_proxy_delete_title)
            .setMessage(R.string.bft_proxy_delete_message)
            .setNegativeButton(R.string.btn_cancel, null)
            .setPositiveButton(R.string.bft_proxy_delete) { _, _ -> proxy.clear() }
            .show()
    }

    private fun render(state: com.barkfluff.client.bft.BftProxyViewState) {
        binding.textName.text = state.name ?: getString(R.string.bft_proxy_name_unknown)
        val proxyHost = state.host
        val proxyPort = state.port
        binding.textAddress.text = if (proxyHost != null && proxyPort != null) {
            val host = if (':' in proxyHost) {
                getString(R.string.bft_proxy_ipv6_host_format, proxyHost)
            } else {
                proxyHost
            }
            getString(R.string.bft_proxy_address_format, host, proxyPort)
        } else getString(R.string.bft_proxy_not_configured)
        binding.textStatus.setText(
            when (state.status) {
                BftProxyStatus.DISABLED -> R.string.bft_proxy_status_disabled
                BftProxyStatus.CONNECTING -> R.string.bft_proxy_status_connecting
                BftProxyStatus.CONNECTED -> R.string.bft_proxy_status_connected
                BftProxyStatus.NO_CONNECTION -> R.string.bft_proxy_status_no_connection
                BftProxyStatus.KEY_REJECTED -> R.string.bft_proxy_status_key_rejected
                BftProxyStatus.SERVER_MISMATCH -> R.string.bft_proxy_status_server_mismatch
            }
        )
        renderingSwitch = true
        binding.switchEnabled.isChecked = state.enabled
        renderingSwitch = false
        binding.switchEnabled.isEnabled = state.configured
        binding.buttonDelete.isEnabled = state.configured
        binding.buttonRetry.visibility = if (state.status == BftProxyStatus.KEY_REJECTED ||
            state.status == BftProxyStatus.NO_CONNECTION || (state.enabled && state.status == BftProxyStatus.SERVER_MISMATCH)
        ) android.view.View.VISIBLE else android.view.View.GONE
    }
}
