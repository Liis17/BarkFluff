package com.barkfluff.client

import android.graphics.Color
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.appcompat.app.AlertDialog
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import com.google.android.material.color.MaterialColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.progressindicator.CircularProgressIndicator

/** Shared connection feedback for the proxy settings and link-import flows. */
internal class BftProxyConnectionDialog private constructor(
    private val activity: ComponentActivity,
    private val dialog: AlertDialog,
    private val target: TextView,
    private val progress: CircularProgressIndicator,
    private val error: TextView,
) : DefaultLifecycleObserver {
    private var dismissed = false

    init {
        activity.lifecycle.addObserver(this)
    }

    fun showError(message: String) {
        if (dismissed || !dialog.isShowing || activity.isFinishing || activity.isDestroyed) return

        target.visibility = View.GONE
        progress.visibility = View.GONE
        error.text = message
        error.visibility = View.VISIBLE
        dialog.getButton(AlertDialog.BUTTON_POSITIVE)?.visibility = View.VISIBLE
    }

    fun dismiss() {
        if (dismissed) return
        dismissed = true
        dialog.dismiss()
        activity.lifecycle.removeObserver(this)
    }

    override fun onDestroy(owner: LifecycleOwner) = dismiss()

    companion object {
        fun show(
            activity: ComponentActivity,
            proxyName: String,
            proxyAddress: String,
        ): BftProxyConnectionDialog? {
            if (activity.isFinishing || activity.isDestroyed || activity.lifecycle.currentState == Lifecycle.State.DESTROYED) {
                return null
            }

            val density = activity.resources.displayMetrics.density
            fun dp(value: Int) = (value * density).toInt()

            val content = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(24), dp(8), dp(24), 0)
            }
            val target = TextView(activity).apply {
                text = activity.getString(R.string.bft_proxy_connecting_target, proxyName, proxyAddress)
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodyMedium)
                setTextColor(
                    MaterialColors.getColor(
                        activity,
                        com.google.android.material.R.attr.colorOnSurfaceVariant,
                        Color.DKGRAY,
                    )
                )
                gravity = Gravity.CENTER
            }
            val progress = CircularProgressIndicator(activity).apply {
                isIndeterminate = true
            }
            val error = TextView(activity).apply {
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodyMedium)
                setTextColor(MaterialColors.getColor(activity, androidx.appcompat.R.attr.colorError, Color.RED))
                visibility = View.GONE
            }
            content.addView(target, LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT,
            ))
            content.addView(progress, LinearLayout.LayoutParams(dp(48), dp(48)).apply {
                gravity = Gravity.CENTER_HORIZONTAL
                topMargin = dp(24)
                bottomMargin = dp(24)
            })
            content.addView(error, LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT,
            ).apply {
                topMargin = dp(16)
            })

            val dialog = MaterialAlertDialogBuilder(activity)
                .setTitle(R.string.bft_proxy_connecting_title)
                .setView(content)
                .setPositiveButton(R.string.dialog_ok, null)
                .create()
            dialog.setCancelable(false)
            dialog.setCanceledOnTouchOutside(false)
            val connectionDialog = BftProxyConnectionDialog(activity, dialog, target, progress, error)
            dialog.setOnShowListener {
                dialog.getButton(AlertDialog.BUTTON_POSITIVE)?.apply {
                    visibility = View.GONE
                    setOnClickListener { connectionDialog.dismiss() }
                }
            }

            dialog.show()
            return connectionDialog
        }
    }
}
