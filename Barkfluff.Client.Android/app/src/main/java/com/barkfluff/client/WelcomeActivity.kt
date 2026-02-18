package com.barkfluff.client

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.View
import android.view.animation.AccelerateDecelerateInterpolator
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.barkfluff.client.databinding.ActivityWelcomeBinding

class WelcomeActivity : AppCompatActivity() {

    private lateinit var binding: ActivityWelcomeBinding

    // Permission launcher
    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.values.all { it }
        if (allGranted) {
            Toast.makeText(this, "Все разрешения предоставлены", Toast.LENGTH_SHORT).show()
        } else {
            Toast.makeText(this, "Некоторые разрешения не предоставлены", Toast.LENGTH_SHORT).show()
        }
        // Переход к MainActivity после запроса разрешений
        navigateToMain()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
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
            requestPermissions()
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

    private fun requestPermissions() {
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
        } else {
            // For Android 12 and below
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
        val intent = Intent(this, MainActivity::class.java)
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
