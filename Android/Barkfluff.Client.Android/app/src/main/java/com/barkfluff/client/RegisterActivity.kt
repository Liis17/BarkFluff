package com.barkfluff.client

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Rect
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.Gravity
import android.view.KeyEvent
import android.view.LayoutInflater
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.activity.viewModels
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.core.widget.NestedScrollView
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.auth.AuthenticationChallengeDialog
import com.barkfluff.client.auth.AuthenticationChallengeViewModel
import com.barkfluff.client.databinding.ActivityRegisterBinding
import com.barkfluff.client.databinding.StepRegister01NameBinding
import com.barkfluff.client.databinding.StepRegister02UsernameBinding
import com.barkfluff.client.databinding.StepRegister03EmailBinding
import com.barkfluff.client.databinding.StepRegister05PasswordBinding
import com.barkfluff.client.databinding.StepRegister06AvatarBinding
import com.barkfluff.client.databinding.StepRegister07BioBinding
import com.barkfluff.client.databinding.StepRegister09CompleteBinding
import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import com.barkfluff.client.domain.gateway.UserDirectoryGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.auth.AuthenticationUiPolicy
import com.barkfluff.client.domain.model.AuthenticationFactor
import com.barkfluff.client.domain.model.AuthenticationLoginMode
import com.barkfluff.client.domain.model.RegistrationRequest
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.yalantis.ucrop.UCrop
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.ByteArrayOutputStream
import java.io.File

/**
 * Активность регистрации
 * Реализует challenge-based регистрацию:
 * 1. Имя и фамилия
 * 2. Логин (проверка на существование)
 * 3. Email или Telegram
 * 4. Пароль, если нужен выбранному режиму
 * 5. Аватар (с кропом через uCrop)
 * 6. Био
 * 7. Завершение
 */
