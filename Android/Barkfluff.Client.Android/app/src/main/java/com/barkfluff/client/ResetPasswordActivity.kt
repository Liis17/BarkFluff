package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.View
import android.widget.EditText
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityResetPasswordBinding
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.launch

/**
 * Экран восстановления/сброса пароля
 * 3 шага: запрос кода → ввод кода → новый пароль
 */
class ResetPasswordActivity : AppCompatActivity() {

    private lateinit var binding: ActivityResetPasswordBinding
    private lateinit var grpcManager: GrpcManager

    private var resetId: String? = null
    private var currentStep = 1

    companion object {
        private const val TAG = "ResetPasswordActivity"
    }

    private val otpBoxes: List<EditText> by lazy {
        listOf(
            binding.otpBox1, binding.otpBox2, binding.otpBox3,
            binding.otpBox4, binding.otpBox5, binding.otpBox6
        )
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityResetPasswordBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

        setupToolbar()
        setupClickListeners()
        setupOtpBoxes()
        updateStepIndicators()
    }

    private fun setupToolbar() {
        // Обработка кнопки назад через системную навигацию
    }

    private fun setupClickListeners() {
        // Шаг 1: Отправка кода
        binding.sendCodeButton.setOnClickListener {
            val input = binding.emailEditText.text?.toString()?.trim() ?: ""
            if (input.isEmpty()) {
                showError("Введите email или имя пользователя")
                return@setOnClickListener
            }

            val isEmail = input.contains("@")
            val email = if (isEmail) input else null
            val username = if (isEmail) null else input

            sendCodeRequest(email, username)
        }

        // Шаг 2: Подтверждение кода
        binding.confirmCodeButton.setOnClickListener {
            val otpCode = getOtpCode()
            if (otpCode.length != 6) {
                showError("Введите 6-значный код")
                return@setOnClickListener
            }

            confirmCodeRequest(otpCode)
        }

        // Шаг 2: Повторная отправка кода
        binding.resendCodeButton.setOnClickListener {
            val input = binding.emailEditText.text?.toString()?.trim() ?: ""
            if (input.isEmpty()) {
                showError("Введите email или имя пользователя")
                return@setOnClickListener
            }

            val isEmail = input.contains("@")
            val email = if (isEmail) input else null
            val username = if (isEmail) null else input

            resendCodeRequest(email, username)
        }

        // Шаг 3: Сохранение нового пароля
        binding.savePasswordButton.setOnClickListener {
            val newPassword = binding.newPasswordEditText.text?.toString() ?: ""
            val confirmPassword = binding.confirmPasswordEditText.text?.toString() ?: ""

            if (newPassword.isEmpty()) {
                showError("Введите новый пароль")
                return@setOnClickListener
            }

            if (newPassword.length < 8) {
                showError("Пароль должен содержать минимум 8 символов")
                return@setOnClickListener
            }

            if (newPassword != confirmPassword) {
                showError("Пароли не совпадают")
                return@setOnClickListener
            }

            saveNewPassword(newPassword)
        }

        // Кнопка назад к входу (экран успеха)
        binding.backToLoginButton.setOnClickListener {
            val intent = Intent(this, LoginActivity::class.java)
            intent.flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
            startActivity(intent)
            finish()
        }
    }

    private fun setupOtpBoxes() {
        for (i in otpBoxes.indices) {
            val box = otpBoxes[i]

            box.addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                override fun afterTextChanged(s: Editable?) {
                    if (s != null && s.length == 1 && i < otpBoxes.size - 1) {
                        otpBoxes[i + 1].requestFocus()
                    }
                    // Auto-submit when all 6 digits are filled
                    if (i == otpBoxes.size - 1 && s != null && s.length == 1) {
                        val otp = getOtpCode()
                        if (otp.length == 6) {
                            confirmCodeRequest(otp)
                        }
                    }
                }
            })

