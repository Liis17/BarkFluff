package com.barkfluff.client

import android.content.Intent
import android.content.res.ColorStateList
import android.os.Bundle
import android.os.CountDownTimer
import android.text.Editable
import android.text.TextUtils
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.View
import android.widget.EditText
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.text.HtmlCompat
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityResetPasswordBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.color.MaterialColors
import kotlinx.coroutines.launch

/**
 * Экран восстановления/сброса пароля
 * 4 шага: email/логин → код из письма → новый пароль → успех
 */
class ResetPasswordActivity : AppCompatActivity() {

    private lateinit var binding: ActivityResetPasswordBinding
    private lateinit var grpcManager: GrpcManager

    private var resetId: String? = null
    private var currentStep = 1
    private var isLoading = false
    private var resendCooldownActive = false
    private var resendTimer: CountDownTimer? = null
    private var lastLoginInput: String = ""

    companion object {
        private const val TAG = "ResetPasswordActivity"
        private const val RESEND_COOLDOWN_MS = 60_000L
    }

    private data class StrengthTier(
        val labelRes: Int,
        val containerColor: Int,
        val contentColor: Int,
        val litSegmentColor: Int
    )

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

        setupClickListeners()
        setupOtpBoxes()
        setupPasswordWatchers()
        updateProgressUi(1)
    }

    override fun onDestroy() {
        resendTimer?.cancel()
        super.onDestroy()
    }

    private fun setupClickListeners() {
        // Шаг 1: Отправка кода
        binding.sendCodeButton.setOnClickListener {
            val input = binding.emailEditText.text?.toString()?.trim() ?: ""
            if (input.isEmpty()) {
                showError(getString(R.string.reset_error_empty_login))
                return@setOnClickListener
            }

            lastLoginInput = input
            val isEmail = input.contains("@")
            val email = if (isEmail) input else null
            val username = if (isEmail) null else input

            sendCodeRequest(email, username)
        }

        binding.backToLoginLink.setOnClickListener { navigateToLogin() }

        // Шаг 2: Подтверждение кода
        binding.confirmCodeButton.setOnClickListener {
            val otpCode = getOtpCode()
            if (otpCode.length != 6) {
                showError(getString(R.string.reset_error_otp_incomplete))
                return@setOnClickListener
            }

            confirmCodeRequest(otpCode)
        }

        // Шаг 2: Повторная отправка кода
        binding.resendCodeButton.setOnClickListener {
            if (lastLoginInput.isEmpty()) {
                showError(getString(R.string.reset_error_empty_login))
                return@setOnClickListener
            }

            val isEmail = lastLoginInput.contains("@")
            val email = if (isEmail) lastLoginInput else null
            val username = if (isEmail) null else lastLoginInput

            resendCodeRequest(email, username)
        }

        // Шаг 3: Сохранение нового пароля
        binding.savePasswordButton.setOnClickListener {
            val newPassword = binding.newPasswordEditText.text?.toString() ?: ""
            val confirmPassword = binding.confirmPasswordEditText.text?.toString() ?: ""

            if (newPassword.isEmpty()) {
                showError(getString(R.string.reset_error_password_empty))
                return@setOnClickListener
            }

            if (newPassword.length < 8) {
                showError(getString(R.string.reset_error_password_too_short))
                return@setOnClickListener
            }

            if (newPassword != confirmPassword) {
                showError(getString(R.string.reset_error_password_mismatch))
                return@setOnClickListener
            }

            saveNewPassword(newPassword)
        }

        // Кнопка назад к входу (экран успеха)
        binding.backToLoginButton.setOnClickListener { navigateToLogin() }
    }

    private fun navigateToLogin() {
        val intent = Intent(this, LoginActivity::class.java)
        intent.flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
        startActivity(intent)
        finish()
    }

    private fun setupOtpBoxes() {
        for (i in otpBoxes.indices) {
            val box = otpBoxes[i]
            box.contentDescription = getString(R.string.reset_otp_digit_description, i + 1)

            box.addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                override fun afterTextChanged(s: Editable?) {
                    updateOtpBoxAppearance(i)
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

            box.setOnFocusChangeListener { _, _ -> updateOtpBoxAppearance(i) }

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

    private fun updateOtpBoxAppearance(index: Int) {
        val box = otpBoxes[index]
        val hasText = !box.text.isNullOrEmpty()
        when {
            hasText -> {
                box.setBackgroundResource(R.drawable.bg_otp_cell_filled)
                box.setTextColor(MaterialColors.getColor(box, com.google.android.material.R.attr.colorOnPrimary))
            }
            box.hasFocus() -> {
                box.setBackgroundResource(R.drawable.bg_otp_cell_active)
                box.setTextColor(MaterialColors.getColor(box, com.google.android.material.R.attr.colorOnSurface))
            }
            else -> {
                box.setBackgroundResource(R.drawable.bg_otp_cell_empty)
                box.setTextColor(MaterialColors.getColor(box, com.google.android.material.R.attr.colorOnSurface))
            }
        }
    }

    private fun setupPasswordWatchers() {
        binding.newPasswordEditText.doAfterTextChanged {
            updatePasswordStrengthUi(it?.toString() ?: "")
            updateConfirmPasswordMatchUi()
        }
        binding.confirmPasswordEditText.doAfterTextChanged {
            updateConfirmPasswordMatchUi()
        }
    }

    /**
     * Чистый подсчёт очков силы пароля — по образцу RegisterActivity.updatePasswordStrength()
     * (RegisterActivity.kt:650), но тиры/цвета сознательно не переиспользуются:
     * там hardcoded android.R.color.holo_*, здесь — dynamic color + success-роли.
     */
    private fun computeStrengthScore(pwd: String): Int {
        var score = 0
        if (pwd.length >= 8) score += 20
        if (pwd.any { it.isUpperCase() }) score += 20
        if (pwd.any { it.isLowerCase() }) score += 20
        if (pwd.any { it.isDigit() }) score += 20
        if (pwd.any { !it.isLetterOrDigit() }) score += 20
        return score
    }

    private fun updatePasswordStrengthUi(pwd: String) {
        if (pwd.isEmpty()) {
            binding.strengthContainer.visibility = View.GONE
            return
        }
        binding.strengthContainer.visibility = View.VISIBLE

        val score = computeStrengthScore(pwd)
        val litSegments = ((score * 4) / 100).coerceIn(1, 4)

        val tier = when {
            score < 40 -> StrengthTier(
                R.string.reset_strength_weak,
                MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorErrorContainer),
                MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorOnErrorContainer),
                MaterialColors.getColor(binding.root, androidx.appcompat.R.attr.colorError)
            )
            score < 60 -> StrengthTier(
                R.string.reset_strength_medium,
                MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorSurfaceContainerHighest),
                MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorOnSurface),
                MaterialColors.getColor(binding.root, androidx.appcompat.R.attr.colorPrimary)
            )
            score < 80 -> StrengthTier(
                R.string.reset_strength_good,
                MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorSurfaceContainerHighest),
                MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorOnSurface),
                MaterialColors.getColor(binding.root, androidx.appcompat.R.attr.colorPrimary)
            )
            else -> StrengthTier(
                R.string.reset_strength_strong,
                getColor(R.color.success_container),
                getColor(R.color.on_success_container),
                getColor(R.color.success)
            )
        }

        binding.strengthContainer.backgroundTintList = ColorStateList.valueOf(tier.containerColor)
        binding.strengthIcon.setColorFilter(tier.contentColor)
        binding.strengthLabel.setTextColor(tier.contentColor)
        binding.strengthLabel.text = getString(tier.labelRes)

        val trackColor = MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorOutlineVariant)
        val segments = listOf(
            binding.strengthSegment1, binding.strengthSegment2,
            binding.strengthSegment3, binding.strengthSegment4
        )
        segments.forEachIndexed { i, segment ->
            segment.backgroundTintList = ColorStateList.valueOf(if (i < litSegments) tier.litSegmentColor else trackColor)
        }
    }

    private fun updateConfirmPasswordMatchUi() {
        val newPassword = binding.newPasswordEditText.text?.toString() ?: ""
        val confirmPassword = binding.confirmPasswordEditText.text?.toString() ?: ""
        binding.confirmPasswordInputLayout.isEndIconVisible =
            confirmPassword.isNotEmpty() && confirmPassword == newPassword
    }

    private fun getOtpCode(): String {
        return otpBoxes.joinToString("") { it.text.toString() }
    }

    private fun maskLoginForDisplay(input: String): String {
        val atIndex = input.indexOf("@")
        if (atIndex <= 0) return input
        val local = input.substring(0, atIndex)
        val domain = input.substring(atIndex)
        val visible = local.take(2)
        return "$visible…$domain"
    }

    private fun updateCodeSentSubtitle() {
        val masked = TextUtils.htmlEncode(maskLoginForDisplay(lastLoginInput))
        val html = getString(R.string.reset_we_sent_code, masked)
        binding.subtitleText.text = HtmlCompat.fromHtml(html, HtmlCompat.FROM_HTML_MODE_LEGACY)
    }

    private fun startResendCooldown() {
        resendTimer?.cancel()
        resendCooldownActive = true
        binding.resendCodeButton.isEnabled = false
        resendTimer = object : CountDownTimer(RESEND_COOLDOWN_MS, 1000) {
            override fun onTick(millisUntilFinished: Long) {
                val totalSeconds = (millisUntilFinished / 1000).toInt()
                binding.resendTimerText.text = String.format("%d:%02d", totalSeconds / 60, totalSeconds % 60)
            }

            override fun onFinish() {
                resendCooldownActive = false
                binding.resendTimerText.text = "0:00"
                binding.resendCodeButton.isEnabled = !isLoading && currentStep == 2
            }
        }.start()
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
                    Toast.makeText(this@ResetPasswordActivity, getString(R.string.reset_toast_code_sent), Toast.LENGTH_LONG).show()
                } else {
                    showError(getString(R.string.reset_error_no_reset_id))
                }
            } else {
                showError(getString(R.string.reset_error_generic, result.exceptionOrNull()?.message))
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
                    Toast.makeText(this@ResetPasswordActivity, getString(R.string.reset_toast_code_confirmed), Toast.LENGTH_SHORT).show()
                } else {
                    showError(getString(R.string.reset_error_confirm_failed))
                }
            } else {
                showError(getString(R.string.reset_error_invalid_otp))
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
                startResendCooldown()
                Toast.makeText(this@ResetPasswordActivity, getString(R.string.reset_toast_code_resent), Toast.LENGTH_SHORT).show()
            } else {
                showError(getString(R.string.reset_error_generic, result.exceptionOrNull()?.message))
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
            } else {
                showError(getString(R.string.reset_error_generic, result.exceptionOrNull()?.message))
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
                binding.subtitleText.text = getString(R.string.reset_password_subtitle)
            }
            2 -> {
                binding.step1Card.visibility = View.GONE
                binding.step2Card.visibility = View.VISIBLE
                binding.step3Card.visibility = View.GONE
                binding.successContainer.visibility = View.GONE
                updateCodeSentSubtitle()
                startResendCooldown()
                otpBoxes[0].requestFocus()
            }
            3 -> {
                binding.step1Card.visibility = View.GONE
                binding.step2Card.visibility = View.GONE
                binding.step3Card.visibility = View.VISIBLE
                binding.successContainer.visibility = View.GONE
                binding.subtitleText.visibility = View.GONE
            }
            4 -> {
                resendTimer?.cancel()
                binding.step1Card.visibility = View.GONE
                binding.step2Card.visibility = View.GONE
                binding.step3Card.visibility = View.GONE
                binding.successContainer.visibility = View.VISIBLE
                binding.headerIconCard.visibility = View.GONE
                binding.progressContainer.visibility = View.GONE
                binding.segmentBarRow.visibility = View.GONE
                binding.titleText.visibility = View.GONE
                binding.subtitleText.visibility = View.GONE
            }
        }

        if (step in 1..3) {
            updateProgressUi(step)
        }
    }

    private fun updateProgressUi(step: Int) {
        binding.stepNumberText.text = String.format("%02d", step)
        binding.headerIcon.setImageResource(
            when (step) {
                1 -> R.drawable.ic_lock_reset
                2 -> R.drawable.ic_mark_email_read
                else -> R.drawable.ic_password_dots
            }
        )

        val activeColor = MaterialColors.getColor(binding.root, androidx.appcompat.R.attr.colorPrimary)
        val trackColor = MaterialColors.getColor(binding.root, com.google.android.material.R.attr.colorSurfaceContainerHighest)
        val segments = listOf(binding.segment1, binding.segment2, binding.segment3)
        segments.forEachIndexed { i, segment ->
            segment.backgroundTintList = ColorStateList.valueOf(if (i < step) activeColor else trackColor)
        }
    }

    private fun setLoading(loading: Boolean) {
        isLoading = loading
        binding.loadingProgress.visibility = if (loading) View.VISIBLE else View.GONE
        binding.sendCodeButton.isEnabled = !loading
        binding.confirmCodeButton.isEnabled = !loading
        binding.resendCodeButton.isEnabled = !loading && currentStep == 2 && !resendCooldownActive
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
