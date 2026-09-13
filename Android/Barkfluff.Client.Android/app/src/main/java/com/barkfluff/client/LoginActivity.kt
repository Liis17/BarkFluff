package com.barkfluff.client

import android.animation.ValueAnimator
import android.content.Intent
import android.os.Bundle
import android.text.Editable
import android.text.InputFilter
import android.text.TextWatcher
import android.util.Log
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.widget.FrameLayout
import android.widget.EditText
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import androidx.appcompat.app.AppCompatActivity
import androidx.dynamicanimation.animation.DynamicAnimation
import androidx.dynamicanimation.animation.SpringAnimation
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityLoginBinding
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.gateway.UserSettingsGateway
import com.barkfluff.client.domain.model.AuthenticationResult
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.utils.applySpringPress
import com.google.android.material.color.DynamicColors
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
        private const val LOGIN_MODE_FADE_DURATION_MS = 160L
        private const val LOGIN_MODE_OFFSET_DP = 12
        private const val LOGIN_MODE_SPRING_STIFFNESS = 700f
        private const val LOGIN_MODE_SPRING_DAMPING = 0.9f

        /** Отступы hero-блока; складываются с системными инсетами. */
        private const val LOGIN_TOP_PADDING_DP = 20
        private const val LOGIN_BOTTOM_PADDING_DP = 16
    }

    private fun Int.dpToPx(): Int = (this * resources.displayMetrics.density).toInt()

    private lateinit var binding: ActivityLoginBinding
    private lateinit var globalParam: GlobalParam
    @javax.inject.Inject lateinit var authGateway: AuthGateway
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var userSettingsGateway: UserSettingsGateway
    @javax.inject.Inject lateinit var clientRegistry: GrpcClientRegistry

    private var isOtpMode = false
    private var isLoading = false
    private var identityErrorVisible = false

    // Saved login/password for OTP retry
    private var savedLogin = ""
    private var savedPassword = ""

    private lateinit var otpBoxes: List<EditText>

    private val loginModeOffsetPx: Float
        get() = LOGIN_MODE_OFFSET_DP.dpToPx().toFloat()

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

        otpBoxes = listOf(
            binding.otpBox1, binding.otpBox2, binding.otpBox3,
            binding.otpBox4, binding.otpBox5, binding.otpBox6
        )

        // Загружаем внешний IP-адрес асинхронно
        lifecycleScope.launch {
            GlobalParam.loadIpAddress(globalParam.sharedPreferences)
        }

        initIdentityClient()
        setupClickListeners()
        setupLoginFields()
        setupOtpBoxes()
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

        // Для авторизации не используем interceptor, так как токена еще нет
        val result = authGateway.createIdentity(identityAddress)
        if (result.isFailure) {
            showIdentityError(getString(R.string.login_identity_connection_failed))
            Log.e(TAG, "Failed to create identity client", result.exceptionOrNull())
        }
    }

    private fun setupClickListeners() {
        binding.loginButton.applySpringPress()
        binding.loginButton.setOnClickListener {
            if (isOtpMode) {
                performOtpLogin()
            } else {
                performLogin()
            }
        }

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

    private fun setupOtpBoxes() {
        for (i in otpBoxes.indices) {
            val box = otpBoxes[i]

            if (i == 0) {
                box.filters = arrayOf(InputFilter { source, start, end, _, _, _ ->
                    val code = source.subSequence(start, end).toString()
                    if (code.length == otpBoxes.size && code.all(Char::isDigit)) {
                        box.post { fillOtpBoxes(code) }
                        ""
                    } else {
                        null
                    }
                }) + box.filters
            }

            box.addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                override fun afterTextChanged(s: Editable?) {
                    clearErrorIfNotIdentity()
                    if (s != null && s.length == 1 && i < otpBoxes.size - 1) {
                        otpBoxes[i + 1].requestFocus()
                    }
                    // Auto-submit when all 6 digits are filled
                    if (i == otpBoxes.size - 1 && s != null && s.length == 1) {
                        val otp = getOtpCode()
                        if (otp.length == 6) {
                            performOtpLogin()
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

    private fun fillOtpBoxes(code: String) {
        otpBoxes.forEachIndexed { index, box ->
            box.setText(code[index].toString())
        }
        otpBoxes.last().requestFocus()
    }

    private fun getOtpCode(): String {
        return otpBoxes.joinToString("") { it.text.toString() }
    }

    private fun performLogin() {
        clearErrorIfNotIdentity()

        if (isLoading) return

        val loginInput = binding.usernameEditText.text.toString().trim()
        val password = binding.passwordEditText.text.toString()

        val loginValid = validateLogin(loginInput)
        val passwordValid = validatePassword(password)
        if (!loginValid || !passwordValid) {
            focusFirstInvalidField(loginValid, passwordValid)
            return
        }

        savedLogin = loginInput
        savedPassword = password

        val isEmail = loginInput.contains("@")
        val email = if (isEmail) loginInput else null
        val username = if (isEmail) null else loginInput

        hideKeyboard()
        setLoadingState(true)

        lifecycleScope.launch {
            val result = authGateway.authenticate(
                email = email,
                username = username,
                password = password,
                otpCode = null,
            )
            handleAuthResult(result)
        }
    }

    private fun performOtpLogin() {
        clearErrorIfNotIdentity()

        if (isLoading) return

        val otpCode = getOtpCode()
        if (otpCode.length != 6) {
            showError(getString(R.string.login_otp_invalid_length))
            return
        }

        val isEmail = savedLogin.contains("@")
        val email = if (isEmail) savedLogin else null
        val username = if (isEmail) null else savedLogin

        hideKeyboard()
        setLoadingState(true)

        lifecycleScope.launch {
            val result = authGateway.authenticate(
                email = email,
                username = username,
                password = savedPassword,
                otpCode = otpCode,
            )
            handleAuthResult(result)
        }
    }

    private fun handleAuthResult(result: AuthenticationResult) {
        when (result) {
            is AuthenticationResult.Success -> {
                lifecycleScope.launch {
                    // Сохраняем токены
                    globalParam.accessToken = result.session.accessToken
                    globalParam.refreshToken = result.session.refreshToken
                    globalParam.accessTokenExpiration = result.session.accessTokenExpiration
                    globalParam.refreshTokenExpiration = result.session.refreshTokenExpiration

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
            AuthenticationResult.OtpRequired -> {
                setLoadingState(false)
                showOtpMode()
            }
            is AuthenticationResult.Error -> {
                setLoadingState(false)
                if (result.canRetryIdentity) {
                    showIdentityError(result.message)
                } else {
                    showError(result.message)
                }
            }
        }
    }

    private fun showOtpMode() {
        isOtpMode = true
        binding.titleText.setText(R.string.login_2fa_title)
        binding.subtitleText.setText(R.string.login_2fa_message)
        binding.loginButton.setText(R.string.btn_confirm)

        // Clear the code before the transition so autofill/paste starts from a clean state.
        otpBoxes.forEach { it.text?.clear() }
        switchLoginMode(showOtp = true) {
            otpBoxes[0].requestFocus()
            otpBoxes[0].post {
                val inputMethodManager = getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager
                inputMethodManager.showSoftInput(otpBoxes[0], InputMethodManager.SHOW_IMPLICIT)
            }
        }
    }

    /**
     * Переключает форму и OTP через короткий fade-through с лёгким spring по вертикали.
     * ValueAnimator отключается системной настройкой Animator duration scale = 0,
     * поэтому в reduced-motion режиме состояние меняется без декоративного движения.
     */
    private fun switchLoginMode(showOtp: Boolean, onShown: (() -> Unit)? = null) {
        val incoming = if (showOtp) binding.otpGroup else binding.loginFieldsGroup
        val outgoing = if (showOtp) binding.loginFieldsGroup else binding.otpGroup

        incoming.animate().cancel()
        outgoing.animate().cancel()
        incoming.translationY = 0f
        outgoing.translationY = 0f
        incoming.alpha = 1f
        outgoing.alpha = 1f

        if (!ValueAnimator.areAnimatorsEnabled()) {
            outgoing.visibility = View.GONE
            incoming.visibility = View.VISIBLE
            onShown?.invoke()
            return
        }

        outgoing.animate()
            .alpha(0f)
            .setDuration(LOGIN_MODE_FADE_DURATION_MS / 2)
            .withEndAction {
                outgoing.visibility = View.GONE
                outgoing.alpha = 1f

                incoming.visibility = View.VISIBLE
                incoming.alpha = 0f
                incoming.translationY = loginModeOffsetPx
                incoming.animate()
                    .alpha(1f)
                    .setDuration(LOGIN_MODE_FADE_DURATION_MS)
                    .withEndAction {
                        incoming.alpha = 1f
                        incoming.translationY = 0f
                        onShown?.invoke()
                    }
                    .start()

                SpringAnimation(incoming, DynamicAnimation.TRANSLATION_Y, 0f).apply {
                    spring.stiffness = LOGIN_MODE_SPRING_STIFFNESS
                    spring.dampingRatio = LOGIN_MODE_SPRING_DAMPING
                }.start()
            }
            .start()
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
        binding.loginButton.text = if (loading) "" else getString(
            if (isOtpMode) R.string.btn_confirm else R.string.btn_login
        )
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

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        if (isOtpMode) {
            // Return to login fields from OTP mode
            isOtpMode = false
            binding.titleText.setText(R.string.login_welcome_title)
            binding.subtitleText.setText(R.string.login_account_prompt)
            binding.loginButton.setText(R.string.btn_login)
            hideError()
            hideKeyboard()
            switchLoginMode(showOtp = false)
        } else {
            super.onBackPressed()
        }
    }

}
