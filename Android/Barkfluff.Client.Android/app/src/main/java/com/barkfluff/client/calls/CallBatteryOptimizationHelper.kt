package com.barkfluff.client.calls

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import android.util.Log

object CallBatteryOptimizationHelper {
    private const val TAG = "CallBatteryHelper"
    private const val PREFS = "call_power_prefs"
    private const val KEY_PROMPTED = "ignore_battery_optimization_prompted"

    fun requestIgnoreBatteryOptimizationsIfNeeded(activity: Activity) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return

        val powerManager = activity.getSystemService(PowerManager::class.java) ?: return
        if (powerManager.isIgnoringBatteryOptimizations(activity.packageName)) return

        val prefs = activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getBoolean(KEY_PROMPTED, false)) return

        val requestIntent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
            data = Uri.parse("package:${activity.packageName}")
        }
        val fallbackIntent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)

        val launched = runCatching {
            activity.startActivity(requestIntent)
            true
        }.getOrElse { requestError ->
            Log.w(TAG, "Direct battery optimization request failed", requestError)
            runCatching {
                activity.startActivity(fallbackIntent)
                true
            }.getOrElse { settingsError ->
                Log.w(TAG, "Battery optimization settings failed", settingsError)
                false
            }
        }

        if (launched) {
            prefs.edit().putBoolean(KEY_PROMPTED, true).apply()
        }
    }
}
