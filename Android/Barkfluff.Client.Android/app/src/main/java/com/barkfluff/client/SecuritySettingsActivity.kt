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
            text = "Шаг 1 из 3: Запрос кода"
            textSize = 14f
            gravity = android.view.Gravity.CENTER
            setTextColor(getColor(android.R.color.holo_blue_dark))
        }
        container.addView(stepIndicator)
        
        // Шаг 1: Кнопка отправки кода
        val sendCodeButton = com.google.android.material.button.MaterialButton(this).apply {
            text = "Отправить код на почту"
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
            hint = "Код из email"
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        otpContainer.addView(otpLayout)
        
        val confirmCodeButton = com.google.android.material.button.MaterialButton(this).apply {
            text = "Подтвердить код"
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
            hint = "Новый пароль"
        }
        val newPasswordEdit = TextInputEditText(newPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        newPasswordLayout.addView(newPasswordEdit)
        passwordContainer.addView(newPasswordLayout)
        
        val confirmPasswordLayout = TextInputLayout(this).apply {
            hint = "Подтвердите пароль"
        }
        val confirmPasswordEdit = TextInputEditText(confirmPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        confirmPasswordLayout.addView(confirmPasswordEdit)
        passwordContainer.addView(confirmPasswordLayout)
        
        val savePasswordButton = com.google.android.material.button.MaterialButton(this).apply {
            text = "Сохранить новый пароль"
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
            .setTitle("Смена пароля")
            .setView(container)
            .setNegativeButton("Отмена", null)
            .create()
        
        // Шаг 1: Отправка кода
        sendCodeButton.setOnClickListener {
            lifecycleScope.launch {
                val result = grpcManager.resetPassword()
                if (result.isSuccess) {
                    resetId = result.getOrNull()
                    currentStep = 2
                    stepIndicator.text = "Шаг 2 из 3: Подтверждение кода"
                    stepIndicator.setTextColor(getColor(android.R.color.holo_green_dark))
                    sendCodeButton.visibility = android.view.View.GONE
                    otpContainer.visibility = android.view.View.VISIBLE
                    Toast.makeText(this@SecuritySettingsActivity, "Код отправлен на email", Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                }
            }
        }
        
        // Шаг 2: Подтверждение кода
        confirmCodeButton.setOnClickListener {
            val code = otpEdit.text?.toString() ?: ""
            if (code.length != 6) {
                Toast.makeText(this, "Код должен содержать 6 цифр", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            
            lifecycleScope.launch {
                val result = grpcManager.confirmResetPassword(resetId!!, code)
                if (result.isSuccess) {
                    currentStep = 3
                    stepIndicator.text = "Шаг 3 из 3: Новый пароль"
                    stepIndicator.setTextColor(getColor(android.R.color.holo_green_dark))
                    otpContainer.visibility = android.view.View.GONE
                    passwordContainer.visibility = android.view.View.VISIBLE
                    Toast.makeText(this@SecuritySettingsActivity, "Код подтверждён", Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this@SecuritySettingsActivity, "Неверный код", Toast.LENGTH_SHORT).show()
                }
            }
        }
        
        // Шаг 3: Сохранение нового пароля
        savePasswordButton.setOnClickListener {
            val newPassword = newPasswordEdit.text?.toString() ?: ""
            val confirmPassword = confirmPasswordEdit.text?.toString() ?: ""
            
            if (newPassword.length < 6) {
                Toast.makeText(this, "Пароль должен быть не менее 6 символов", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            if (newPassword != confirmPassword) {
                Toast.makeText(this, "Пароли не совпадают", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            
            lifecycleScope.launch {
                val result = grpcManager.setPasswordAfterReset(newPassword)
                if (result.isSuccess) {
                    Toast.makeText(this@SecuritySettingsActivity, "Пароль успешно изменён", Toast.LENGTH_SHORT).show()
                    dialog.dismiss()
                } else {
                    Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
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
                Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
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
            text = "Код: ${setup.justCode}"
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
            hint = "Введите код из приложения"
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        container.addView(otpLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle("Настройка 2FA")
            .setView(container)
            .setPositiveButton("Подтвердить") { _, _ ->
                val code = otpEdit.text?.toString() ?: ""
                if (code.isEmpty()) {
                    Toast.makeText(this, "Введите код", Toast.LENGTH_SHORT).show()
                    isUpdatingSwitch = true
                    binding.switchTwoFactorApp.isChecked = false
                    isUpdatingSwitch = false
                    return@setPositiveButton
                }
                lifecycleScope.launch {
                    val result = grpcManager.confirmOtpSetup(code)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, "2FA включена", Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, "Неверный код", Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorApp.isChecked = false
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton("Отмена") { _, _ ->
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
                Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
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
            hint = "Код из email"
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        container.addView(otpLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle("Подтверждение 2FA по email")
            .setMessage("Код отправлен на вашу почту")
            .setView(container)
            .setPositiveButton("Подтвердить") { _, _ ->
                val code = otpEdit.text?.toString() ?: ""
                lifecycleScope.launch {
                    val result = grpcManager.confirmOtpSetup(code)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, "2FA по email включена", Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, "Неверный код", Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorEmail.isChecked = false
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton("Отмена") { _, _ ->
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
            hint = "Код из приложения"
        }
        val otpEdit = TextInputEditText(otpLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_NUMBER
        }
        otpLayout.addView(otpEdit)
        container.addView(otpLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle("Отключение 2FA")
            .setMessage("Введите код из приложения-аутентификатора")
            .setView(container)
            .setPositiveButton("Отключить") { _, _ ->
                val code = otpEdit.text?.toString() ?: ""
                lifecycleScope.launch {
                    val result = grpcManager.disableOtpVerification(IdentityApiOuterClass.OtpTypeId.Authenticator, code)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, "2FA отключена", Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorApp.isChecked = true
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton("Отмена") { _, _ ->
                isUpdatingSwitch = true
                binding.switchTwoFactorApp.isChecked = true
                isUpdatingSwitch = false
            }
            .setCancelable(false)
            .show()
    }

    private fun disableEmail2FA() {
        MaterialAlertDialogBuilder(this)
            .setTitle("Отключение 2FA по email")
            .setMessage("Вы уверены, что хотите отключить двухфакторную аутентификацию по email?")
            .setPositiveButton("Отключить") { _, _ ->
                lifecycleScope.launch {
                    val result = grpcManager.disableOtpVerification(IdentityApiOuterClass.OtpTypeId.Email, "")
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, "2FA по email отключена", Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                        isUpdatingSwitch = true
                        binding.switchTwoFactorEmail.isChecked = true
                        isUpdatingSwitch = false
                    }
                }
            }
            .setNegativeButton("Отмена") { _, _ ->
                isUpdatingSwitch = true
                binding.switchTwoFactorEmail.isChecked = true
                isUpdatingSwitch = false
            }
            .show()
    }
}