            box.setOnKeyListener { _, keyCode, event ->
                if (keyCode == KeyEvent.KEYCODE_DEL && event.action == KeyEvent.ACTION_DOWN) {
                    if (box.text.isNullOrEmpty() && i > 0) {
                        otpBoxes[i - 1].apply {
                            requestFocus()
                            text?.clear()
                        }
                        return@setOnKeyListener true
                    }
                }
                false
            }
        }
    }

    private fun getOtpCode(): String {
        return otpBoxes.joinToString("") { it.text.toString() }
    }

    private fun sendCodeRequest(email: String?, username: String?) {
        hideError()
        setLoading(true)

        lifecycleScope.launch {
            val result = grpcManager.resetPassword(email, username)
            setLoading(false)

            if (result.isSuccess) {
                resetId = result.getOrNull()
                if (resetId != null) {
                    goToStep(2)
                    Toast.makeText(this@ResetPasswordActivity, "Код отправлен на вашу почту", Toast.LENGTH_LONG).show()
                } else {
                    showError("Ошибка: не получен resetId")
                }
            } else {
                showError("Ошибка: ${result.exceptionOrNull()?.message}")
            }
        }
    }

    private fun confirmCodeRequest(otpCode: String) {
        hideError()
        setLoading(true)

        lifecycleScope.launch {
            val result = grpcManager.confirmResetPassword(resetId!!, otpCode)
            setLoading(false)

            if (result.isSuccess) {
                val tokenResult = result.getOrNull()
                if (tokenResult != null) {
                    // Сохраняем новые токены
                    val globalParam = GlobalParam(this@ResetPasswordActivity)
                    globalParam.accessToken = tokenResult.accessToken
                    globalParam.accessTokenExpiration = tokenResult.accessTokenExpiration
                    globalParam.refreshToken = tokenResult.refreshToken
                    globalParam.refreshTokenExpiration = tokenResult.refreshTokenExpiration

                    goToStep(3)
                    Toast.makeText(this@ResetPasswordActivity, "Код подтверждён", Toast.LENGTH_SHORT).show()
                } else {
                    showError("Ошибка подтверждения кода")
                }
            } else {
                showError("Неверный код подтверждения")
                // Очищаем OTP боксы
                otpBoxes.forEach { it.text?.clear() }
                otpBoxes[0].requestFocus()
            }
        }
    }

    private fun resendCodeRequest(email: String?, username: String?) {
        hideError()
        setLoading(true)

        lifecycleScope.launch {
            val result = grpcManager.resetPassword(email, username)
            setLoading(false)

            if (result.isSuccess) {
                resetId = result.getOrNull()
                Toast.makeText(this@ResetPasswordActivity, "Код отправлен повторно", Toast.LENGTH_SHORT).show()
            } else {
                showError("Ошибка: ${result.exceptionOrNull()?.message}")
            }
        }
    }

    private fun saveNewPassword(newPassword: String) {
        hideError()
        setLoading(true)

        lifecycleScope.launch {
            val result = grpcManager.setPasswordAfterReset(newPassword)
            setLoading(false)

            if (result.isSuccess) {
                goToStep(4) // Экран успеха
                Toast.makeText(this@ResetPasswordActivity, "Пароль успешно изменён", Toast.LENGTH_SHORT).show()
            } else {
                showError("Ошибка: ${result.exceptionOrNull()?.message}")
            }
        }
    }

    private fun goToStep(step: Int) {
        currentStep = step

        when (step) {
            1 -> {
                binding.step1Card.visibility = View.VISIBLE
                binding.step2Card.visibility = View.GONE
                binding.step3Card.visibility = View.GONE
                binding.successContainer.visibility = View.GONE
                binding.subtitleText.text = "Следуйте инструкциям для сброса пароля"
            }
            2 -> {
                binding.step1Card.visibility = View.GONE
                binding.step2Card.visibility = View.VISIBLE
                binding.step3Card.visibility = View.GONE
                binding.successContainer.visibility = View.GONE
                binding.subtitleText.text = "Введите код из письма"
                // Фокус на первый бокс
                otpBoxes[0].requestFocus()
            }
            3 -> {
                binding.step1Card.visibility = View.GONE
                binding.step2Card.visibility = View.GONE
                binding.step3Card.visibility = View.VISIBLE
                binding.successContainer.visibility = View.GONE
                binding.subtitleText.text = "Придумайте новый пароль"
            }
            4 -> {
                binding.step1Card.visibility = View.GONE
                binding.step2Card.visibility = View.GONE
                binding.step3Card.visibility = View.GONE
                binding.successContainer.visibility = View.VISIBLE
                binding.progressContainer.visibility = View.GONE
                binding.subtitleText.visibility = View.GONE
            }
        }

        updateStepIndicators()
    }

    private fun updateStepIndicators() {
        // Сброс всех индикаторов
        val inactiveDrawable = getDrawable(R.drawable.circle_background_inactive)
        val activeDrawable = getDrawable(R.drawable.circle_background_active)

        binding.stepIndicator1.background = inactiveDrawable
        binding.stepIndicator2.background = inactiveDrawable
        binding.stepIndicator3.background = inactiveDrawable
        binding.stepIndicatorComplete.visibility = View.GONE

        binding.line1.setBackgroundColor(getColor(android.R.color.darker_gray))
        binding.line2.setBackgroundColor(getColor(android.R.color.darker_gray))
        binding.line3.setBackgroundColor(getColor(android.R.color.darker_gray))

        // Установка активных индикаторов
        when (currentStep) {
            1 -> {
                binding.stepIndicator1.background = activeDrawable
            }
            2 -> {
                binding.stepIndicator1.background = activeDrawable
                binding.stepIndicator2.background = activeDrawable
                binding.line1.setBackgroundColor(getColor(android.R.color.holo_green_light))
            }
            3 -> {
                binding.stepIndicator1.background = activeDrawable
                binding.stepIndicator2.background = activeDrawable
                binding.stepIndicator3.background = activeDrawable
                binding.line1.setBackgroundColor(getColor(android.R.color.holo_green_light))
                binding.line2.setBackgroundColor(getColor(android.R.color.holo_green_light))
            }
            4 -> {
                binding.stepIndicatorComplete.visibility = View.VISIBLE
                binding.stepIndicatorComplete.setColorFilter(getColor(android.R.color.holo_green_light))
                binding.line1.setBackgroundColor(getColor(android.R.color.holo_green_light))
                binding.line2.setBackgroundColor(getColor(android.R.color.holo_green_light))
                binding.line3.setBackgroundColor(getColor(android.R.color.holo_green_light))
            }
        }
    }

    private fun setLoading(loading: Boolean) {
        binding.loadingProgress.visibility = if (loading) View.VISIBLE else View.GONE
        binding.sendCodeButton.isEnabled = !loading
        binding.confirmCodeButton.isEnabled = !loading
        binding.resendCodeButton.isEnabled = !loading && currentStep == 2
        binding.savePasswordButton.isEnabled = !loading
    }

    private fun showError(message: String) {
        binding.errorText.text = message
        binding.errorText.visibility = View.VISIBLE
    }

    private fun hideError() {
        binding.errorText.visibility = View.GONE
    }

    override fun onSupportNavigateUp(): Boolean {
        onBackPressedDispatcher.onBackPressed()
        return true
    }
}
