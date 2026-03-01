package com.barkfluff.client

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

    private fun showChangePasswordDialog() {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val hPad = (24 * resources.displayMetrics.density).toInt()
            setPadding(hPad, 0, hPad, 0)
        }

        val oldPasswordLayout = TextInputLayout(this).apply {
            hint = "Старый пароль"
            endIconMode = TextInputLayout.END_ICON_PASSWORD_TOGGLE
        }
        val oldPasswordEdit = TextInputEditText(oldPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        oldPasswordLayout.addView(oldPasswordEdit)
        container.addView(oldPasswordLayout)

        val newPasswordLayout = TextInputLayout(this).apply {
            hint = "Новый пароль"
            endIconMode = TextInputLayout.END_ICON_PASSWORD_TOGGLE
        }
        val newPasswordEdit = TextInputEditText(newPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        newPasswordLayout.addView(newPasswordEdit)
        container.addView(newPasswordLayout)

        val confirmPasswordLayout = TextInputLayout(this).apply {
            hint = "Подтверждение пароля"
            endIconMode = TextInputLayout.END_ICON_PASSWORD_TOGGLE
        }
        val confirmPasswordEdit = TextInputEditText(confirmPasswordLayout.context).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        confirmPasswordLayout.addView(confirmPasswordEdit)
        container.addView(confirmPasswordLayout)

        MaterialAlertDialogBuilder(this)
            .setTitle("Смена пароля")
            .setView(container)
            .setPositiveButton("Сменить") { _, _ ->
                val oldPassword = oldPasswordEdit.text?.toString() ?: ""
                val newPassword = newPasswordEdit.text?.toString() ?: ""
                val confirmPassword = confirmPasswordEdit.text?.toString() ?: ""

                if (newPassword.isEmpty()) {
                    Toast.makeText(this, "Введите новый пароль", Toast.LENGTH_SHORT).show()
                    return@setPositiveButton
                }
                if (newPassword != confirmPassword) {
                    Toast.makeText(this, "Пароли не совпадают", Toast.LENGTH_SHORT).show()
                    return@setPositiveButton
                }

                lifecycleScope.launch {
                    val result = grpcManager.changePassword(oldPassword, newPassword)
                    if (result.isSuccess) {
                        Toast.makeText(this@SecuritySettingsActivity, "Пароль изменен", Toast.LENGTH_SHORT).show()
                    } else {
                        val error = result.exceptionOrNull()
                        if (error is GrpcManager.InvalidOldPasswordException) {
                            Toast.makeText(this@SecuritySettingsActivity, "Неверный старый пароль", Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(this@SecuritySettingsActivity, "Ошибка: ${error?.message}", Toast.LENGTH_SHORT).show()
                        }
                    }
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
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