@AndroidEntryPoint
class RegisterActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "RegisterActivity"
        private const val TOTAL_STEPS = 7
        private const val MIN_PASSWORD_LENGTH = 8
        private const val MAX_BIO_LENGTH = 200
        private const val MIN_NAME_LENGTH = 3
        private const val MAX_NAME_LENGTH = 40
        private const val MAX_USERNAME_LENGTH = 30
        
        // Ключи для сохранения состояния
        private const val KEY_CURRENT_STEP = "current_step"
        private const val KEY_FIRST_NAME = "first_name"
        private const val KEY_LAST_NAME = "last_name"
        private const val KEY_USERNAME = "username"
        private const val KEY_EMAIL = "email"
        private const val KEY_PASSWORD = "password"
        private const val KEY_BIO = "bio"
        private const val KEY_AVATAR_BYTES = "avatar_bytes"
        private const val KEY_CONFIRMATION_METHOD = "confirmation_method"
        private const val KEY_LOGIN_MODE = "login_mode"
    }

    private lateinit var binding: ActivityRegisterBinding
    private lateinit var globalParam: GlobalParam
    private val authenticationChallengeViewModel: AuthenticationChallengeViewModel by viewModels()
    @javax.inject.Inject lateinit var authenticationChallengeGateway: AuthenticationChallengeGateway
    @javax.inject.Inject lateinit var userDirectoryGateway: UserDirectoryGateway
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var clientRegistry: GrpcClientRegistry

    // Данные регистрации
    private var currentStep = 1
    private var firstName = ""
    private var lastName = ""
    private var username = ""
    private var email = ""
    private var password = ""
    private var bio = ""
    private var avatarBytes: ByteArray? = null
    private var confirmationMethod = AuthenticationFactor.EMAIL
    private var registrationLoginMode = AuthenticationLoginMode.PASSWORD

    // Bindings for each step
    private var step1Binding: StepRegister01NameBinding? = null
    private var step2Binding: StepRegister02UsernameBinding? = null
    private var step3Binding: StepRegister03EmailBinding? = null
    private var step5Binding: StepRegister05PasswordBinding? = null
    private var step6Binding: StepRegister06AvatarBinding? = null
    private var step7Binding: StepRegister07BioBinding? = null
    private var step9Binding: StepRegister09CompleteBinding? = null
    private var preparedStepContent: View? = null
    private var preparedStepContentBottomPadding = 0

    // Photo picker
    private val pickMedia = registerForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) {
            startUCrop(uri)
        }
    }

    // UCrop launcher
    private val ucropLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            val uri = UCrop.getOutput(result.data!!)
            if (uri != null) {
                loadCroppedImage(uri)
            } else {
                Toast.makeText(this, R.string.register_crop_error, Toast.LENGTH_SHORT).show()
            }
        } else if (result.resultCode == UCrop.RESULT_ERROR) {
            val error = UCrop.getError(result.data!!)
            Toast.makeText(this, getString(R.string.register_crop_error_detail, error?.message.orEmpty()), Toast.LENGTH_SHORT).show()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityRegisterBinding.inflate(layoutInflater)
        setContentView(binding.root)
        setupWindowInsets()

        globalParam = GlobalParam(this)

        // Создаем gRPC клиенты один раз при старте
        createGrpcClients()

        // Отображаем имя сервера
        binding.serverNameText.text = globalParam.serverName.ifBlank { getString(R.string.app_name) }

        // Восстанавливаем состояние если было
        savedInstanceState?.let {
            currentStep = it.getInt(KEY_CURRENT_STEP, 1)
            firstName = it.getString(KEY_FIRST_NAME, "")
            lastName = it.getString(KEY_LAST_NAME, "")
            username = it.getString(KEY_USERNAME, "")
            email = it.getString(KEY_EMAIL, "")
            password = it.getString(KEY_PASSWORD, "")
            bio = it.getString(KEY_BIO, "")
            confirmationMethod = it.getString(KEY_CONFIRMATION_METHOD)?.let { value ->
                runCatching { AuthenticationFactor.valueOf(value) }.getOrDefault(AuthenticationFactor.EMAIL)
            } ?: AuthenticationFactor.EMAIL
            registrationLoginMode = it.getString(KEY_LOGIN_MODE)?.let { value ->
                runCatching { AuthenticationLoginMode.valueOf(value) }.getOrDefault(AuthenticationLoginMode.PASSWORD)
            } ?: AuthenticationLoginMode.PASSWORD
            avatarBytes = it.getByteArray(KEY_AVATAR_BYTES)
            Log.d(TAG, "Состояние восстановлено: шаг $currentStep")
        }

        setupClickListeners()
        loadStep(currentStep)
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        saveCurrentStepData()
        outState.putInt(KEY_CURRENT_STEP, currentStep)
        outState.putString(KEY_FIRST_NAME, firstName)
        outState.putString(KEY_LAST_NAME, lastName)
        outState.putString(KEY_USERNAME, username)
        outState.putString(KEY_EMAIL, email)
        // Password, challenge references and security proofs are intentionally never persisted.
        outState.putString(KEY_BIO, bio)
        outState.putString(KEY_CONFIRMATION_METHOD, confirmationMethod.name)
        outState.putString(KEY_LOGIN_MODE, registrationLoginMode.name)
        outState.putByteArray(KEY_AVATAR_BYTES, avatarBytes)
        Log.d(TAG, "Состояние сохранено: шаг $currentStep")
    }

    private fun createGrpcClients() {
        // Создаем Identity и Users клиенты с заголовками устройства
        clientRegistry.createIdentityClient(globalParam.socketIdentity, this, includeDeviceInfo = true)
        clientRegistry.createUsersClient(globalParam.socketUsers, this, includeDeviceInfo = true)
        clientRegistry.createFilesClient(globalParam.socketFiles, this, includeDeviceInfo = true)
        Log.d(TAG, "gRPC клиенты созданы")
    }

    private fun recreateGrpcClients() {
        // Закрываем старые клиенты
        clientRegistry.recreateAllClients(globalParam, this)
        Log.d(TAG, "gRPC клиенты пересозданы")
    }

    private fun setupClickListeners() {
        binding.nextButton.setOnClickListener {
            when (currentStep) {
                2 -> checkUsernameOnServerAndProceed()
                3 -> checkEmailOnServerAndProceed()
                4 -> beginRegistrationAndProceed()
                5 -> uploadAvatarAndProceed()
                6 -> saveBioOnServerAndProceed()
                else -> {
                    if (validateCurrentStep()) {
                        if (currentStep < TOTAL_STEPS) {
                            saveCurrentStepData()
                            currentStep++
                            loadStep(currentStep)
                        }
                    }
                }
            }
        }
    }

    private fun checkEmailOnServerAndProceed() {
        if (!validateCurrentStep()) return

        if (confirmationMethod == AuthenticationFactor.TELEGRAM) {
            currentStep = 4
            loadStep(currentStep)
            return
        }

        val b = step3Binding ?: return
        b.emailValidationText.text = getString(R.string.register_checking)
        b.emailValidationText.visibility = View.VISIBLE
        setNextButtonEnabled(false)

        lifecycleScope.launch {
            try {
                val existsResult = userDirectoryGateway.checkEmail(email)
                if (existsResult.isSuccess) {
                    val exists = existsResult.getOrNull()!!
                    Log.d(TAG, "Email exists: $exists")
                    if (exists) {
                        b.emailValidationText.text = getString(R.string.register_email_taken)
                        b.emailValidationText.visibility = View.VISIBLE
                        setNextButtonEnabled(true)
                    } else {
                        b.emailValidationText.text = getString(R.string.register_email_available)
                        b.emailValidationText.visibility = View.VISIBLE
                        delay(500)
                        proceedToPasswordStep()
                    }
                } else {
                    Log.e(TAG, "Check email failed: ${existsResult.exceptionOrNull()?.message}")
                    b.emailValidationText.text = getString(R.string.register_check_error)
                    setNextButtonEnabled(true)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Check email error: ${e.message}", e)
                b.emailValidationText.text = getString(R.string.register_check_error)
                setNextButtonEnabled(true)
            }
        }
    }

    private fun loadStep(step: Int) {
        binding.contentFrame.removeAllViews()
        preparedStepContent = null

        // Обновляем крупную нумерацию и сегментный прогресс-бар
        binding.numberBigText.text = step.toString().padStart(2, '0')
        for (i in 0 until binding.segmentsRow.childCount) {
            binding.segmentsRow.getChildAt(i).setBackgroundColor(
                if (i < step) resolveThemeColor(androidx.appcompat.R.attr.colorPrimary)
                else resolveThemeColor(com.google.android.material.R.attr.colorSurfaceContainerHighest)
            )
        }

        // Настраиваем кнопки
        if (step == TOTAL_STEPS) {
            // На финальном шаге скрываем весь нижний бар — у шага 9 своя кнопка
            binding.headerPanel.visibility = View.GONE
            setNextButtonEnabled(false)
        } else {
            binding.headerPanel.visibility = View.VISIBLE
            setNextButtonEnabled(
                when (step) {
                    3 -> false
                    5 -> avatarBytes != null
                    else -> true
                }
            )
        }

        binding.nextButton.setText(R.string.btn_next)

        val inflater = LayoutInflater.from(this)
        when (step) {
            1 -> {
                step1Binding = StepRegister01NameBinding.inflate(inflater, binding.contentFrame, true)
                setupStep1()
            }
            2 -> {
                step2Binding = StepRegister02UsernameBinding.inflate(inflater, binding.contentFrame, true)
                setupStep2()
            }
            3 -> {
                step3Binding = StepRegister03EmailBinding.inflate(inflater, binding.contentFrame, true)
                setupStep3()
            }
            4 -> {
                step5Binding = StepRegister05PasswordBinding.inflate(inflater, binding.contentFrame, true)
                setupStep5()
            }
            5 -> {
                step6Binding = StepRegister06AvatarBinding.inflate(inflater, binding.contentFrame, true)
                setupStep6()
            }
            6 -> {
                step7Binding = StepRegister07BioBinding.inflate(inflater, binding.contentFrame, true)
                setupStep7()
            }
            7 -> {
                step9Binding = StepRegister09CompleteBinding.inflate(inflater, binding.contentFrame, true)
                setupStep9()
            }
        }

        prepareStepScroll()
    }

    private fun setupWindowInsets() {
        WindowCompat.setDecorFitsSystemWindows(window, false)

        val headerBasePaddingLeft = binding.headerPanel.paddingLeft
        val headerBasePaddingTop = binding.headerPanel.paddingTop
        val headerBasePaddingRight = binding.headerPanel.paddingRight
        val contentBasePaddingLeft = binding.contentFrame.paddingLeft
        val contentBasePaddingBottom = binding.contentFrame.paddingBottom
        val contentBasePaddingRight = binding.contentFrame.paddingRight
        val buttonBasePaddingLeft = binding.buttonPanel.paddingLeft
        val buttonBasePaddingBottom = binding.buttonPanel.paddingBottom
        val buttonBasePaddingRight = binding.buttonPanel.paddingRight
        val buttonBaseMarginBottom =
            (binding.buttonPanel.layoutParams as? android.widget.FrameLayout.LayoutParams)?.bottomMargin ?: 0

        ViewCompat.setOnApplyWindowInsetsListener(binding.headerPanel) { view, insets ->
            val safeArea = insets.getInsets(
                WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout()
            )
            view.updatePadding(
                left = headerBasePaddingLeft + safeArea.left,
                top = headerBasePaddingTop + safeArea.top,
                right = headerBasePaddingRight + safeArea.right
            )
            insets
        }

        ViewCompat.setOnApplyWindowInsetsListener(binding.contentFrame) { view, insets ->
            val safeArea = insets.getInsets(
                WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout()
            )
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            view.updatePadding(
                left = contentBasePaddingLeft + safeArea.left,
                right = contentBasePaddingRight + safeArea.right,
                bottom = contentBasePaddingBottom + maxOf(safeArea.bottom, ime.bottom)
            )
            insets
        }

        ViewCompat.setOnApplyWindowInsetsListener(binding.buttonPanel) { view, insets ->
            val safeArea = insets.getInsets(
                WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout()
            )
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            view.updatePadding(
                left = buttonBasePaddingLeft + safeArea.left,
                right = buttonBasePaddingRight + safeArea.right,
                bottom = buttonBasePaddingBottom
            )

            val layoutParams = view.layoutParams as? android.widget.FrameLayout.LayoutParams
            layoutParams?.let {
                it.bottomMargin = buttonBaseMarginBottom + maxOf(safeArea.bottom, ime.bottom)
                view.layoutParams = it
            }
            insets
        }

        ViewCompat.requestApplyInsets(binding.root)
    }

    private fun prepareStepScroll() {
        val scrollView = binding.contentFrame.getChildAt(0) as? NestedScrollView ?: return
        scrollView.clipToPadding = false

        val content = scrollView.getChildAt(0) ?: return
        if (preparedStepContent !== content) {
            preparedStepContent = content
            preparedStepContentBottomPadding = content.paddingBottom
        }
        val keyboardPadding = resources.getDimensionPixelSize(R.dimen.register_keyboard_content_padding)
        val buttonReserve = if (binding.buttonPanel.visibility == View.VISIBLE) {
            resources.getDimensionPixelSize(R.dimen.register_cta_height) +
                binding.buttonPanel.paddingTop + binding.buttonPanel.paddingBottom
        } else {
            0
        }
        content.setPaddingRelative(
            content.paddingStart,
            content.paddingTop,
            content.paddingEnd,
            preparedStepContentBottomPadding + keyboardPadding + buttonReserve
        )
    }

    private fun setNextButtonEnabled(enabled: Boolean) {
        binding.nextButton.isEnabled = enabled

        val visibility = if (enabled) View.VISIBLE else View.GONE
        if (binding.buttonPanel.visibility != visibility) {
            binding.buttonPanel.visibility = visibility
            prepareStepScroll()
            ViewCompat.requestApplyInsets(binding.root)
        }
    }

    private fun setupStep1() {
        val b = step1Binding ?: return
        b.firstNameEditText.setText(firstName)
        b.lastNameEditText.setText(lastName)
        setupTextField(b.firstNameEditText, focusContainer = b.firstNameEditText.parent as? View)
        setupTextField(b.lastNameEditText, focusContainer = b.lastNameEditText.parent as? View)
        b.firstNameCounterText.text = getString(R.string.register_bio_counter, firstName.length, MAX_NAME_LENGTH)
        b.lastNameCounterText.text = getString(R.string.register_bio_counter, lastName.length, MAX_NAME_LENGTH)

        b.firstNameEditText.doAfterTextChanged {
            firstName = it?.toString()?.trim() ?: ""
            b.firstNameCounterText.text = getString(R.string.register_bio_counter, it?.length ?: 0, MAX_NAME_LENGTH)
            validateFirstName()
        }

        b.lastNameEditText.doAfterTextChanged {
            lastName = it?.toString()?.trim() ?: ""
            b.lastNameCounterText.text = getString(R.string.register_bio_counter, it?.length ?: 0, MAX_NAME_LENGTH)
            validateLastName()
        }

        b.firstNameEditText.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_NEXT || isEnterKey(event)) {
                b.lastNameEditText.requestFocus()
                true
            } else {
                false
            }
        }

        b.lastNameEditText.setOnEditorActionListener { _, actionId, event ->
            if (actionId == EditorInfo.IME_ACTION_DONE || isEnterKey(event)) {
                binding.nextButton.performClick()
                true
            } else {
                false
            }
        }
    }

    private fun validateFirstName(): Boolean {
        val b = step1Binding ?: return false
        if (firstName.isEmpty()) {
            b.firstNameValidationText.text = getString(R.string.register_first_name_required_error)
            b.firstNameValidationText.setTextColor(resolveThemeColor(androidx.appcompat.R.attr.colorError))
            b.firstNameValidationText.visibility = View.VISIBLE
            return false
        }
        return if (firstName.length < MIN_NAME_LENGTH) {
            b.firstNameValidationText.text = getString(R.string.register_first_name_min_length, MIN_NAME_LENGTH)
            b.firstNameValidationText.setTextColor(resolveThemeColor(androidx.appcompat.R.attr.colorError))
            b.firstNameValidationText.visibility = View.VISIBLE
            false
        } else if (firstName.length > MAX_NAME_LENGTH) {
            b.firstNameValidationText.text = getString(R.string.register_first_name_max_length, MAX_NAME_LENGTH)
            b.firstNameValidationText.setTextColor(resolveThemeColor(androidx.appcompat.R.attr.colorError))
            b.firstNameValidationText.visibility = View.VISIBLE
            false
        } else {
            b.firstNameValidationText.text = getString(R.string.register_first_name_valid)
            b.firstNameValidationText.setTextColor(getColor(R.color.success))
            b.firstNameValidationText.visibility = View.VISIBLE
            true
        }
    }

    private fun validateLastName(): Boolean {
        val b = step1Binding ?: return false
        return if (lastName.isNotEmpty() && lastName.length > MAX_NAME_LENGTH) {
            b.lastNameValidationText.text = getString(R.string.register_first_name_max_length, MAX_NAME_LENGTH)
            b.lastNameValidationText.setTextColor(resolveThemeColor(androidx.appcompat.R.attr.colorError))
            b.lastNameValidationText.visibility = View.VISIBLE
            false
        } else {
            b.lastNameValidationText.text = getString(R.string.register_last_name_hint)
            b.lastNameValidationText.setTextColor(resolveThemeColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
            b.lastNameValidationText.visibility = View.VISIBLE
            true
        }
    }

    private fun setupStep2() {
        val b = step2Binding ?: return
        setupTextField(b.usernameEditText)
        b.usernameEditText.setText(username)

        b.usernameEditText.doAfterTextChanged {
            username = it?.toString()?.trim()?.lowercase() ?: ""
            if (username.isNotEmpty()) {
                val validPattern = Regex("^[a-z0-9_]+$")
                if (!username.matches(validPattern)) {
                    b.usernameValidationText.text = getString(R.string.register_username_charset_error)
                    b.usernameValidationText.visibility = View.VISIBLE
                } else if (username.length > MAX_USERNAME_LENGTH) {
                    b.usernameValidationText.text = getString(R.string.register_first_name_max_length, MAX_USERNAME_LENGTH)
                    b.usernameValidationText.visibility = View.VISIBLE
                } else {
                    b.usernameValidationText.visibility = View.GONE
                }
            } else {
                b.usernameValidationText.visibility = View.GONE
            }
        }
    }

    private fun checkUsernameOnServerAndProceed() {
        if (!validateCurrentStep()) return

        val b = step2Binding ?: return
        b.usernameValidationText.text = getString(R.string.register_checking)
        b.usernameValidationText.setTextColor(resolveThemeColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
        b.usernameValidationText.visibility = View.VISIBLE
        setNextButtonEnabled(false)

        lifecycleScope.launch {
            try {
                val existsResult = userDirectoryGateway.checkUsername(username)
                if (existsResult.isSuccess) {
                    val exists = existsResult.getOrNull()!!
                    Log.d(TAG, "Username exists: $exists")
                    if (exists) {
                        b.usernameValidationText.text = getString(R.string.register_username_status_taken)
                        b.usernameValidationText.setTextColor(getColor(R.color.error))
                        b.usernameValidationText.visibility = View.VISIBLE
                        setNextButtonEnabled(true)
                    } else {
                        b.usernameValidationText.text = getString(R.string.register_username_status_free)
                        b.usernameValidationText.setTextColor(getColor(R.color.success))
                        b.usernameValidationText.visibility = View.VISIBLE
                        delay(500)
                        saveCurrentStepData()
                        currentStep++
                        loadStep(currentStep)
                    }
                } else {
                    Log.e(TAG, "Check username failed: ${existsResult.exceptionOrNull()?.message}")
                    b.usernameValidationText.text = getString(R.string.register_check_error)
                    setNextButtonEnabled(true)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Check username error: ${e.message}", e)
                b.usernameValidationText.text = getString(R.string.register_check_error)
                setNextButtonEnabled(true)
            }
        }
    }

    private fun setupStep3() {
        val b = step3Binding ?: return
        setupTextField(b.emailEditText)
        b.emailEditText.setText(email)
        b.emailInputContainer.visibility = if (confirmationMethod == AuthenticationFactor.TELEGRAM) View.GONE else View.VISIBLE

        b.telegramRegistrationButton.setOnClickListener {
            MaterialAlertDialogBuilder(this)
                .setTitle(R.string.register_telegram_title)
                .setSingleChoiceItems(
                    arrayOf(
                        getString(R.string.register_login_mode_telegram),
                        getString(R.string.register_login_mode_password_factor),
                    ),
                    if (registrationLoginMode == AuthenticationLoginMode.TELEGRAM_LOGIN) 0 else 1,
                ) { dialog, selected ->
                    confirmationMethod = AuthenticationFactor.TELEGRAM
                    registrationLoginMode = if (selected == 0) {
                        AuthenticationLoginMode.TELEGRAM_LOGIN
                    } else {
                        AuthenticationLoginMode.PASSWORD_SECOND_FACTOR
                    }
                    email = ""
                    b.emailInputContainer.visibility = View.GONE
                    b.emailValidationText.text = getString(R.string.register_telegram_selected)
                    b.emailValidationText.visibility = View.VISIBLE
                    setNextButtonEnabled(true)
                    dialog.dismiss()
                }
                .show()
        }
        b.emailRegistrationButton.setOnClickListener {
            confirmationMethod = AuthenticationFactor.EMAIL
            registrationLoginMode = AuthenticationLoginMode.PASSWORD
            b.emailInputContainer.visibility = View.VISIBLE
            setNextButtonEnabled(email.isNotBlank())
        }
        lifecycleScope.launch {
            authenticationChallengeGateway.capabilities().onSuccess { capabilities ->
                val methods = AuthenticationUiPolicy.registrationConfirmationMethods(capabilities)
                b.telegramRegistrationButton.visibility = if (AuthenticationFactor.TELEGRAM in methods) View.VISIBLE else View.GONE
                b.emailRegistrationButton.visibility = if (
                    confirmationMethod == AuthenticationFactor.TELEGRAM && AuthenticationFactor.EMAIL in methods
                ) View.VISIBLE else View.GONE
                if (AuthenticationFactor.EMAIL !in methods && AuthenticationFactor.TELEGRAM in methods && confirmationMethod == AuthenticationFactor.EMAIL) {
                    b.telegramRegistrationButton.performClick()
                }
            }
        }

        b.emailEditText.doAfterTextChanged {
            email = it?.toString()?.trim()?.lowercase() ?: ""
            if (confirmationMethod == AuthenticationFactor.TELEGRAM) return@doAfterTextChanged
            if (email.isNotEmpty()) {
                val emailPattern = android.util.Patterns.EMAIL_ADDRESS
                if (!emailPattern.matcher(email).matches()) {
                    b.emailValidationText.text = getString(R.string.register_email_invalid)
                    b.emailValidationText.visibility = View.VISIBLE
                    setNextButtonEnabled(false)
                } else {
                    // Email валиден, проверяем на сервере
                    checkEmailExists()
                }
            } else {
                b.emailValidationText.visibility = View.GONE
                setNextButtonEnabled(false)
            }
        }
    }

    private var emailCheckJob: kotlinx.coroutines.Job? = null

    private fun checkEmailExists() {
        val b = step3Binding ?: return
        
        // Отменяем предыдущую проверку
        emailCheckJob?.cancel()
        
        // Запускаем новую проверку с задержкой (debounce)
        emailCheckJob = lifecycleScope.launch {
            delay(500) // Ждем пока пользователь закончит ввод
            
            b.emailValidationText.text = getString(R.string.register_checking)
            b.emailValidationText.visibility = View.VISIBLE
            setNextButtonEnabled(false)
            
            try {
                val existsResult = userDirectoryGateway.checkEmail(email)
                if (existsResult.isSuccess) {
                    val exists = existsResult.getOrNull()!!
                    Log.d(TAG, "Email exists: $exists")
                    if (exists) {
                        b.emailValidationText.text = getString(R.string.register_email_taken)
                        b.emailValidationText.visibility = View.VISIBLE
                        setNextButtonEnabled(false)
                    } else {
                        b.emailValidationText.text = getString(R.string.register_email_available)
                        b.emailValidationText.visibility = View.VISIBLE
                        setNextButtonEnabled(true)
                    }
                } else {
                    Log.e(TAG, "Check email failed: ${existsResult.exceptionOrNull()?.message}")
                    b.emailValidationText.text = getString(R.string.register_check_error)
                    b.emailValidationText.visibility = View.VISIBLE
                    setNextButtonEnabled(false)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Check email error: ${e.message}", e)
                b.emailValidationText.text = getString(R.string.register_check_error)
                b.emailValidationText.visibility = View.VISIBLE
                setNextButtonEnabled(false)
            }
        }
    }

    private fun proceedToPasswordStep() {
        if (!validateCurrentStep()) return

        val b = step3Binding ?: return
        b.emailValidationText.text = getString(R.string.register_account_creating)
        b.emailValidationText.visibility = View.VISIBLE
        setNextButtonEnabled(false)

        b.emailValidationText.text = getString(R.string.register_email_available)
        b.emailValidationText.visibility = View.VISIBLE
        saveCurrentStepData()
        currentStep = 4
        loadStep(currentStep)
    }

    private fun setupStep5() {
        val b = step5Binding ?: return
        if (registrationLoginMode == AuthenticationLoginMode.TELEGRAM_LOGIN) {
            // Telegram-only registration has no password step.
            b.root.visibility = View.GONE
            beginRegistrationAndProceed()
            return
        }
        setupTextField(b.passwordEditText)
        setupTextField(b.confirmPasswordEditText)

        b.passwordEditText.doAfterTextChanged {
            password = it?.toString() ?: ""
            updatePasswordStrength(password)
            validatePasswordStep()
        }

        b.confirmPasswordEditText.doAfterTextChanged {
            validatePasswordStep()
        }
    }

    private fun validatePasswordStep() {
        val b = step5Binding ?: return
        
        val confirmPassword = b.confirmPasswordEditText.text.toString()
        
        // Проверяем: пароль не пустой, минимальная длина, пароли совпадают
        if (password.length >= MIN_PASSWORD_LENGTH && password == confirmPassword && confirmPassword.isNotEmpty()) {
            setNextButtonEnabled(true)
        } else {
            setNextButtonEnabled(false)
        }
    }

    private fun updatePasswordStrength(pwd: String) {
        val b = step5Binding ?: return

        var score = 0
        val hasMinLength = pwd.length >= MIN_PASSWORD_LENGTH
        val hasUpperCase = pwd.any { it.isUpperCase() }
        val hasLowerCase = pwd.any { it.isLowerCase() }
        val hasDigit = pwd.any { it.isDigit() }
        val hasSpecial = pwd.any { !it.isLetterOrDigit() }
        if (hasMinLength) score += 20
        if (hasUpperCase) score += 20
        if (hasLowerCase) score += 20
        if (hasDigit) score += 20
        if (hasSpecial) score += 20

        val filledSegments = score / 20
        val segments = listOf(b.strengthSegment1, b.strengthSegment2, b.strengthSegment3, b.strengthSegment4)
        val activeColor = getColor(R.color.on_success_container)
        val inactiveColor = resolveThemeColor(com.google.android.material.R.attr.colorSurfaceContainerHighest)
        segments.forEachIndexed { index, segment ->
            segment.setBackgroundColor(if (index < filledSegments.coerceAtMost(4)) activeColor else inactiveColor)
        }

        b.passwordDifficultyIndicator.text = when {
            score == 0 -> getString(R.string.register_password_start_typing)
            score < 40 -> getString(R.string.register_password_strength_weak)
            score < 60 -> getString(R.string.register_password_strength_medium)
            score < 80 -> getString(R.string.register_password_strength_good)
            else -> getString(R.string.register_password_strength_strong)
        }

        // Обновляем чипы требований
        b.reqMinLength.isChecked = hasMinLength
        b.reqUpperCase.isChecked = hasUpperCase
        b.reqLowerCase.isChecked = hasLowerCase
        b.reqDigit.isChecked = hasDigit
        b.reqSpecialChar.isChecked = hasSpecial
    }

    private fun setupStep6() {
        val b = step6Binding ?: return

        b.uploadAvatarButton.setOnClickListener {
            checkPermissionAndPickImage()
        }

        b.skipAvatarButton.setOnClickListener {
            avatarBytes = null
            saveCurrentStepData()
            currentStep++
            loadStep(currentStep)
        }

        // Если аватар уже выбран, показываем его
        avatarBytes?.let { bytes ->
            val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
            b.croppedImageView.setImageBitmap(bitmap)
            b.croppedImageView.visibility = View.VISIBLE
            b.avatarPlaceholder.visibility = View.GONE
        }
    }

    private fun checkPermissionAndPickImage() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    android.Manifest.permission.READ_MEDIA_IMAGES
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                requestPermissions(arrayOf(android.Manifest.permission.READ_MEDIA_IMAGES), 100)
                return
            }
        } else if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.Q) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    android.Manifest.permission.READ_EXTERNAL_STORAGE
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                requestPermissions(arrayOf(android.Manifest.permission.READ_EXTERNAL_STORAGE), 100)
                return
            }
        }

        pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
    }

    private fun startUCrop(uri: Uri) {
        val destinationUri = Uri.fromFile(File(cacheDir, "cropped_avatar_${System.currentTimeMillis()}.jpg"))
        
        val options = UCrop.Options().apply {
            setCompressionFormat(Bitmap.CompressFormat.JPEG)
            setCompressionQuality(80)
            // Светлая тема для тулбара чтобы кнопки были видны
            setToolbarColor(getColor(android.R.color.white))
            setStatusBarColor(getColor(android.R.color.black))
            // Цвет иконок кнопок (черный на белом фоне)
            setActiveControlsWidgetColor(getColor(android.R.color.black))
            // Отключаем свободное кадрирование, только квадрат
            withAspectRatio(1f, 1f)
            withMaxResultSize(512, 512)
        }
        
        val uCrop = UCrop.of(uri, destinationUri)
            .withAspectRatio(1f, 1f)
            .withMaxResultSize(512, 512)
            .withOptions(options)
        
        ucropLauncher.launch(uCrop.getIntent(this))
    }

    private fun loadCroppedImage(uri: Uri) {
        try {
            val inputStream = contentResolver.openInputStream(uri)
            val bitmap = BitmapFactory.decodeStream(inputStream)
            inputStream?.close()

            val outputStream = ByteArrayOutputStream()
            bitmap.compress(Bitmap.CompressFormat.JPEG, 80, outputStream)
            avatarBytes = outputStream.toByteArray()

            step6Binding?.croppedImageView?.setImageBitmap(bitmap)
            step6Binding?.croppedImageView?.visibility = View.VISIBLE
            step6Binding?.avatarPlaceholder?.visibility = View.GONE

            // Включаем кнопку "Далее" теперь, когда аватар выбран
            setNextButtonEnabled(true)

            Toast.makeText(this, R.string.register_photo_selected, Toast.LENGTH_SHORT).show()
        } catch (e: Exception) {
            Log.e(TAG, "Load cropped image error: ${e.message}", e)
            Toast.makeText(this, getString(R.string.register_photo_upload_error, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
        }
    }

    private fun setupStep7() {
        val b = step7Binding ?: return
        setupTextField(b.bioEditText, Gravity.TOP)

        b.previewFullName.text = getString(R.string.register_full_name_format, firstName, lastName).trim()
        b.previewUsername.text = getString(R.string.register_username_format, username)

        avatarBytes?.let { bytes ->
            val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
            b.previewAvatar.setImageBitmap(bitmap)
        }

        b.bioEditText.setText(bio)
        b.bioCounterText.text = getString(R.string.register_bio_counter, bio.length, MAX_BIO_LENGTH)
        b.bioEditText.doAfterTextChanged {
            bio = it?.toString() ?: ""
            b.bioCounterText.text = getString(R.string.register_bio_counter, it?.length ?: 0, MAX_BIO_LENGTH)
        }
    }

    private fun beginRegistrationAndProceed() {
        if (!validateCurrentStep()) return

        setNextButtonEnabled(false)

        lifecycleScope.launch {
            val result = AuthenticationChallengeDialog(
                this@RegisterActivity,
                authenticationChallengeGateway,
                authenticationChallengeViewModel.controller,
            ).run(
                title = getString(R.string.register_step4_headline),
            ) {
                authenticationChallengeGateway.beginRegistration(
                    RegistrationRequest(
                        username = username,
                        password = if (registrationLoginMode == AuthenticationLoginMode.TELEGRAM_LOGIN) "" else password,
                        firstName = firstName,
                        lastName = lastName,
                        email = email,
                        confirmationMethod = confirmationMethod,
                        loginMode = registrationLoginMode,
                    ),
                )
            }
            result.onSuccess { completion ->
                val session = completion.session
                if (session == null) {
                    Toast.makeText(this@RegisterActivity, R.string.auth_error, Toast.LENGTH_SHORT).show()
                    setNextButtonEnabled(true)
                    return@onSuccess
                }
                globalParam.accessToken = session.accessToken
                globalParam.accessTokenExpiration = session.accessTokenExpiration
                globalParam.refreshToken = session.refreshToken
                globalParam.refreshTokenExpiration = session.refreshTokenExpiration
                recreateGrpcClients()
                if (completion.recoveryCodes.isNotEmpty()) showRecoveryCodes(completion.recoveryCodes)
                currentStep = 5
                loadStep(currentStep)
            }.onFailure { failure ->
                if (failure !is java.util.concurrent.CancellationException) {
                    Toast.makeText(this@RegisterActivity, failure.message ?: getString(R.string.auth_error), Toast.LENGTH_SHORT).show()
                }
                setNextButtonEnabled(true)
            }
        }
    }

    private fun showRecoveryCodes(codes: List<String>) {
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.security_recovery_codes_title)
            .setMessage(codes.joinToString("\n"))
            .setPositiveButton(R.string.btn_confirm, null)
            .show()
    }

    private fun uploadAvatarAndProceed() {
        val bytes = avatarBytes ?: return

        setNextButtonEnabled(false)

        lifecycleScope.launch {
            try {
                val uploadResult = userProfileGateway.uploadAvatar(bytes)
                if (uploadResult.isSuccess) {
                    val fileId = uploadResult.getOrNull()!!
                    Log.d(TAG, "Аватар загружен, fileId: $fileId")

                    val setResult = userProfileGateway.setProfilePicture(fileId)
                    if (setResult.isFailure) {
                        Log.e(TAG, "Set profile picture failed: ${setResult.exceptionOrNull()?.message}")
                    }

                    saveCurrentStepData()
                    currentStep++
                    loadStep(currentStep)
                } else {
                    Log.e(TAG, "Upload avatar failed: ${uploadResult.exceptionOrNull()?.message}")
                    Toast.makeText(
                        this@RegisterActivity,
                        getString(R.string.register_avatar_upload_error, uploadResult.exceptionOrNull()?.message.orEmpty()),
                        Toast.LENGTH_SHORT
                    ).show()
                    setNextButtonEnabled(true)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Upload avatar error: ${e.message}", e)
                Toast.makeText(this@RegisterActivity, getString(R.string.register_error_detail, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
                setNextButtonEnabled(true)
            }
        }
    }

    private fun saveBioOnServerAndProceed() {
        saveCurrentStepData()

        if (bio.isEmpty()) {
            currentStep = 7
            loadStep(currentStep)
            return
        }

        setNextButtonEnabled(false)

        lifecycleScope.launch {
            try {
                val result = userProfileGateway.changeBio(bio)
                if (result.isSuccess) {
                    Log.d(TAG, "Био установлено")
                } else {
                    Log.e(TAG, "Change bio failed: ${result.exceptionOrNull()?.message}")
                }
                currentStep = 7
                loadStep(currentStep)
            } catch (e: Exception) {
                Log.e(TAG, "Change bio error: ${e.message}", e)
                currentStep = 7
                loadStep(currentStep)
            }
        }
    }

    private fun resolveThemeColor(attr: Int): Int {
        val typedValue = android.util.TypedValue()
        theme.resolveAttribute(attr, typedValue, true)
        return typedValue.data
    }

    private fun setupTextField(
        field: EditText,
        gravity: Int = Gravity.CENTER_VERTICAL,
        focusContainer: View? = null
    ) {
        field.gravity = gravity
        field.setPaddingRelative(field.paddingStart, 0, field.paddingEnd, 0)
        field.setOnFocusChangeListener { _, hasFocus ->
            focusContainer?.setBackgroundResource(
                if (hasFocus) R.drawable.bg_register_input_row_focused
                else R.drawable.bg_register_input_row
            )
            if (hasFocus) {
                field.post { ensureFieldVisible(field) }
            }
        }
    }

    private fun ensureFieldVisible(field: View) {
        val margin = resources.getDimensionPixelSize(R.dimen.register_keyboard_scroll_margin)
        field.requestRectangleOnScreen(
            Rect(0, -margin, field.width, field.height + margin),
            true
        )
    }

    private fun focusAndShowKeyboard(field: EditText) {
        field.requestFocus()
        field.post {
            ensureFieldVisible(field)
            val inputMethodManager = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            inputMethodManager.showSoftInput(field, InputMethodManager.SHOW_IMPLICIT)
        }
    }

    private fun isEnterKey(event: KeyEvent?): Boolean {
        return event?.keyCode == KeyEvent.KEYCODE_ENTER && event.action == KeyEvent.ACTION_DOWN
    }

    private fun setupStep9() {
        val b = step9Binding ?: return

        b.step9StatusChip.text = getString(R.string.register_step9_status_chip, TOTAL_STEPS)

        b.goToLoginButton.setOnClickListener {
            navigateToMainActivity()
        }
    }

    private fun navigateToMainActivity() {
        val b = step9Binding ?: return
        b.finalLoadingIndicator.visibility = View.VISIBLE
        b.goToLoginButton.isEnabled = false

        lifecycleScope.launch {
            try {
                // Загружаем данные пользователя и сохраняем в globalParam
                val userResult = userProfileGateway.currentUser()
                if (userResult.isSuccess) {
                    val userData = userResult.getOrNull()!!
                    globalParam.userId = userData.userId
                    globalParam.userName = userData.username
                    globalParam.firstName = userData.firstName
                    globalParam.lastName = userData.lastName
                    globalParam.description = userData.bio
                    globalParam.pictureUrl = userData.profilePictureUrl
                    globalParam.pictureFileId = userData.profilePictureFileId
                    globalParam.picturePreviewFileId = userData.profilePicturePreviewFileId
                    globalParam.picturePreviewUrl = userData.profilePicturePreviewUrl
                    globalParam.profilePictureUrl = userData.profilePictureUrl
                }
            } catch (e: Exception) {
                Log.e(TAG, "Failed to load user data: ${e.message}", e)
            }

            val intent = Intent(this@RegisterActivity, MainActivity::class.java)
            intent.flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
            startActivity(intent)
            finish()
        }
    }

    private fun validateStep1(): Boolean {
        val b = step1Binding ?: return false
        val firstNameValid = validateFirstName()
        val lastNameValid = validateLastName()

        when {
            !firstNameValid -> focusAndShowKeyboard(b.firstNameEditText)
            !lastNameValid -> focusAndShowKeyboard(b.lastNameEditText)
        }

        return firstNameValid && lastNameValid
    }

    private fun validateCurrentStep(): Boolean {
        return when (currentStep) {
            1 -> validateStep1()
            2 -> {
                if (username.isEmpty()) return false
                val validPattern = Regex("^[a-z0-9_-]+$")
                username.matches(validPattern) && username.length <= MAX_USERNAME_LENGTH
            }
            3 -> {
                confirmationMethod == AuthenticationFactor.TELEGRAM ||
                    (email.isNotEmpty() && android.util.Patterns.EMAIL_ADDRESS.matcher(email).matches())
            }
            4 -> {
                if (registrationLoginMode == AuthenticationLoginMode.TELEGRAM_LOGIN) return true
                // Валидация пароля происходит в setupStep5
                val confirmPassword = step5Binding?.confirmPasswordEditText?.text?.toString() ?: ""
                password.length >= MIN_PASSWORD_LENGTH && password == confirmPassword
            }
            else -> true
        }
    }

    private fun saveCurrentStepData() {
        when (currentStep) {
            1 -> {
                firstName = step1Binding?.firstNameEditText?.text?.toString()?.trim() ?: ""
                lastName = step1Binding?.lastNameEditText?.text?.toString()?.trim() ?: ""
            }
            2 -> {
                username = step2Binding?.usernameEditText?.text?.toString()?.trim()?.lowercase() ?: ""
            }
            3 -> {
                email = step3Binding?.emailEditText?.text?.toString()?.trim()?.lowercase() ?: ""
            }
            4 -> {
                password = step5Binding?.passwordEditText?.text?.toString() ?: ""
            }
            6 -> {
                bio = step7Binding?.bioEditText?.text?.toString()?.trim() ?: ""
            }
        }
    }

    @Deprecated("Deprecated in Java")
    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        // Не позволяем возвращаться назад по шагам — только закрыть активити
        super.onBackPressed()
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 100) {
            if (grantResults.isNotEmpty() && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
            } else {
                Toast.makeText(this, R.string.register_photo_permission_denied, Toast.LENGTH_SHORT).show()
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        step1Binding = null
        step2Binding = null
        step3Binding = null
        step5Binding = null
        step6Binding = null
        step7Binding = null
        step9Binding = null
    }
}
