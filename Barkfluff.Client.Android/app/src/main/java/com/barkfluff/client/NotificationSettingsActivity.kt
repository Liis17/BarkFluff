package com.barkfluff.client

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityNotificationSettingsBinding

class NotificationSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityNotificationSettingsBinding
    private lateinit var globalParam: GlobalParam

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityNotificationSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)

        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }

        binding.switchNotifications.isChecked = globalParam.notificationsEnabled

        binding.switchNotifications.setOnCheckedChangeListener { _, isChecked ->
            globalParam.notificationsEnabled = isChecked
        }
    }
}
