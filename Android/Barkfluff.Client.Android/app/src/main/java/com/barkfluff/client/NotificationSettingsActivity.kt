package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityNotificationSettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.launch

class NotificationSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityNotificationSettingsBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    private var isUpdatingFromServer = false

    companion object {
        private const val TAG = "NotificationSettings"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityNotificationSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager
        globalParam = GlobalParam(this)

        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }

        binding.switchNotifications.isChecked = globalParam.notificationsEnabled
        binding.switchNotifications.isEnabled = false

        loadNotificationsState()

        binding.switchNotifications.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingFromServer) return@setOnCheckedChangeListener
            updateNotificationsState(isChecked)
        }
    }

    private fun loadNotificationsState() {
        lifecycleScope.launch {
            val result = grpcManager.getNotificationsEnabled()

            if (result.isSuccess) {
                val enabled = result.getOrDefault(true)
                isUpdatingFromServer = true
                binding.switchNotifications.isChecked = enabled
                globalParam.notificationsEnabled = enabled
                isUpdatingFromServer = false
            } else {
                Log.e(TAG, "Ошибка загрузки статуса уведомлений", result.exceptionOrNull())
            }

            binding.switchNotifications.isEnabled = true
        }
    }

    private fun updateNotificationsState(enabled: Boolean) {
        binding.switchNotifications.isEnabled = false

        lifecycleScope.launch {
            val result = grpcManager.setNotificationsEnabled(enabled)

            if (result.isSuccess) {
                globalParam.notificationsEnabled = enabled
            } else {
                Log.e(TAG, "Ошибка обновления статуса уведомлений", result.exceptionOrNull())
                Toast.makeText(
                    this@NotificationSettingsActivity,
                    R.string.notification_update_error,
                    Toast.LENGTH_SHORT
                ).show()

                isUpdatingFromServer = true
                binding.switchNotifications.isChecked = !enabled
                isUpdatingFromServer = false
            }

            binding.switchNotifications.isEnabled = true
        }
    }
}
