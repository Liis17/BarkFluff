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
import android.view.animation.AccelerateDecelerateInterpolator
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.barkfluff.client.databinding.ActivityWelcomeBinding
import com.google.android.material.color.DynamicColors

class WelcomeActivity : AppCompatActivity() {

    private lateinit var binding: ActivityWelcomeBinding

    // Permission launcher for normal permissions
    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.values.all { it }
        if (allGranted) {
            Toast.makeText(this, "Все разрешения предоставлены", Toast.LENGTH_SHORT).show()
        }
        navigateToMain()
    }

    // Launcher for MANAGE_EXTERNAL_STORAGE permission (Android 11+)
    private val manageStorageLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (Environment.isExternalStorageManager()) {
                Toast.makeText(this, "Доступ к хранилищу предоставлен", Toast.LENGTH_SHORT).show()
                requestRemainingPermissions()
            } else {
                Toast.makeText(this, "Доступ к хранилищу не предоставлен", Toast.LENGTH_SHORT).show()
                navigateToMain()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityWelcomeBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setupAnimations()
        setupClickListeners()
    }

    private fun setupAnimations() {
        val duration = 600L
        val interpolator = AccelerateDecelerateInterpolator()

        // Content card animation
        binding.contentCard.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(duration)
            .setInterpolator(interpolator)
            .start()

        // Logo animation with delay
        binding.logoImage.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(duration)
            .setStartDelay(100)
            .setInterpolator(interpolator)
            .start()

        // Title animation
        binding.titleText.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(duration)
            .setStartDelay(200)
            .setInterpolator(interpolator)
            .start()

        // Subtitle animation
        binding.subtitleText.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(duration)
            .setStartDelay(300)
            .setInterpolator(interpolator)
            .start()

        // Description animation
        binding.descriptionText.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(duration)
            .setStartDelay(400)
            .setInterpolator(interpolator)
            .start()

        // Button animation
        binding.startButton.animate()
            .alpha(1f)
            .translationY(0f)
            .setDuration(duration)
            .setStartDelay(500)
            .setInterpolator(interpolator)
            .start()

        // Footer animation
        binding.footerPanel.animate()
            .alpha(1f)
            .setDuration(duration)
            .setStartDelay(600)
            .setInterpolator(interpolator)
            .start()
    }

    private fun setupClickListeners() {
        binding.startButton.setOnClickListener {
            navigateToSelectServer()
        }

        binding.aboutLink.setOnClickListener {
            showAboutDialog()
        }

        binding.privacyLink.setOnClickListener {
            openPrivacyPolicy()
        }

        binding.helpLink.setOnClickListener {
            showHelp()
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

    private fun showAboutDialog() {
        Toast.makeText(this, "Barkfluff\nВерсия: 1.0", Toast.LENGTH_LONG).show()
    }

    private fun openPrivacyPolicy() {
        // TODO: Open privacy policy URL
        Toast.makeText(this, "Политика конфиденциальности", Toast.LENGTH_SHORT).show()
    }

    private fun showHelp() {
        // TODO: Open help
        Toast.makeText(this, "Справка", Toast.LENGTH_SHORT).show()
    }
}
