package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.util.Log
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.widget.FrameLayout
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import androidx.appcompat.app.AppCompatActivity
import androidx.activity.viewModels
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.auth.AuthenticationChallengeDialog
import com.barkfluff.client.auth.AuthenticationChallengeViewModel
import com.barkfluff.client.databinding.ActivityLoginBinding
import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.gateway.UserSettingsGateway
import com.barkfluff.client.domain.auth.AuthenticationUiPolicy
import com.barkfluff.client.domain.model.AuthSession
import com.barkfluff.client.domain.model.AuthenticationCapabilities
import com.barkfluff.client.domain.model.AuthenticationLoginMode
import com.barkfluff.client.domain.model.SignInRequest
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.utils.applySpringPress
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.snackbar.Snackbar
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import kotlinx.coroutines.async
import java.util.regex.Pattern

/**
 * Экран авторизации
 * Аналог Login.xaml из WPF клиента
 */
@AndroidEntryPoint
class LoginActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "LoginActivity"
        private val USERNAME_PATTERN = Pattern.compile("^[a-zA-Z0-9._]{3,}$")
        private val EMAIL_PATTERN = Pattern.compile("^[A-Za-z0-9+_.-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$")
        private const val MIN_PASSWORD_LENGTH = 6
        private const val MEDIUM_WINDOW_MIN_WIDTH_DP = 600
        /** Отступы hero-блока; складываются с системными инсетами. */
        private const val LOGIN_TOP_PADDING_DP = 20
        private const val LOGIN_BOTTOM_PADDING_DP = 16
    }

    private fun Int.dpToPx(): Int = (this * resources.displayMetrics.density).toInt()

    private lateinit var binding: ActivityLoginBinding
    private lateinit var globalParam: GlobalParam
    private val authenticationChallengeViewModel: AuthenticationChallengeViewModel by viewModels()
    @javax.inject.Inject lateinit var authGateway: AuthGateway
    @javax.inject.Inject lateinit var authenticationChallengeGateway: AuthenticationChallengeGateway
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var userSettingsGateway: UserSettingsGateway
    @javax.inject.Inject lateinit var clientRegistry: GrpcClientRegistry

    private var isLoading = false
    private var identityErrorVisible = false
    private var selectedLoginMode = AuthenticationLoginMode.PASSWORD
    private var authenticationCapabilities: AuthenticationCapabilities? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityLoginBinding.inflate(layoutInflater)
        setContentView(binding.root)

        applyAdaptiveContentWidth()
        globalParam = GlobalParam(this)
        renderNodeSummary()

        // Edge-to-edge: инсеты на contentPanel, а не на корень — иначе декоративный круг
        // обрезается по нижней границе статус-бара вместо того чтобы уходить за край.
        ViewCompat.setOnApplyWindowInsetsListener(binding.contentPanel) { v, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.updatePadding(
                top = systemBars.top + LOGIN_TOP_PADDING_DP.dpToPx(),
                bottom = systemBars.bottom + LOGIN_BOTTOM_PADDING_DP.dpToPx()
            )
            insets
        }

        // Загружаем внешний IP-адрес асинхронно
        lifecycleScope.launch {
            GlobalParam.loadIpAddress(globalParam.sharedPreferences)
        }

        initIdentityClient()
        setupClickListeners()
        setupLoginFields()
        loadAuthenticationCapabilities()
    }

    override fun onResume() {
        super.onResume()
        renderNodeSummary()
    }

    /** На широких окнах ограничиваем форму 600dp и сохраняем читаемую длину строки. */
    private fun applyAdaptiveContentWidth() {
        if (resources.configuration.screenWidthDp < MEDIUM_WINDOW_MIN_WIDTH_DP) return

        val sideMarginPx = resources.getDimensionPixelSize(R.dimen.server_medium_window_margin)
        val maxContentWidthPx = resources.getDimensionPixelSize(R.dimen.server_content_max_width)
        val availableWidthPx = resources.configuration.screenWidthDp.dpToPx()
        val contentWidthPx = minOf(maxContentWidthPx, availableWidthPx - sideMarginPx * 2)
        if (contentWidthPx <= 0) return

        val layoutParams = binding.contentPanel.layoutParams as? FrameLayout.LayoutParams
            ?: return
        layoutParams.width = contentWidthPx
        layoutParams.gravity = Gravity.CENTER_HORIZONTAL
        binding.contentPanel.layoutParams = layoutParams
    }

    private fun renderNodeSummary() {
        val nodeName = globalParam.serverName.trim()
        val nodeAddress = globalParam.socketBeacon.ifBlank { globalParam.socketIdentity }.trim()

        binding.nodeSummaryText.text = nodeName.ifBlank {
            nodeAddress.ifBlank { getString(R.string.login_node_not_selected) }
        }
        if (nodeName.isNotBlank() && nodeAddress.isNotBlank()) {
            binding.nodeSummaryAddress.text = nodeAddress
            binding.nodeSummaryAddress.visibility = View.VISIBLE
        } else {
            binding.nodeSummaryAddress.visibility = View.GONE
        }
    }

    private fun initIdentityClient() {
        val identityAddress = globalParam.socketIdentity
        if (identityAddress.isBlank()) {
            showIdentityError(getString(R.string.login_identity_address_missing))
            return
        }

        // Identity остается анонимным до входа, но reset-password требует метаданные устройства.
        val result = authGateway.createIdentity(identityAddress, includeDeviceInfo = true)
        if (result.isFailure) {
            showIdentityError(getString(R.string.login_identity_connection_failed))
            Log.e(TAG, "Failed to create identity client", result.exceptionOrNull())
        }
    }

    private fun setupClickListeners() {
        binding.loginButton.applySpringPress()
        binding.loginButton.setOnClickListener {
            performLogin()
        }

        binding.loginModeButton.setOnClickListener { showLoginModePicker() }

        binding.changeServerLink.setOnClickListener {
            navigateToSelectServer()
        }

        binding.retryIdentityButton.setOnClickListener {
            hideError()
            initIdentityClient()
        }

        binding.registerButton.setOnClickListener {
            navigateToRegister()
        }

        binding.forgotPasswordLink.setOnClickListener {
            val intent = Intent(this, ResetPasswordActivity::class.java)
            startActivity(intent)
        }
    }

    private fun setupLoginFields() {
        binding.usernameEditText.apply {
            gravity = Gravity.CENTER_VERTICAL
            setPaddingRelative(paddingStart, 0, paddingEnd, 0)
        }
        binding.passwordEditText.apply {
            gravity = Gravity.CENTER_VERTICAL
            setPaddingRelative(paddingStart, 0, paddingEnd, 0)
        }

        binding.usernameEditText.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) = Unit
            override fun afterTextChanged(s: Editable?) {
                binding.usernameInputLayout.error = null
                clearErrorIfNotIdentity()
            }
        })

        binding.passwordEditText.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) = Unit
            override fun afterTextChanged(s: Editable?) {
                binding.passwordInputLayout.error = null
                clearErrorIfNotIdentity()
            }
        })

        binding.usernameEditText.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_NEXT || isEnterKey(event)) {
                binding.passwordEditText.requestFocus()
                true
            } else {
                false
            }
        }

        binding.passwordEditText.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_DONE || isEnterKey(event)) {
                performLogin()
                true
            } else {
                false
            }
        }
    }

    private fun isEnterKey(event: KeyEvent?): Boolean {
        return event?.keyCode == KeyEvent.KEYCODE_ENTER && event.action == KeyEvent.ACTION_DOWN
    }

    private fun performLogin() {
        clearErrorIfNotIdentity()

        if (isLoading) return

        val loginInput = binding.usernameEditText.text.toString().trim()
        val password = binding.passwordEditText.text.toString()

        val loginValid = validateLogin(loginInput)
        val passwordValid = selectedLoginMode == AuthenticationLoginMode.TELEGRAM_LOGIN || validatePassword(password)
        if (!loginValid || !passwordValid) {
            focusFirstInvalidField(loginValid, passwordValid)
            return
        }

        hideKeyboard()
        setLoadingState(true)

        lifecycleScope.launch {
            val result = AuthenticationChallengeDialog(
                this@LoginActivity,
                authenticationChallengeGateway,
                authenticationChallengeViewModel.controller,
            ).run(
                title = getString(R.string.login_2fa_title),
                useRecoveryCode = binding.recoveryCodeCheckBox.isChecked,
                restartWithFactor = { factor ->
                    authenticationChallengeGateway.beginSignIn(
                        SignInRequest(
                            login = loginInput,
                            password = password,
                            loginMode = selectedLoginMode,
                            factor = factor,
                            useRecoveryCode = binding.recoveryCodeCheckBox.isChecked,
                        ),
                    )
                },
            ) {
                authenticationChallengeGateway.beginSignIn(
                    SignInRequest(
                        login = loginInput,
                        password = password,
                        loginMode = selectedLoginMode,
                        useRecoveryCode = binding.recoveryCodeCheckBox.isChecked,
                    ),
                )
            }
            setLoadingState(false)
            result.onSuccess { completion ->
                completion.session?.let(::handleCompletedSession)
                    ?: showError(getString(R.string.auth_error))
            }.onFailure { failure ->
                if (failure !is java.util.concurrent.CancellationException) {
                    showError(failure.message ?: getString(R.string.auth_error))
                }
            }
        }
    }

    private fun loadAuthenticationCapabilities() {
        lifecycleScope.launch {
            authenticationChallengeGateway.capabilities().onSuccess { capabilities ->
                authenticationCapabilities = capabilities
                if (!capabilities.telegramAvailable && selectedLoginMode == AuthenticationLoginMode.TELEGRAM_LOGIN) {
                    selectedLoginMode = AuthenticationLoginMode.PASSWORD
                }
                renderLoginMode()
            }
        }
    }

    private fun showLoginModePicker() {
        val capabilities = authenticationCapabilities ?: return
        val modes = AuthenticationUiPolicy.signInModes(capabilities)
        val labels = modes.map { getString(loginModeLabel(it)) }.toTypedArray()
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.login_mode_title)
            .setSingleChoiceItems(labels, modes.indexOf(selectedLoginMode)) { dialog, which ->
                selectedLoginMode = modes[which]
                binding.recoveryCodeCheckBox.isChecked = false
                renderLoginMode()
                dialog.dismiss()
            }
            .show()
    }

    private fun renderLoginMode() {
        val passwordRequired = selectedLoginMode != AuthenticationLoginMode.TELEGRAM_LOGIN
        binding.loginModeButton.setText(loginModeLabel(selectedLoginMode))
        binding.passwordLabel.visibility = if (passwordRequired) View.VISIBLE else View.GONE
        binding.passwordInputLayout.visibility = if (passwordRequired) View.VISIBLE else View.GONE
        binding.recoveryCodeCheckBox.visibility = if (
            selectedLoginMode == AuthenticationLoginMode.PASSWORD_SECOND_FACTOR
        ) View.VISIBLE else View.GONE
        binding.forgotPasswordLink.visibility = if (passwordRequired) View.VISIBLE else View.GONE
        if (!passwordRequired) binding.passwordInputLayout.error = null
    }

    private fun loginModeLabel(mode: AuthenticationLoginMode): Int = when (mode) {
        AuthenticationLoginMode.PASSWORD -> R.string.login_mode_password
        AuthenticationLoginMode.TELEGRAM_LOGIN -> R.string.login_mode_telegram
        AuthenticationLoginMode.PASSWORD_SECOND_FACTOR -> R.string.login_mode_password_factor
    }

    private fun handleCompletedSession(session: AuthSession) {
        lifecycleScope.launch {
                    // Сохраняем токены
                    globalParam.accessToken = session.accessToken
                    globalParam.refreshToken = session.refreshToken
                    globalParam.accessTokenExpiration = session.accessTokenExpiration
                    globalParam.refreshTokenExpiration = session.refreshTokenExpiration

                    // Создаем Users клиент для загрузки данных пользователя
                    val usersAddress = globalParam.socketUsers
                    if (usersAddress.isNotBlank()) {
                        val usersResult = clientRegistry.createUsersClient(usersAddress, this@LoginActivity, includeDeviceInfo = true)
                        if (usersResult.isSuccess) {
                            // Загружаем профиль и синхронизируемые настройки параллельно.
                            val userSettingsDeferred = async { userSettingsGateway.syncedChatBackgrounds() }
                            val userDataResult = userProfileGateway.currentUser()
                            if (userDataResult.isSuccess) {
                                val userData = userDataResult.getOrNull()
                                if (userData != null) {
                                    Log.d(TAG, "Login: userData.userId=${userData.userId}, profilePictureFileId='${userData.profilePictureFileId}', profilePicturePreviewFileId='${userData.profilePicturePreviewFileId}'")

                                    globalParam.userId = userData.userId
                                    globalParam.userName = userData.username
                                    globalParam.firstName = userData.firstName
                                    globalParam.lastName = userData.lastName
                                    globalParam.description = userData.bio
                                    globalParam.pictureUrl = userData.profilePictureUrl
                                    globalParam.pictureId = userData.profilePictureUrl
                                    globalParam.pictureFileId = userData.profilePictureFileId
                                    globalParam.picturePreviewFileId = userData.profilePicturePreviewFileId
                                    globalParam.picturePreviewUrl = userData.profilePicturePreviewUrl
                                    globalParam.profilePictureUrl = userData.profilePictureUrl
                                    globalParam.registrationDate = userData.registrationDate

                                    Log.d(TAG, "Login: Saved to GlobalParam - pictureFileId='${globalParam.pictureFileId}', picturePreviewFileId='${globalParam.picturePreviewFileId}'")
                                }
                            }
                            userSettingsDeferred.await().onSuccess { settings ->
                                globalParam.applyChatBackgroundSettings(
                                    settings.globalChatBackgroundFileId,
                                    settings.chatBackgroundFileIds
                                )
                            }
                        }
                    }

                    // Переходим в чаты. Fresh channels pick up the newly persisted token.
                    clientRegistry.recreateAllClients(globalParam, this@LoginActivity)
                    navigateToChats()
        }
    }

    private fun validateLogin(login: String): Boolean {
        if (login.isBlank()) {
            binding.usernameInputLayout.error = getString(R.string.login_empty_login)
            return false
        }

        if (login.contains("@")) {
            if (!EMAIL_PATTERN.matcher(login).matches()) {
                binding.usernameInputLayout.error = getString(R.string.login_invalid_email)
                return false
            }
        } else {
            if (!USERNAME_PATTERN.matcher(login).matches()) {
                binding.usernameInputLayout.error = getString(R.string.login_username_too_short)
                return false
            }
        }

        binding.usernameInputLayout.error = null
        return true
    }

    private fun validatePassword(password: String): Boolean {
        if (password.length < MIN_PASSWORD_LENGTH) {
            binding.passwordInputLayout.error = getString(R.string.login_password_too_short, MIN_PASSWORD_LENGTH)
            return false
        }

        binding.passwordInputLayout.error = null
        return true
    }

    private fun setLoadingState(loading: Boolean) {
        isLoading = loading
        binding.loginButton.isEnabled = !loading
        binding.retryIdentityButton.isEnabled = !loading
        binding.loginButton.text = if (loading) "" else getString(R.string.btn_login)
        binding.loginProgressBar.visibility = if (loading) View.VISIBLE else View.GONE
    }

    private fun showError(message: String) {
        identityErrorVisible = false
        binding.errorText.visibility = View.VISIBLE
        binding.errorText.text = message
        binding.retryIdentityButton.visibility = View.GONE
    }

    private fun showIdentityError(message: String) {
        showError(message)
        identityErrorVisible = true
        binding.retryIdentityButton.visibility = View.VISIBLE
    }

    private fun hideError() {
        identityErrorVisible = false
        binding.errorText.visibility = View.GONE
        binding.retryIdentityButton.visibility = View.GONE
    }

    private fun clearErrorIfNotIdentity() {
        if (!identityErrorVisible) {
            hideError()
        }
    }

    private fun focusFirstInvalidField(loginValid: Boolean, passwordValid: Boolean) {
        val target = when {
            !loginValid -> binding.usernameEditText
            !passwordValid -> binding.passwordEditText
            else -> return
        }
        target.requestFocus()
        target.post {
            val inputMethodManager = getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager
            inputMethodManager.showSoftInput(target, InputMethodManager.SHOW_IMPLICIT)
        }
    }

    private fun hideKeyboard() {
        currentFocus?.windowToken?.let { token ->
            val inputMethodManager = getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager
            inputMethodManager.hideSoftInputFromWindow(token, 0)
        }
    }

    private fun navigateToChats() {
        val intent = Intent(this, MainActivity::class.java)
        startActivity(intent)
        finish()
    }

    private fun navigateToSelectServer() {
        val intent = Intent(this, SelectServerActivity::class.java).apply {
            putExtra(SelectServerActivity.EXTRA_RETURN_TO_LOGIN, true)
        }
        startActivity(intent)
    }

    private fun navigateToRegister() {
        val intent = Intent(this, RegisterActivity::class.java)
        startActivity(intent)
    }

}
