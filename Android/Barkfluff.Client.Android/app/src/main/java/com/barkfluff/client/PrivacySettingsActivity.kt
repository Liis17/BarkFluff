package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.databinding.ActivityPrivacySettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch

class PrivacySettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityPrivacySettingsBinding
    private lateinit var grpcManager: GrpcManager

    private var isUpdatingSwitch = false
    private var currentSettings: UsersApiOuterClass.PrivacySettings? = null

    companion object {
        private const val TAG = "PrivacySettings"
        private val VISIBILITY_LABELS = arrayOf("Все", "Друзья", "Никто")
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPrivacySettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }

        setupClickListeners()
    }

    override fun onResume() {
        super.onResume()
        loadPrivacySettings()
    }

    private fun loadPrivacySettings() {
        lifecycleScope.launch {
            val result = grpcManager.getPrivacySettings()
            if (result.isSuccess) {
                currentSettings = result.getOrNull()
                applySettingsToUi(currentSettings!!)
            } else {
                Log.e(TAG, "Ошибка загрузки настроек приватности", result.exceptionOrNull())
                Toast.makeText(
                    this@PrivacySettingsActivity,
                    "Ошибка загрузки настроек",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    private fun applySettingsToUi(settings: UsersApiOuterClass.PrivacySettings) {
        isUpdatingSwitch = true

        binding.switchProfileOnSite.isChecked = settings.profileVisibleOnSite
        binding.switchSearchVisible.isChecked = settings.searchVisible

        binding.tvAvatarVisibilityValue.text = visibilityLabel(settings.avatarVisibility)
        binding.tvBioVisibilityValue.text = visibilityLabel(settings.bioVisibility)
        binding.tvEmailVisibilityValue.text = visibilityLabel(settings.emailVisibility)
        binding.tvOnlineVisibilityValue.text = visibilityLabel(settings.onlineVisibility)

        isUpdatingSwitch = false
    }

    private fun setupClickListeners() {
        binding.switchProfileOnSite.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingSwitch) return@setOnCheckedChangeListener
            saveSettings { it.toBuilder().setProfileVisibleOnSite(isChecked).build() }
        }

        binding.switchSearchVisible.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingSwitch) return@setOnCheckedChangeListener
            saveSettings { it.toBuilder().setSearchVisible(isChecked).build() }
        }

        binding.itemAvatarVisibility.setOnClickListener {
            showVisibilityDialog("Видимость аватара", currentSettings?.avatarVisibility) { selected ->
                binding.tvAvatarVisibilityValue.text = visibilityLabel(selected)
                saveSettings { it.toBuilder().setAvatarVisibility(selected).build() }
            }
        }

        binding.itemBioVisibility.setOnClickListener {
            showVisibilityDialog("Видимость описания", currentSettings?.bioVisibility) { selected ->
                binding.tvBioVisibilityValue.text = visibilityLabel(selected)
                saveSettings { it.toBuilder().setBioVisibility(selected).build() }
            }
        }

        binding.itemEmailVisibility.setOnClickListener {
            showVisibilityDialog("Видимость почты", currentSettings?.emailVisibility) { selected ->
                binding.tvEmailVisibilityValue.text = visibilityLabel(selected)
                saveSettings { it.toBuilder().setEmailVisibility(selected).build() }
            }
        }

        binding.itemOnlineVisibility.setOnClickListener {
            showVisibilityDialog("Видимость онлайна", currentSettings?.onlineVisibility) { selected ->
                binding.tvOnlineVisibilityValue.text = visibilityLabel(selected)
                saveSettings { it.toBuilder().setOnlineVisibility(selected).build() }
            }
        }
    }

    private fun showVisibilityDialog(
        title: String,
        current: UsersApiOuterClass.ProfileFieldVisibility?,
        onSelected: (UsersApiOuterClass.ProfileFieldVisibility) -> Unit
    ) {
        val currentIdx = current?.number ?: 0
        MaterialAlertDialogBuilder(this)
            .setTitle(title)
            .setSingleChoiceItems(VISIBILITY_LABELS, currentIdx) { dialog, which ->
                val selected = UsersApiOuterClass.ProfileFieldVisibility.forNumber(which)
                    ?: UsersApiOuterClass.ProfileFieldVisibility.ALL
                onSelected(selected)
                dialog.dismiss()
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun saveSettings(transform: (UsersApiOuterClass.PrivacySettings) -> UsersApiOuterClass.PrivacySettings) {
        val base = currentSettings ?: return
        val updated = transform(base)
        currentSettings = updated

        lifecycleScope.launch {
            val result = grpcManager.updatePrivacySettings(updated)
            if (result.isFailure) {
                Log.e(TAG, "Ошибка сохранения настроек приватности", result.exceptionOrNull())
                Toast.makeText(
                    this@PrivacySettingsActivity,
                    "Ошибка сохранения",
                    Toast.LENGTH_SHORT
                ).show()
                loadPrivacySettings()
            }
        }
    }

    private fun visibilityLabel(visibility: UsersApiOuterClass.ProfileFieldVisibility?): String {
        return when (visibility) {
            UsersApiOuterClass.ProfileFieldVisibility.ALL -> "Все"
            UsersApiOuterClass.ProfileFieldVisibility.FRIENDS -> "Друзья"
            UsersApiOuterClass.ProfileFieldVisibility.NONE -> "Никто"
            else -> "Все"
        }
    }
}
