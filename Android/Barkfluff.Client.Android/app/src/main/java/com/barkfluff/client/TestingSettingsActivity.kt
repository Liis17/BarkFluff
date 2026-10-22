package com.barkfluff.client

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.crypto.E2EBootstrap
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityTestingSettingsBinding
import kotlinx.coroutines.launch

class TestingSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityTestingSettingsBinding
    private lateinit var globalParam: GlobalParam

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityTestingSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)

        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }

        binding.switchShowIds.isChecked = globalParam.showIdsInProfile
        binding.switchShowServerAddresses.isChecked = globalParam.showServerAddressesInAbout
        binding.switchSecretChats.isChecked = globalParam.secretChatsEnabled
        binding.switchPrivateChats.isChecked = globalParam.privateChatsEnabled

        binding.switchShowIds.setOnCheckedChangeListener { _, isChecked ->
            globalParam.showIdsInProfile = isChecked
        }
        binding.switchShowServerAddresses.setOnCheckedChangeListener { _, isChecked ->
            globalParam.showServerAddressesInAbout = isChecked
        }
        binding.switchSecretChats.setOnCheckedChangeListener { _, isChecked ->
            globalParam.secretChatsEnabled = isChecked
            if (isChecked) {
                lifecycleScope.launch {
                    E2EBootstrap.ensurePrekeyBundleRegistered(applicationContext)
                    (application as BarkFluffApplication).secretChatRepository.retryPendingOperations()
                }
            }
        }
        binding.switchPrivateChats.setOnCheckedChangeListener { _, isChecked ->
            globalParam.privateChatsEnabled = isChecked
        }
    }
}
