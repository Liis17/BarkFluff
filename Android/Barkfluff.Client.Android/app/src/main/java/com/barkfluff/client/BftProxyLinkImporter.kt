package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.bft.BftConnectionString
import com.barkfluff.client.bft.BftProxyErrorCode
import com.barkfluff.client.bft.BftProxyException
import com.barkfluff.client.bft.BftProxyManager
import com.barkfluff.client.bft.BftProxySessionPolicy
import com.barkfluff.client.data.GlobalParam
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

internal object BftProxyLinkImporter {
    fun confirm(
        activity: ComponentActivity,
        value: String,
        onImported: (activeSession: Boolean) -> Unit,
        onDeclined: () -> Unit = {}
    ): Boolean {
        val link = value.trim()
        val parsed = try {
            BftConnectionString.parse(link)
        } catch (_: IllegalArgumentException) {
            Toast.makeText(activity, R.string.bft_proxy_link_invalid, Toast.LENGTH_LONG).show()
            return false
        }
        val manager = BftProxyManager.get(activity.applicationContext)
        val name = parsed.name?.takeIf(String::isNotBlank) ?: activity.getString(R.string.bft_proxy_name_unknown)
        val host = if (':' in parsed.host) "[${parsed.host}]" else parsed.host
        val address = "$host:${parsed.port}"
        parsed.staticPublicKey.fill(0)
        parsed.obfsKey.fill(0)

        val message = activity.getString(R.string.bft_proxy_import_details, name, address) +
            "\n\n" + activity.getString(R.string.bft_proxy_import_warning)
        val dialog = MaterialAlertDialogBuilder(activity)
            .setTitle(if (manager.settings.configured) R.string.bft_proxy_replace_title else R.string.bft_proxy_import_title)
            .setMessage(message)
            .setPositiveButton(if (manager.settings.configured) R.string.bft_proxy_replace else R.string.bft_proxy_import) { _, _ ->
                val globalParam = GlobalParam(activity.applicationContext)
                val activeSession = BftProxySessionPolicy.hasSession(
                    globalParam.accessToken,
                    globalParam.refreshToken,
                    globalParam.userId
                )
                if (activeSession) {
                    val connectionDialog = BftProxyConnectionDialog.show(activity, name, address)
                    activity.lifecycleScope.launch {
                        val result = withContext(Dispatchers.IO) {
                            runCatching { manager.saveConnectionStringAndEnable(link) }
                        }
                        result.onFailure { error ->
                            connectionDialog?.showError(
                                proxyErrorMessage(activity, error) ?: activity.getString(R.string.bft_proxy_link_invalid)
                            )
                        }.onSuccess {
                            connectionDialog?.dismiss()
                            onImported(true)
                        }
                    }
                } else {
                    runCatching { manager.saveConnectionString(link, enable = true) }
                        .onFailure {
                            Toast.makeText(activity, R.string.bft_proxy_link_invalid, Toast.LENGTH_LONG).show()
                            return@setPositiveButton
                        }
                    onImported(false)
                }
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ -> onDeclined() }
            .create()
        dialog.setOnCancelListener { onDeclined() }
        dialog.show()
        return true
    }

    fun restartAtNodeSelection(activity: Activity) {
        val intent = Intent(activity, SelectServerActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        activity.startActivity(intent)
        activity.finish()
    }

    internal fun proxyErrorMessage(activity: Activity, error: Throwable): String? {
        val proxyError = generateSequence(error) { it.cause }.filterIsInstance<BftProxyException>().firstOrNull()
            ?: return null
        val resource = when (proxyError.code) {
            BftProxyErrorCode.SERVER_MISMATCH -> R.string.bft_proxy_server_mismatch
            BftProxyErrorCode.SERVER_IDENTITY_UNAVAILABLE -> R.string.bft_proxy_node_check_failed
            BftProxyErrorCode.KEY_REJECTED -> R.string.bft_proxy_status_key_rejected
            BftProxyErrorCode.NO_CONNECTION -> R.string.bft_proxy_status_no_connection
            BftProxyErrorCode.SERVICE_UNAVAILABLE -> R.string.bft_proxy_status_no_connection
        }
        return activity.getString(resource)
    }
}
