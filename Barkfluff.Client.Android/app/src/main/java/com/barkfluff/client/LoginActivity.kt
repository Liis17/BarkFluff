package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.util.Log
import android.view.KeyEvent
import android.view.View
import android.widget.EditText
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityLoginBinding
import com.barkfluff.client.grpc.GrpcManager
import com.google.android.material.color.DynamicColors
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.launch
import java.util.regex.Pattern

/**
 * Экран авторизации
 * Аналог Login.xaml из WPF клиента
 */
class LoginActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "LoginActivity"
        private val USERNAME_PATTERN = Pattern.compile("^[a-zA-Z0-9._]{3,}$")
        private val EMAIL_PATTERN = Pattern.compile("^[A-Za-z0-9+_.-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$")
        private const val MIN_PASSWORD_LENGTH = 6
    }

    private lateinit var binding: ActivityLoginBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    private var isOtpMode = false
    private var isLoading = false

    // Saved login/password for OTP retry
    private var savedLogin = ""
    private var savedPassword = ""

    private lateinit var otpBoxes: List<EditText>

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityLoginBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager()

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
        setupOtpBoxes()
    }

    private fun initIdentityClient() {
        val identityAddress = globalParam.socketIdentity
        if (identityAddress.isBlank()) {
            showError("Адрес сервера авторизации не настроен")
            return
        }

        // Для авторизации не используем interceptor, так как токена еще нет
        val result = grpcManager.createIdentityClient(identityAddress)
        if (result.isFailure) {
            showError("Не удалось подключиться к серверу авторизации")
            Log.e(TAG, "Failed to create identity client", result.exceptionOrNull())
        }
    }

    private fun setupClickListeners() {
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

        binding.registerButton.setOnClickListener {
            navigateToRegister()
        }

        binding.forgotPasswordLink.setOnClickListener {
            Snackbar.make(binding.root, "Сброс пароля скоро будет доступен", Snackbar.LENGTH_SHORT).show()
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

    private fun getOtpCode(): String {
        return otpBoxes.joinToString("") { it.text.toString() }
    }

    private fun performLogin() {
        hideError()

        val loginInput = binding.usernameEditText.text.toString().trim()
        val password = binding.passwordEditText.text.toString()

        // Validate
        if (!validateLogin(loginInput) || !validatePassword(password)) {
            return
        }

        savedLogin = loginInput
        savedPassword = password

        val isEmail = loginInput.contains("@")
        val email = if (isEmail) loginInput else null
        val username = if (isEmail) null else loginInput

        setLoadingState(true)

        lifecycleScope.launch {
            val result = grpcManager.auth(
                email = email,
                username = username,
                password = password,
                otpCode = null,
                context = this@LoginActivity
            )
            handleAuthResult(result)
        }
    }

    private fun performOtpLogin() {
        hideError()

        val otpCode = getOtpCode()
        if (otpCode.length != 6) {
            showError("Введите 6-значный код")
            return
        }

        val isEmail = savedLogin.contains("@")
        val email = if (isEmail) savedLogin else null
        val username = if (isEmail) null else savedLogin

        setLoadingState(true)

        lifecycleScope.launch {
            val result = grpcManager.auth(
                email = email,
                username = username,
                password = savedPassword,
                otpCode = otpCode,
                context = this@LoginActivity
            )
            handleAuthResult(result)
        }
    }

    private fun handleAuthResult(result: GrpcManager.AuthResult) {
        setLoadingState(false)

        when (result) {
            is GrpcManager.AuthResult.Success -> {
                lifecycleScope.launch {
                    // Сохраняем токены
                    globalParam.accessToken = result.accessToken
                    globalParam.refreshToken = result.refreshToken
                    globalParam.accessTokenExpiration = result.accessTokenExpiration
                    globalParam.refreshTokenExpiration = result.refreshTokenExpiration

                    // Создаем Users клиент для загрузки данных пользователя
                    val usersAddress = globalParam.socketUsers
                    if (usersAddress.isNotBlank()) {
                        val usersResult = grpcManager.createUsersClient(usersAddress, this@LoginActivity)
                        if (usersResult.isSuccess) {
                            // Загружаем данные пользователя
                            val userDataResult = grpcManager.getCurrentUserData()
                            if (userDataResult.isSuccess) {
                                val userData = userDataResult.getOrNull()
                                if (userData != null) {
                                    globalParam.userId = userData.userId
                                    globalParam.userName = userData.username
                                    globalParam.firstName = userData.firstName
                                    globalParam.lastName = userData.lastName
                                    globalParam.description = userData.bio
                                    globalParam.pictureUrl = userData.profilePictureUrl
                                    globalParam.pictureId = userData.profilePictureUrl
                                    globalParam.registrationDate = userData.registrationDate
                                }
                            }
                        }
                    }

                    // Переходим в чаты
                    navigateToChats()
                }
            }
            is GrpcManager.AuthResult.OtpRequired -> {
                showOtpMode()
            }
            is GrpcManager.AuthResult.Error -> {
                showError(result.message)
            }
        }
    }

    private fun showOtpMode() {
        isOtpMode = true
        binding.loginFieldsGroup.visibility = View.GONE
        binding.otpGroup.visibility = View.VISIBLE
        binding.titleText.text = "Двухфакторная аутентификация"
        binding.subtitleText.text = "Подтвердите вход"
        binding.loginButton.text = "Подтвердить"

        // Clear and focus first box
        otpBoxes.forEach { it.text?.clear() }
        otpBoxes[0].requestFocus()
    }

    private fun validateLogin(login: String): Boolean {
        if (login.isBlank()) {
            binding.usernameInputLayout.error = "Введите имя пользователя или email"
            return false
        }

        if (login.contains("@")) {
            if (!EMAIL_PATTERN.matcher(login).matches()) {
                binding.usernameInputLayout.error = "Некорректный формат email"
                return false
            }
        } else {
            if (!USERNAME_PATTERN.matcher(login).matches()) {
                binding.usernameInputLayout.error = "Минимум 3 символа (буквы, цифры, . и _)"
                return false
            }
        }

        binding.usernameInputLayout.error = null
        return true
    }

    private fun validatePassword(password: String): Boolean {
        if (password.length < MIN_PASSWORD_LENGTH) {
            binding.passwordInputLayout.error = "Минимум $MIN_PASSWORD_LENGTH символов"
            return false
        }

        binding.passwordInputLayout.error = null
        return true
    }

    private fun setLoadingState(loading: Boolean) {
        isLoading = loading
        binding.loginButton.isEnabled = !loading
        binding.loginButton.text = if (loading) "" else if (isOtpMode) "Подтвердить" else "Войти"
        binding.loginProgressBar.visibility = if (loading) View.VISIBLE else View.GONE
    }

    private fun showError(message: String) {
        binding.errorText.text = message
        binding.errorText.visibility = View.VISIBLE
    }

    private fun hideError() {
        binding.errorText.visibility = View.GONE
    }

    private fun navigateToChats() {
        val intent = Intent(this, ChatsActivity::class.java)
        startActivity(intent)
        finish()
    }

    private fun navigateToSelectServer() {
        val intent = Intent(this, SelectServerActivity::class.java)
        startActivity(intent)
        finish()
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
            binding.loginFieldsGroup.visibility = View.VISIBLE
            binding.otpGroup.visibility = View.GONE
            binding.titleText.text = "Добро пожаловать"
            binding.subtitleText.text = "Войдите в свой аккаунт"
            binding.loginButton.text = "Войти"
            hideError()
        } else {
            super.onBackPressed()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }
}
