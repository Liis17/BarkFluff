package com.barkfluff.client

import android.content.Intent
import android.graphics.BitmapFactory
import android.os.Bundle
import android.util.Base64
import android.view.Gravity
import android.view.View
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.activity.viewModels
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.auth.AuthenticationChallengeDialog
import com.barkfluff.client.auth.AuthenticationChallengeViewModel
import com.barkfluff.client.databinding.ActivitySecuritySettingsBinding
import com.barkfluff.client.domain.auth.AuthenticationSecurityPolicy
import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import com.barkfluff.client.domain.model.AuthenticationChallengeReference
import com.barkfluff.client.domain.model.AuthenticationFactor
import com.barkfluff.client.domain.model.AuthenticationLoginMode
import com.barkfluff.client.domain.model.OtpEnrollment
import com.barkfluff.client.domain.model.SecuritySettings
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

@AndroidEntryPoint
class SecuritySettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivitySecuritySettingsBinding
    private val authenticationChallengeViewModel: AuthenticationChallengeViewModel by viewModels()
    @javax.inject.Inject lateinit var authenticationChallengeGateway: AuthenticationChallengeGateway

    private var isUpdatingSwitch = false
    private var securitySettings: SecuritySettings? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivitySecuritySettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)
        binding.toolbar.setNavigationOnClickListener { onBackPressedDispatcher.onBackPressed() }
        setupClickListeners()
    }

    override fun onResume() {
        super.onResume()
        loadSecuritySettings()
    }

    private fun setupClickListeners() {
        // Password recovery has its own challenge-based screen.
        binding.itemChangePassword.setOnClickListener {
            startActivity(Intent(this, ResetPasswordActivity::class.java))
        }
        binding.switchTwoFactorApp.setOnCheckedChangeListener { _, checked ->
            if (!isUpdatingSwitch) changeOtpFactor(AuthenticationFactor.AUTHENTICATOR, checked)
        }
        binding.switchTwoFactorEmail.setOnCheckedChangeListener { _, checked ->
            if (!isUpdatingSwitch) changeOtpFactor(AuthenticationFactor.EMAIL, checked)
        }
        binding.switchTwoFactorTelegram.setOnCheckedChangeListener { _, checked ->
            if (!isUpdatingSwitch) updateTelegramFactor(checked)
        }
        binding.telegramBindingButton.setOnClickListener { changeTelegramBinding() }
        binding.emailBindingButton.setOnClickListener { showBindEmailDialog() }
        binding.loginPolicyButton.setOnClickListener { showLoginPolicyPicker() }
        binding.recoveryCodesButton.setOnClickListener { regenerateRecoveryCodes() }
    }

    private fun loadSecuritySettings() {
        lifecycleScope.launch {
            authenticationChallengeGateway.securitySettings()
                .onSuccess(::renderSecuritySettings)
                .onFailure(::showFailure)
        }
    }

    private fun renderSecuritySettings(settings: SecuritySettings) {
        securitySettings = settings
        isUpdatingSwitch = true
        binding.switchTwoFactorApp.isChecked = settings.authenticatorEnabled
        binding.switchTwoFactorEmail.isChecked = settings.emailEnabled
        binding.switchTwoFactorTelegram.isChecked = settings.telegramOtpEnabled
        isUpdatingSwitch = false

        binding.itemTwoFactorTelegram.visibility = if (settings.telegramLinked) View.VISIBLE else View.GONE
        binding.telegramBindingButton.setText(
            if (settings.telegramLinked) R.string.security_unlink_telegram else R.string.security_link_telegram,
        )
        binding.emailBindingButton.text = if (settings.verifiedEmail.isBlank()) {
            getString(R.string.security_bind_email)
        } else {
            getString(R.string.security_bound_email, settings.verifiedEmail)
        }
        binding.loginPolicyButton.text = getString(
            R.string.security_login_policy_value,
            getString(loginModeLabel(settings.loginMode)),
        )
        binding.recoveryCodesButton.text = getString(
            R.string.security_recovery_codes_count,
            settings.remainingRecoveryCodes,
        )
    }

    private fun changeOtpFactor(factor: AuthenticationFactor, enabled: Boolean) {
        withSecurityProof { proof, _ ->
            if (enabled) {
                authenticationChallengeGateway.enableOtpVerification(factor, proof)
                    .onSuccess { enrollment ->
                        if (factor == AuthenticationFactor.AUTHENTICATOR) {
                            showAuthenticatorEnrollment(enrollment, proof)
                        } else {
                            showEmailEnrollment(proof)
                        }
                    }
                    .onFailure {
                        restoreSwitch(factor, false)
                        showFailure(it)
                    }
            } else {
                authenticationChallengeGateway.disableOtpVerification(factor, proof)
                    .onSuccess { loadSecuritySettings() }
                    .onFailure {
                        restoreSwitch(factor, true)
                        showFailure(it)
                    }
            }
        }
    }

    private fun updateTelegramFactor(enabled: Boolean) {
        withSecurityProof { proof, settings ->
            authenticationChallengeGateway.updateSecuritySettings(
                proof,
                AuthenticationSecurityPolicy.update(settings, telegramOtpEnabled = enabled),
            ).onSuccess(::renderSecuritySettings)
                .onFailure {
                    restoreSwitch(AuthenticationFactor.TELEGRAM, !enabled)
                    showFailure(it)
                }
        }
    }

    private fun showAuthenticatorEnrollment(enrollment: OtpEnrollment, proof: AuthenticationChallengeReference) {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(24), dp(16), dp(24), 0)
        }
        runCatching {
            val bytes = Base64.decode(enrollment.qrBase64, Base64.DEFAULT)
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.let { bitmap ->
                container.addView(ImageView(this).apply {
                    layoutParams = LinearLayout.LayoutParams(dp(200), dp(200))
                    setImageBitmap(bitmap)
                    scaleType = ImageView.ScaleType.FIT_CENTER
                })
            }
        }
        container.addView(TextView(this).apply {
            text = getString(R.string.security_manual_code, enrollment.manualCode)
            gravity = Gravity.CENTER
        })
        val codeInput = confirmationCodeInput()
        container.addView(codeInput.first)
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_2fa_setup_title)
            .setView(container)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                confirmOtpEnrollment(codeInput.second, proof, AuthenticationFactor.AUTHENTICATOR)
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ ->
                restoreSwitch(AuthenticationFactor.AUTHENTICATOR, false)
            }
            .show()
    }

    private fun showEmailEnrollment(proof: AuthenticationChallengeReference) {
        val codeInput = confirmationCodeInput()
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_2fa_email_title)
            .setMessage(R.string.security_2fa_email_message)
            .setView(codeInput.first)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                confirmOtpEnrollment(codeInput.second, proof, AuthenticationFactor.EMAIL)
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ ->
                restoreSwitch(AuthenticationFactor.EMAIL, false)
            }
            .show()
    }

    private fun confirmationCodeInput(): Pair<TextInputLayout, TextInputEditText> {
        val layout = TextInputLayout(this).apply { hint = getString(R.string.auth_confirmation_code) }
        return layout to TextInputEditText(this).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT
            layout.addView(this)
        }
    }

    private fun confirmOtpEnrollment(
        input: TextInputEditText,
        proof: AuthenticationChallengeReference,
        factor: AuthenticationFactor,
    ) {
        lifecycleScope.launch {
            authenticationChallengeGateway.confirmOtpVerification(input.text?.toString().orEmpty(), proof)
                .onSuccess { codes ->
                    loadSecuritySettings()
                    if (codes.isNotEmpty()) showRecoveryCodes(codes)
                }
                .onFailure {
                    restoreSwitch(factor, false)
                    showFailure(it)
                }
        }
    }

    private fun changeTelegramBinding() {
        withSecurityProof { proof, latest ->
            if (latest.telegramLinked) {
                authenticationChallengeGateway.unlinkTelegram(
                    proof,
                    AuthenticationSecurityPolicy.update(
                        latest,
                        telegramEnabled = false,
                        telegramOtpEnabled = false,
                    ),
                ).onSuccess(::renderSecuritySettings).onFailure(::showFailure)
            } else {
                AuthenticationChallengeDialog(
                    this@SecuritySettingsActivity,
                    authenticationChallengeGateway,
                    authenticationChallengeViewModel.controller,
                ).run(
                    title = getString(R.string.security_link_telegram),
                ) { authenticationChallengeGateway.beginTelegramBinding(proof) }
                    .onSuccess { loadSecuritySettings() }
                    .onFailure { if (it !is java.util.concurrent.CancellationException) showFailure(it) }
            }
        }
    }

    private fun showBindEmailDialog() {
        val input = TextInputEditText(this).apply { inputType = android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS }
        val layout = TextInputLayout(this).apply {
            hint = getString(R.string.hint_email_required)
            addView(input)
        }
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_bind_email)
            .setView(layout)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                val email = input.text?.toString()?.trim().orEmpty()
                if (email.isBlank()) return@setPositiveButton
                withSecurityProof { proof, _ ->
                    AuthenticationChallengeDialog(
                        this@SecuritySettingsActivity,
                        authenticationChallengeGateway,
                        authenticationChallengeViewModel.controller,
                    ).run(
                        title = getString(R.string.security_bind_email),
                    ) { authenticationChallengeGateway.beginEmailBinding(proof, email) }
                        .onSuccess { loadSecuritySettings() }
                        .onFailure { if (it !is java.util.concurrent.CancellationException) showFailure(it) }
                }
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    private fun showLoginPolicyPicker() {
        val settings = securitySettings ?: return
        val modes = buildList {
            add(AuthenticationLoginMode.PASSWORD)
            if (settings.telegramLinked) add(AuthenticationLoginMode.TELEGRAM_LOGIN)
            add(AuthenticationLoginMode.PASSWORD_SECOND_FACTOR)
        }
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_login_policy)
            .setSingleChoiceItems(
                modes.map { getString(loginModeLabel(it)) }.toTypedArray(),
                modes.indexOf(settings.loginMode),
            ) { dialog, position ->
                dialog.dismiss()
                withSecurityProof { proof, latest ->
                    val mode = modes[position]
                    val factor = if (mode == AuthenticationLoginMode.TELEGRAM_LOGIN) {
                        AuthenticationFactor.TELEGRAM
                    } else {
                        latest.preferredFactor
                    }
                    authenticationChallengeGateway.updateSecuritySettings(
                        proof,
                        AuthenticationSecurityPolicy.update(latest, loginMode = mode, preferredFactor = factor),
                    ).onSuccess(::renderSecuritySettings).onFailure(::showFailure)
                }
            }
            .show()
    }

    private fun regenerateRecoveryCodes() {
        withSecurityProof { proof, _ ->
            authenticationChallengeGateway.generateRecoveryCodes(proof)
                .onSuccess(::showRecoveryCodes)
                .onFailure(::showFailure)
        }
    }

    /** Requests a one-time proof without retaining its opaque value outside this callback. */
    private fun withSecurityProof(action: suspend (AuthenticationChallengeReference, SecuritySettings) -> Unit) {
        val settings = securitySettings ?: return
        val password = TextInputEditText(this).apply {
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        val layout = TextInputLayout(this).apply {
            hint = getString(R.string.security_reauthenticate_password)
            addView(password)
        }
        val recovery = MaterialCheckBox(this).apply { text = getString(R.string.login_use_recovery_code) }
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(24), 0, dp(24), 0)
            addView(layout)
            addView(recovery)
        }
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_reauthenticate)
            .setMessage(R.string.security_reauthenticate_message)
            .setView(content)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                lifecycleScope.launch {
                    AuthenticationChallengeDialog(
                        this@SecuritySettingsActivity,
                        authenticationChallengeGateway,
                        authenticationChallengeViewModel.controller,
                    ).run(
                        title = getString(R.string.security_reauthenticate),
                        useRecoveryCode = recovery.isChecked,
                    ) {
                        authenticationChallengeGateway.beginReauthentication(
                            password.text?.toString().orEmpty(),
                            settings.preferredFactor,
                            recovery.isChecked,
                        )
                    }.onSuccess { completion ->
                        completion.securityProof?.let { action(it, settings) }
                            ?: showFailure(IllegalStateException(getString(R.string.auth_error)))
                    }.onFailure { if (it !is java.util.concurrent.CancellationException) showFailure(it) }
                }
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    private fun restoreSwitch(factor: AuthenticationFactor, value: Boolean) {
        isUpdatingSwitch = true
        when (factor) {
            AuthenticationFactor.AUTHENTICATOR -> binding.switchTwoFactorApp.isChecked = value
            AuthenticationFactor.EMAIL -> binding.switchTwoFactorEmail.isChecked = value
            AuthenticationFactor.TELEGRAM -> binding.switchTwoFactorTelegram.isChecked = value
            AuthenticationFactor.NONE -> Unit
        }
        isUpdatingSwitch = false
    }

    private fun showRecoveryCodes(codes: List<String>) {
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_recovery_codes_title)
            .setMessage(codes.joinToString("\n"))
            .setPositiveButton(android.R.string.ok, null)
            .show()
    }

    private fun loginModeLabel(mode: AuthenticationLoginMode): Int = when (mode) {
        AuthenticationLoginMode.PASSWORD -> R.string.login_mode_password
        AuthenticationLoginMode.TELEGRAM_LOGIN -> R.string.login_mode_telegram
        AuthenticationLoginMode.PASSWORD_SECOND_FACTOR -> R.string.login_mode_password_factor
    }

    private fun showFailure(error: Throwable) {
        Toast.makeText(this, error.message ?: getString(R.string.auth_error), Toast.LENGTH_SHORT).show()
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
}
