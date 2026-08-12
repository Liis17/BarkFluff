package com.barkfluff.client

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityWelcomeBinding
import com.barkfluff.client.utils.LegalDocsRepository
import com.google.android.material.color.DynamicColors

class WelcomeActivity : AppCompatActivity() {

    private lateinit var binding: ActivityWelcomeBinding

    // Permission launcher for normal permissions
    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.values.all { it }
        if (allGranted) {
            Toast.makeText(this, R.string.welcome_permissions_granted, Toast.LENGTH_SHORT).show()
        }
        navigateToMain()
    }

    // Launcher for MANAGE_EXTERNAL_STORAGE permission (Android 11+)
    private val manageStorageLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (Environment.isExternalStorageManager()) {
                Toast.makeText(this, R.string.welcome_storage_granted, Toast.LENGTH_SHORT).show()
                requestRemainingPermissions()
            } else {
                Toast.makeText(this, R.string.welcome_storage_denied, Toast.LENGTH_SHORT).show()
                navigateToMain()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityWelcomeBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Edge-to-edge: инсеты вешаем на contentPanel, а не на android.R.id.content.
        // Иначе весь корень уезжает вниз, над ним остаётся полоса windowBackground другого
        // цвета, а декоративные круги обрезаются по нижней границе статус-бара.
        ViewCompat.setOnApplyWindowInsetsListener(binding.contentPanel) { v, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.updatePadding(
                top = systemBars.top + WELCOME_TOP_PADDING_DP.dpToPx(),
                bottom = systemBars.bottom + WELCOME_BOTTOM_PADDING_DP.dpToPx()
            )
            insets
        }

        setupClickListeners()
    }

    /**
     * Согласие требуется, если оно ещё не давалось или соглашение обновилось с прошлого раза.
     * Редакцию берём из самого документа, а не из версии приложения.
     */
    private fun legalConsentRequired(): Boolean {
        val current = runCatching { LegalDocsRepository.revision(this) }.getOrDefault("")
        return current.isNotEmpty() && current != GlobalParam(this).acceptedLegalRevision
    }

    private fun requestLegalConsent() {
        supportFragmentManager.setFragmentResultListener(
            LegalConsentBottomSheet.RESULT_KEY,
            this
        ) { _, result ->
            if (result.getBoolean(LegalConsentBottomSheet.RESULT_ACCEPTED)) {
                GlobalParam(this).acceptedLegalRevision =
                    runCatching { LegalDocsRepository.revision(this) }.getOrDefault("")
                navigateToSelectServer()
            }
        }
        LegalConsentBottomSheet.forConsent()
            .show(supportFragmentManager, LegalConsentBottomSheet.TAG)
    }

    private fun setupClickListeners() {
        binding.startButton.setOnClickListener {
            if (legalConsentRequired()) requestLegalConsent() else navigateToSelectServer()
        }

        binding.privacyLink.setOnClickListener {
            LegalConsentBottomSheet.forReading(LegalConsentBottomSheet.TAB_PRIVACY)
                .show(supportFragmentManager, LegalConsentBottomSheet.TAG)
        }
    }

    private fun navigateToSelectServer() {
        val intent = Intent(this, SelectServerActivity::class.java)
        startActivity(intent)
        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out)
        finish() // Блокируем возврат на эту страницу
    }

    private fun requestAllPermissions() {
        // First, request MANAGE_EXTERNAL_STORAGE for Android 11+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (!Environment.isExternalStorageManager()) {
                val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                    data = Uri.parse("package:$packageName")
                }
                manageStorageLauncher.launch(intent)
                return
            }
        }

        // If we already have storage access or on older Android, request remaining permissions
        requestRemainingPermissions()
    }

    private fun requestRemainingPermissions() {
        val permissionsToRequest = mutableListOf<String>()

        // Add notification permission for Android 13+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.POST_NOTIFICATIONS
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.POST_NOTIFICATIONS)
            }
        }

        // Add media permissions for Android 13+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_MEDIA_IMAGES
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_MEDIA_IMAGES)
            }
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_MEDIA_VIDEO
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_MEDIA_VIDEO)
            }
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_MEDIA_AUDIO
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_MEDIA_AUDIO)
            }
        } else if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.Q) {
            // For Android 10 and below
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_EXTERNAL_STORAGE
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_EXTERNAL_STORAGE)
            }
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
            }
        }

        if (permissionsToRequest.isNotEmpty()) {
            permissionLauncher.launch(permissionsToRequest.toTypedArray())
        } else {
            navigateToMain()
        }
    }

    private fun navigateToMain() {
        val intent = Intent(this, SelectServerActivity::class.java)
        startActivity(intent)
        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out)
    }

    private fun Int.dpToPx(): Int = (this * resources.displayMetrics.density).toInt()

    companion object {
        /** Отступ hero-блока от статус-бара; складывается с системным инсетом. */
        private const val WELCOME_TOP_PADDING_DP = 24
        private const val WELCOME_BOTTOM_PADDING_DP = 24
    }
}
