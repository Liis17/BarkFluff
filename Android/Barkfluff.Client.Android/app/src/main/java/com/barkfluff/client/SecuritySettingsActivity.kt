package com.barkfluff.client

import android.content.Intent
import android.graphics.BitmapFactory
import android.os.Bundle
import android.util.Base64
import android.util.Log
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import barkfluff.identity.IdentityApiOuterClass
import com.barkfluff.client.databinding.ActivitySecuritySettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlinx.coroutines.launch

class SecuritySettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivitySecuritySettingsBinding
    private lateinit var grpcManager: GrpcManager
    private var isUpdatingSwitch = false

    companion object {
        private const val TAG = "SecuritySettings"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivitySecuritySettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

        setupToolbar()
        setupClickListeners()
    }

    override fun onResume() {
        super.onResume()
        loadOtpStatus()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun setupClickListeners() {
        binding.itemChangePassword.setOnClickListener {
            showChangePasswordDialog()
        }

        binding.switchTwoFactorApp.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingSwitch) return@setOnCheckedChangeListener
            if (isChecked) {
                enableAuthenticator2FA()
            } else {
                disableAuthenticator2FA()
            }
        }

        binding.switchTwoFactorEmail.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingSwitch) return@setOnCheckedChangeListener
            if (isChecked) {
                enableEmail2FA()
            } else {
                disableEmail2FA()
            }
        }
    }

    private fun loadOtpStatus() {
        lifecycleScope.launch {
            val result = grpcManager.listOtpVerification()
            if (result.isSuccess) {
                val status = result.getOrNull()!!
                isUpdatingSwitch = true
                binding.switchTwoFactorApp.isChecked = status.authenticatorEnabled
                binding.switchTwoFactorEmail.isChecked = status.emailEnabled
                isUpdatingSwitch = false
            } else {
                Log.e(TAG, "Ошибка получения статуса 2FA", result.exceptionOrNull())
            }
        }
    }

    /**
     * Модалка смены пароля через код на email (recommended flow)
     * 3 шага: запрос кода → подтверждение кода → новый пароль
     */
    private fun showChangePasswordDialog() {
        var resetId: String? = null
        var currentStep = 1
        
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val hPad = (24 * resources.displayMetrics.density).toInt()
            setPadding(hPad, 0, hPad, 0)
        }
        
        val stepIndicator = android.widget.TextView(this).apply {
            text = getString(R.string.security_password_step, 1, getString(R.string.security_password_step_request))
            textSize = 14f
            gravity = android.view.Gravity.CENTER
            setTextColor(getColor(android.R.color.holo_blue_dark))
        }
        container.addView(stepIndicator)
        
        // Шаг 1: Кнопка отправки кода
        val sendCodeButton = com.google.android.material.button.MaterialButton(this).apply {
            text = getString(R.string.security_send_code)
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                topMargin = (16 * resources.displayMetrics.density).toInt()
            }
        }
        container.addView(sendCodeButton)
        
        // Шаг 2: Поле ввода OTP
        val otpContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = android.view.View.GONE
        }
        
        val otpLayout = TextInputLayout(this).apply {
            hint = getString(R.string.security_code_from_email)
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        otpContainer.addView(otpLayout)
        
        val confirmCodeButton = com.google.android.material.button.MaterialButton(this).apply {
            text = getString(R.string.security_confirm_code)
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                topMargin = (8 * resources.displayMetrics.density).toInt()
            }
        }
        otpContainer.addView(confirmCodeButton)
        container.addView(otpContainer)
        
        // Шаг 3: Поля нового пароля
        val passwordContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = android.view.View.GONE
        }
        
        val newPasswordLayout = TextInputLayout(this).apply {
            hint = getString(R.string.security_new_password)
        }
        val newPasswordEdit = TextInputEditText(newPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        newPasswordLayout.addView(newPasswordEdit)
        passwordContainer.addView(newPasswordLayout)
        
        val confirmPasswordLayout = TextInputLayout(this).apply {
            hint = getString(R.string.security_confirm_password)
        }
        val confirmPasswordEdit = TextInputEditText(confirmPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        confirmPasswordLayout.addView(confirmPasswordEdit)
        passwordContainer.addView(confirmPasswordLayout)
        
        val savePasswordButton = com.google.android.material.button.MaterialButton(this).apply {
            text = getString(R.string.security_save_password)
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                topMargin = (8 * resources.displayMetrics.density).toInt()
            }
        }
        passwordContainer.addView(savePasswordButton)
        container.addView(passwordContainer)
        
        val dialog = MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_change_password)
            .setView(container)
            .setNegativeButton(R.string.btn_cancel, null)
            .create()
        
        // Шаг 1: Отправка кода
        sendCodeButton.setOnClickListener {
            lifecycleScope.launch {
                val result = grpcManager.resetPassword()
                if (result.isSuccess) {
                    resetId = result.getOrNull()
                    currentStep = 2
                    stepIndicator.text = getString(R.string.security_password_step, 2, getString(R.string.security_password_step_confirm))
                    stepIndicator.setTextColor(getColor(android.R.color.holo_green_dark))
                    sendCodeButton.visibility = android.view.View.GONE
                    otpContainer.visibility = android.view.View.VISIBLE
                    Toast.makeText(this@SecuritySettingsActivity, R.string.security_code_sent, Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this@SecuritySettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                }
            }
        }
        
        // Шаг 2: Подтверждение кода
        confirmCodeButton.setOnClickListener {
            val code = otpEdit.text?.toString() ?: ""
            if (code.length != 6) {
                Toast.makeText(this, R.string.security_code_length, Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            
            lifecycleScope.launch {
                val result = grpcManager.confirmResetPassword(resetId!!, code)
                if (result.isSuccess) {
                    currentStep = 3
                    stepIndicator.text = getString(R.string.security_password_step, 3, getString(R.string.security_password_step_new_password))
                    stepIndicator.setTextColor(getColor(android.R.color.holo_green_dark))
                    otpContainer.visibility = android.view.View.GONE
                    passwordContainer.visibility = android.view.View.VISIBLE
                    Toast.makeText(this@SecuritySettingsActivity, R.string.security_code_confirmed, Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this@SecuritySettingsActivity, R.string.security_invalid_code, Toast.LENGTH_SHORT).show()
                }
            }
        }
        
        // Шаг 3: Сохранение нового пароля
        savePasswordButton.setOnClickListener {
            val newPassword = newPasswordEdit.text?.toString() ?: ""
            val confirmPassword = confirmPasswordEdit.text?.toString() ?: ""
            
            if (newPassword.length < 6) {
                Toast.makeText(this, R.string.security_password_min_length, Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            if (newPassword != confirmPassword) {
                Toast.makeText(this, R.string.security_password_mismatch, Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            
            lifecycleScope.launch {
                val result = grpcManager.setPasswordAfterReset(newPassword)
                if (result.isSuccess) {
                    Toast.makeText(this@SecuritySettingsActivity, R.string.security_password_changed, Toast.LENGTH_SHORT).show()
                    dialog.dismiss()
                } else {
                    Toast.makeText(this@SecuritySettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                }
            }
        }
        
        dialog.show()
    }

    private fun enableAuthenticator2FA() {
        lifecycleScope.launch {
            val result = grpcManager.getOtpSetup()
            if (result.isSuccess) {
                val setup = result.getOrNull()!!
                showOtpSetupDialog(setup)
            } else {
                Toast.makeText(this@SecuritySettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                isUpdatingSwitch = true
                binding.switchTwoFactorApp.isChecked = false
                isUpdatingSwitch = false
            }
        }
    }

    private fun showOtpSetupDialog(setup: GrpcManager.OtpSetupResult) {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = android.view.Gravity.CENTER_HORIZONTAL
            val hPad = (24 * resources.displayMetrics.density).toInt()
            setPadding(hPad, (16 * resources.displayMetrics.density).toInt(), hPad, 0)
        }

        // QR код
        try {
            val qrBytes = Base64.decode(setup.qrBase64, Base64.DEFAULT)
            val qrBitmap = BitmapFactory.decodeByteArray(qrBytes, 0, qrBytes.size)
            val qrImageView = ImageView(this).apply {
                val size = (200 * resources.displayMetrics.density).toInt()
                layoutParams = LinearLayout.LayoutParams(size, size)
                setImageBitmap(qrBitmap)
                scaleType = ImageView.ScaleType.FIT_CENTER
            }
            container.addView(qrImageView)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка декодирования QR", e)
        }

        // Код для ручного ввода
        val codeText = android.widget.TextView(this).apply {
            text = getString(R.string.security_manual_code, setup.justCode)
            textSize = 14f
            gravity = android.view.Gravity.CENTER
            val topMargin = (8 * resources.displayMetrics.density).toInt()
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { setMargins(0, topMargin, 0, topMargin) }
        }
        container.addView(codeText)

        // Поле ввода OTP
        val otpLayout = TextInputLayout(this).apply {
            hint = getString(R.string.security_enter_app_code)
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        container.addView(otpLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_2fa_setup_title)
            .setView(container)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                val code = otpEdit.text?.toString() ?: ""
                if (code.isEmpty()) {
                    Toast.makeText(this, R.string.security_enter_code, Toast.LENGTH_SHORT).show()
                    isUpdatingSwitch = true
                    binding.switchTwoFactorApp.isChecked = false
                    isUpdatingSwitch = false
                    return@setPositiveButton
                }
                lifecycleScope.launch {
                    val result = grpcManager.confirmOtpSetup(code)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, R.string.security_2fa_enabled, Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, R.string.security_invalid_code, Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorApp.isChecked = false
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ ->
                isUpdatingSwitch = true
                binding.switchTwoFactorApp.isChecked = false
                isUpdatingSwitch = false
            }
            .setCancelable(false)
            .show()
    }

    private fun enableEmail2FA() {
        lifecycleScope.launch {
            val result = grpcManager.enableOtpEmail()
            if (result.isSuccess) {
                showEmailOtpConfirmDialog()
            } else {
                Toast.makeText(this@SecuritySettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                isUpdatingSwitch = true
                binding.switchTwoFactorEmail.isChecked = false
                isUpdatingSwitch = false
            }
        }
    }

    private fun showEmailOtpConfirmDialog() {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val hPad = (24 * resources.displayMetrics.density).toInt()
            setPadding(hPad, 0, hPad, 0)
        }

        val otpLayout = TextInputLayout(this).apply {
            hint = getString(R.string.security_code_from_email)
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        container.addView(otpLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_2fa_email_title)
            .setMessage(R.string.security_2fa_email_message)
            .setView(container)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                val code = otpEdit.text?.toString() ?: ""
                lifecycleScope.launch {
                    val result = grpcManager.confirmOtpSetup(code)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, R.string.security_2fa_email_enabled, Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, R.string.security_invalid_code, Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorEmail.isChecked = false
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ ->
                isUpdatingSwitch = true
                binding.switchTwoFactorEmail.isChecked = false
                isUpdatingSwitch = false
            }
            .setCancelable(false)
            .show()
    }

    private fun disableAuthenticator2FA() {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val hPad = (24 * resources.displayMetrics.density).toInt()
            setPadding(hPad, 0, hPad, 0)
        }

        val otpLayout = TextInputLayout(this).apply {
            hint = getString(R.string.security_code_from_app)
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        container.addView(otpLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_disable_2fa_title)
            .setMessage(R.string.security_disable_2fa_message)
            .setView(container)
            .setPositiveButton(R.string.security_disable) { _, _ ->
                val code = otpEdit.text?.toString() ?: ""
                lifecycleScope.launch {
                    val result = grpcManager.disableOtpVerification(IdentityApiOuterClass.OtpTypeId.Authenticator, code)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, R.string.security_2fa_disabled, Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorApp.isChecked = true
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ ->
                isUpdatingSwitch = true
                binding.switchTwoFactorApp.isChecked = true
                isUpdatingSwitch = false
            }
            .setCancelable(false)
            .show()
    }

    private fun disableEmail2FA() {
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_disable_2fa_email_title)
            .setMessage(R.string.security_disable_2fa_email_message)
            .setPositiveButton(R.string.security_disable) { _, _ ->
                lifecycleScope.launch {
                    val result = grpcManager.disableOtpVerification(IdentityApiOuterClass.OtpTypeId.Email, "")
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, R.string.security_2fa_email_disabled, Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorEmail.isChecked = true
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ ->
                isUpdatingSwitch = true
                binding.switchTwoFactorEmail.isChecked = true
                isUpdatingSwitch = false
            }
            .show()
    }
}
