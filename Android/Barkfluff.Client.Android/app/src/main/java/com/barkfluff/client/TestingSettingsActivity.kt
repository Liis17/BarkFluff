package com.barkfluff.client

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityTestingSettingsBinding

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
        binding.switchSecretChats.isChecked = globalParam.secretChatsEnabled

        binding.switchShowIds.setOnCheckedChangeListener { _, isChecked ->
            globalParam.showIdsInProfile = isChecked
        }
        binding.switchSecretChats.setOnCheckedChangeListener { _, isChecked ->
            globalParam.secretChatsEnabled = isChecked
        }
    }
}
