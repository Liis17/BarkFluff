package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.widget.Toast
import com.barkfluff.client.bft.BftConnectionString
import com.barkfluff.client.bft.BftProxyManager
import com.barkfluff.client.data.GlobalParam
import com.google.android.material.dialog.MaterialAlertDialogBuilder

internal object BftProxyLinkImporter {
    fun confirm(
        activity: Activity,
        value: String,
        onImported: () -> Unit,
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
                runCatching { manager.saveConnectionString(link, enable = true) }
                    .onFailure {
                        Toast.makeText(activity, R.string.bft_proxy_link_invalid, Toast.LENGTH_LONG).show()
                        return@setPositiveButton
                    }
                onImported()
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
}
