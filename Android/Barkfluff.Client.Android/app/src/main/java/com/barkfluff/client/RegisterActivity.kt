package com.barkfluff.client

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityRegisterBinding
import com.barkfluff.client.databinding.StepRegister01NameBinding
import com.barkfluff.client.databinding.StepRegister02UsernameBinding
import com.barkfluff.client.databinding.StepRegister03EmailBinding
import com.barkfluff.client.databinding.StepRegister04VerifyBinding
import com.barkfluff.client.databinding.StepRegister05PasswordBinding
import com.barkfluff.client.databinding.StepRegister06AvatarBinding
import com.barkfluff.client.databinding.StepRegister07BioBinding
import com.barkfluff.client.databinding.StepRegister082faBinding
import com.barkfluff.client.databinding.StepRegister09CompleteBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.DeviceInfoInterceptor
import com.barkfluff.client.utils.OtpCellsHelper
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.ByteArrayOutputStream
import java.io.File

/**
 * Активность регистрации
 * Реализует 9 шагов как в десктопном приложении:
 * 1. Имя и фамилия
 * 2. Логин (проверка на существование)
 * 3. Email (создание аккаунта, отправка кода)
 * 4. Код подтверждения (подтверждение аккаунта)
 * 5. Пароль
 * 6. Аватар (с кропом через uCrop)
 * 7. Био
 * 8. 2FA
 * 9. Завершение
 */
class RegisterActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "RegisterActivity"
        private const val TOTAL_STEPS = 9
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
        private const val KEY_CODE_ID = "code_id"
        private const val KEY_2FA_ENABLED = "2fa_enabled"
        private const val KEY_AVATAR_BYTES = "avatar_bytes"
    }

    private lateinit var binding: ActivityRegisterBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    // Данные регистрации
    private var currentStep = 1
    private var firstName = ""
    private var lastName = ""
    private var username = ""
    private var email = ""
    private var password = ""
    private var bio = ""
    private var avatarBytes: ByteArray? = null
    private var codeId: String? = null  // CodeId от CreateAccount
    private var is2faEnabled = false
    private var otpHelper: OtpCellsHelper? = null

    // Bindings for each step
    private var step1Binding: StepRegister01NameBinding? = null
    private var step2Binding: StepRegister02UsernameBinding? = null
    private var step3Binding: StepRegister03EmailBinding? = null
    private var step4Binding: StepRegister04VerifyBinding? = null
    private var step5Binding: StepRegister05PasswordBinding? = null
    private var step6Binding: StepRegister06AvatarBinding? = null
    private var step7Binding: StepRegister07BioBinding? = null
    private var step8Binding: StepRegister082faBinding? = null
    private var step9Binding: StepRegister09CompleteBinding? = null

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

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager(applicationContext)

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
            codeId = it.getString(KEY_CODE_ID)
            is2faEnabled = it.getBoolean(KEY_2FA_ENABLED, false)
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
        outState.putString(KEY_PASSWORD, password)
        outState.putString(KEY_BIO, bio)
        outState.putString(KEY_CODE_ID, codeId)
        outState.putBoolean(KEY_2FA_ENABLED, is2faEnabled)
        outState.putByteArray(KEY_AVATAR_BYTES, avatarBytes)
        Log.d(TAG, "Состояние сохранено: шаг $currentStep")
    }

    private fun createGrpcClients() {
        // Создаем Identity и Users клиенты с заголовками устройства
        grpcManager.createIdentityClient(globalParam.socketIdentity, this, includeDeviceInfo = true)
        grpcManager.createUsersClient(globalParam.socketUsers, this, includeDeviceInfo = true)
        grpcManager.createFilesClient(globalParam.socketFiles, this, includeDeviceInfo = true)
        Log.d(TAG, "gRPC клиенты созданы")
    }

    private fun recreateGrpcClients() {
        // Закрываем старые клиенты
        grpcManager.shutdown()
        // Создаем новые
        createGrpcClients()
        Log.d(TAG, "gRPC клиенты пересозданы")
    }

    private fun setupClickListeners() {
        binding.nextButton.setOnClickListener {
            when (currentStep) {
                2 -> checkUsernameOnServerAndProceed()
                3 -> checkEmailOnServerAndProceed()
                4 -> confirmAccountAndProceed()
                5 -> setPasswordOnServerAndProceed()
                6 -> uploadAvatarAndProceed()
                7 -> saveBioOnServerAndProceed()
                8 -> verify2faAndProceed()
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

        val b = step3Binding ?: return
        b.emailValidationText.text = getString(R.string.register_checking)
        b.emailValidationText.visibility = View.VISIBLE
        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                val existsResult = grpcManager.checkEmail(email)
                if (existsResult.isSuccess) {
                    val exists = existsResult.getOrNull()!!
                    Log.d(TAG, "Email exists: $exists")
                    if (exists) {
                        b.emailValidationText.text = getString(R.string.register_email_taken)
                        b.emailValidationText.visibility = View.VISIBLE
                        binding.nextButton.isEnabled = true
                    } else {
                        b.emailValidationText.text = getString(R.string.register_email_available)
                        b.emailValidationText.visibility = View.VISIBLE
                        delay(500)
                        createAccountAndProceed()
                    }
                } else {
                    Log.e(TAG, "Check email failed: ${existsResult.exceptionOrNull()?.message}")
                    b.emailValidationText.text = getString(R.string.register_check_error)
                    binding.nextButton.isEnabled = true
                }
            } catch (e: Exception) {
                Log.e(TAG, "Check email error: ${e.message}", e)
                b.emailValidationText.text = getString(R.string.register_check_error)
                binding.nextButton.isEnabled = true
            }
        }
    }

    private fun loadStep(step: Int) {
        binding.contentFrame.removeAllViews()

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
            binding.buttonPanel.visibility = View.GONE
        } else {
            binding.headerPanel.visibility = View.VISIBLE
            binding.buttonPanel.visibility = View.VISIBLE
        }

        binding.nextButton.isEnabled = true
        binding.nextButton.setText(R.string.btn_next)

        // Специфичные настройки кнопки для шагов
        when (step) {
            6 -> {
                // Далее доступен только если аватар выбран
                binding.nextButton.isEnabled = avatarBytes != null
            }
            8 -> {
                // Далее доступен только после ввода 6-значного кода
                binding.nextButton.isEnabled = false
            }
        }

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
                step4Binding = StepRegister04VerifyBinding.inflate(inflater, binding.contentFrame, true)
                setupStep4()
            }
            5 -> {
                step5Binding = StepRegister05PasswordBinding.inflate(inflater, binding.contentFrame, true)
                setupStep5()
            }
            6 -> {
                step6Binding = StepRegister06AvatarBinding.inflate(inflater, binding.contentFrame, true)
                setupStep6()
            }
            7 -> {
                step7Binding = StepRegister07BioBinding.inflate(inflater, binding.contentFrame, true)
                setupStep7()
            }
            8 -> {
                step8Binding = StepRegister082faBinding.inflate(inflater, binding.contentFrame, true)
                setupStep8()
            }
            9 -> {
                step9Binding = StepRegister09CompleteBinding.inflate(inflater, binding.contentFrame, true)
                setupStep9()
            }
        }
    }

    private fun setupStep1() {
        val b = step1Binding ?: return
        b.firstNameEditText.setText(firstName)
        b.lastNameEditText.setText(lastName)
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
    }

    private fun validateFirstName(): Boolean {
        val b = step1Binding ?: return false
        if (firstName.isEmpty()) {
            b.firstNameValidationText.text = getString(R.string.register_first_name_required_error)
            b.firstNameValidationText.visibility = View.VISIBLE
            return false
        }
        return if (firstName.length < MIN_NAME_LENGTH) {
            b.firstNameValidationText.text = getString(R.string.register_first_name_min_length, MIN_NAME_LENGTH)
            b.firstNameValidationText.visibility = View.VISIBLE
            false
        } else if (firstName.length > MAX_NAME_LENGTH) {
            b.firstNameValidationText.text = getString(R.string.register_first_name_max_length, MAX_NAME_LENGTH)
            b.firstNameValidationText.visibility = View.VISIBLE
            false
        } else {
            b.firstNameValidationText.text = getString(R.string.register_first_name_valid)
            b.firstNameValidationText.visibility = View.VISIBLE
            true
        }
    }

    private fun validateLastName(): Boolean {
        val b = step1Binding ?: return false
        return if (lastName.isNotEmpty() && lastName.length > MAX_NAME_LENGTH) {
            b.lastNameValidationText.text = getString(R.string.register_first_name_max_length, MAX_NAME_LENGTH)
            b.lastNameValidationText.visibility = View.VISIBLE
            false
        } else {
            b.lastNameValidationText.visibility = View.GONE
            true
        }
    }

    private fun setupStep2() {
        val b = step2Binding ?: return
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
        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                val existsResult = grpcManager.checkUsername(username)
                if (existsResult.isSuccess) {
                    val exists = existsResult.getOrNull()!!
                    Log.d(TAG, "Username exists: $exists")
                    if (exists) {
                        b.usernameValidationText.text = getString(R.string.register_username_status_taken)
                        b.usernameValidationText.setTextColor(getColor(R.color.error))
                        b.usernameValidationText.visibility = View.VISIBLE
                        binding.nextButton.isEnabled = true
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
                    binding.nextButton.isEnabled = true
                }
            } catch (e: Exception) {
                Log.e(TAG, "Check username error: ${e.message}", e)
                b.usernameValidationText.text = getString(R.string.register_check_error)
                binding.nextButton.isEnabled = true
            }
        }
    }

    private fun setupStep3() {
        val b = step3Binding ?: return
        b.emailEditText.setText(email)

        b.emailEditText.doAfterTextChanged {
            email = it?.toString()?.trim()?.lowercase() ?: ""
            if (email.isNotEmpty()) {
                val emailPattern = android.util.Patterns.EMAIL_ADDRESS
                if (!emailPattern.matcher(email).matches()) {
                    b.emailValidationText.text = getString(R.string.register_email_invalid)
                    b.emailValidationText.visibility = View.VISIBLE
                    binding.nextButton.isEnabled = false
                } else {
                    // Email валиден, проверяем на сервере
                    checkEmailExists()
                }
            } else {
                b.emailValidationText.visibility = View.GONE
                binding.nextButton.isEnabled = false
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
            binding.nextButton.isEnabled = false
            
            try {
                val existsResult = grpcManager.checkEmail(email)
                if (existsResult.isSuccess) {
                    val exists = existsResult.getOrNull()!!
                    Log.d(TAG, "Email exists: $exists")
                    if (exists) {
                        b.emailValidationText.text = getString(R.string.register_email_taken)
                        b.emailValidationText.visibility = View.VISIBLE
                        binding.nextButton.isEnabled = false
                    } else {
                        b.emailValidationText.text = getString(R.string.register_email_available)
                        b.emailValidationText.visibility = View.VISIBLE
                        binding.nextButton.isEnabled = true
                    }
                } else {
                    Log.e(TAG, "Check email failed: ${existsResult.exceptionOrNull()?.message}")
                    b.emailValidationText.text = getString(R.string.register_check_error)
                    b.emailValidationText.visibility = View.VISIBLE
                    binding.nextButton.isEnabled = false
                }
            } catch (e: Exception) {
                Log.e(TAG, "Check email error: ${e.message}", e)
                b.emailValidationText.text = getString(R.string.register_check_error)
                b.emailValidationText.visibility = View.VISIBLE
                binding.nextButton.isEnabled = false
            }
        }
    }

    private fun createAccountAndProceed() {
        if (!validateCurrentStep()) return

        val b = step3Binding ?: return
        b.emailValidationText.text = getString(R.string.register_account_creating)
        b.emailValidationText.visibility = View.VISIBLE
        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                // Создаем аккаунт - сервер отправит код на почту
                val createResult = grpcManager.createAccount(firstName, lastName, email, username)
                if (createResult.isSuccess) {
                    codeId = createResult.getOrNull()
                    Log.d(TAG, "Аккаунт создан, CodeId: $codeId")
                    
                    b.emailValidationText.text = getString(R.string.register_code_sent, email)
                    b.emailValidationText.visibility = View.VISIBLE
                    delay(1000)
                    saveCurrentStepData()
                    currentStep++
                    loadStep(currentStep)
                } else {
                    Log.e(TAG, "Create account failed: ${createResult.exceptionOrNull()?.message}")
                    b.emailValidationText.text = getString(
                        R.string.register_error_detail,
                        createResult.exceptionOrNull()?.message.orEmpty()
                    )
                    binding.nextButton.isEnabled = true
                }
            } catch (e: Exception) {
                Log.e(TAG, "Create account error: ${e.message}", e)
                b.emailValidationText.text = getString(R.string.register_error_detail, e.message.orEmpty())
                binding.nextButton.isEnabled = true
            }
        }
    }

    private fun setupStep4() {
        val b = step4Binding ?: return
        otpHelper = OtpCellsHelper(
            listOf(b.otpCell1, b.otpCell2, b.otpCell3, b.otpCell4, b.otpCell5, b.otpCell6)
        ) { confirmAccountAndProceed() }
        otpHelper?.setup()
        otpHelper?.focusFirst()
    }

    private fun confirmAccountAndProceed() {
        val b = step4Binding ?: return
        val code = otpHelper?.getCode() ?: ""

        if (code.length != 6) {
            b.verificationCodeValidationText.text = getString(R.string.register_code_incomplete)
            b.verificationCodeValidationText.visibility = View.VISIBLE
            return
        }

        if (codeId == null) {
            b.verificationCodeValidationText.text = getString(R.string.register_account_not_created)
            b.verificationCodeValidationText.visibility = View.VISIBLE
            return
        }

        b.verificationCodeValidationText.text = getString(R.string.register_checking)
        b.verificationCodeValidationText.visibility = View.VISIBLE
        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                // Подтверждаем аккаунт - получаем refresh токен
                val confirmResult = grpcManager.confirmAccount(codeId!!, code)
                if (confirmResult.isSuccess) {
                    val confirmData = confirmResult.getOrNull()!!
                    Log.d(TAG, "Аккаунт подтвержден, получен refresh токен")
                    
                    // Сохраняем refresh токен
                    globalParam.refreshToken = confirmData.refreshToken
                    globalParam.refreshTokenExpiration = confirmData.refreshTokenExpiration
                    
                    // Создаем access токен
                    val tokenResult = grpcManager.refreshAccessToken(confirmData.refreshToken, confirmData.refreshTokenExpiration)
                    if (tokenResult.isSuccess) {
                        val tokenData = tokenResult.getOrNull()!!
                        globalParam.accessToken = tokenData.accessToken
                        globalParam.accessTokenExpiration = tokenData.accessTokenExpiration
                        Log.d(TAG, "Access токен получен")
                        
                        // Пересоздаем gRPC клиенты с новыми токенами
                        recreateGrpcClients()
                        
                        b.verificationCodeValidationText.text = getString(R.string.register_account_confirmed)
                        delay(500)
                        saveCurrentStepData()
                        currentStep++
                        loadStep(currentStep)
                    } else {
                        Log.e(TAG, "Failed to create access token: ${tokenResult.exceptionOrNull()?.message}")
                        b.verificationCodeValidationText.text = getString(R.string.register_token_error)
                        binding.nextButton.isEnabled = true
                    }
                } else {
                    Log.e(TAG, "Confirm account failed: ${confirmResult.exceptionOrNull()?.message}")
                    b.verificationCodeValidationText.text = getString(
                        R.string.register_error_detail,
                        confirmResult.exceptionOrNull()?.message.orEmpty()
                    )
                    binding.nextButton.isEnabled = true
                    otpHelper?.clear()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Confirm account error: ${e.message}", e)
                b.verificationCodeValidationText.text = getString(R.string.register_error_detail, e.message.orEmpty())
                binding.nextButton.isEnabled = true
                otpHelper?.clear()
            }
        }
    }

    private fun setupStep5() {
        val b = step5Binding ?: return

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
            binding.nextButton.isEnabled = true
        } else {
            binding.nextButton.isEnabled = false
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
            binding.nextButton.isEnabled = true

            Toast.makeText(this, R.string.register_photo_selected, Toast.LENGTH_SHORT).show()
        } catch (e: Exception) {
            Log.e(TAG, "Load cropped image error: ${e.message}", e)
            Toast.makeText(this, getString(R.string.register_photo_upload_error, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
        }
    }

    private fun setupStep7() {
        val b = step7Binding ?: return

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

    private fun setupStep8() {
        val b = step8Binding ?: return

        // Загружаем 2FA код при открытии шага
        setup2fa()

        b.skip2faButton.setOnClickListener {
            is2faEnabled = false
            saveCurrentStepData()
            currentStep++
            loadStep(currentStep)
        }

        b.copyCodeButton.setOnClickListener {
            val code = b.twoFaSecretCode.text.toString()
            copyToClipboard(code)
            Toast.makeText(this, R.string.register_code_copied, Toast.LENGTH_SHORT).show()
        }

        b.openAuthenticatorButton.setOnClickListener {
            openGoogleAuthenticator()
        }

        // Включаем кнопку "Далее" только после ввода 6-значного кода
        b.otpCodeEditText.doAfterTextChanged {
            binding.nextButton.isEnabled = (it?.length == 6)
        }
    }

    private fun setup2fa() {
        lifecycleScope.launch {
            try {
                val result = grpcManager.getOtpSetup()
                if (result.isSuccess) {
                    val otpResult = result.getOrNull()
                    if (otpResult != null) {
                        step8Binding?.twoFaSecretCode?.text = otpResult.justCode
                    }
                } else {
                    Log.e(TAG, "Get OTP setup failed: ${result.exceptionOrNull()?.message}")
                    Toast.makeText(this@RegisterActivity, R.string.register_2fa_setup_error, Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Get OTP setup error: ${e.message}", e)
                Toast.makeText(this@RegisterActivity, R.string.register_2fa_setup_error, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun setPasswordOnServerAndProceed() {
        if (!validateCurrentStep()) return

        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                val result = grpcManager.setPassword(password)
                if (result.isSuccess) {
                    Log.d(TAG, "Пароль установлен")
                    saveCurrentStepData()
                    currentStep++
                    loadStep(currentStep)
                } else {
                    Log.e(TAG, "Set password failed: ${result.exceptionOrNull()?.message}")
                    Toast.makeText(
                        this@RegisterActivity,
                        getString(R.string.register_error_detail, result.exceptionOrNull()?.message.orEmpty()),
                        Toast.LENGTH_SHORT
                    ).show()
                    binding.nextButton.isEnabled = true
                }
            } catch (e: Exception) {
                Log.e(TAG, "Set password error: ${e.message}", e)
                Toast.makeText(this@RegisterActivity, getString(R.string.register_error_detail, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
                binding.nextButton.isEnabled = true
            }
        }
    }

    private fun uploadAvatarAndProceed() {
        val bytes = avatarBytes ?: return

        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                val uploadResult = grpcManager.uploadUserAvatar(bytes)
                if (uploadResult.isSuccess) {
                    val fileId = uploadResult.getOrNull()!!
                    Log.d(TAG, "Аватар загружен, fileId: $fileId")

                    val setResult = grpcManager.setProfilePicture(fileId)
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
                    binding.nextButton.isEnabled = true
                }
            } catch (e: Exception) {
                Log.e(TAG, "Upload avatar error: ${e.message}", e)
                Toast.makeText(this@RegisterActivity, getString(R.string.register_error_detail, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
                binding.nextButton.isEnabled = true
            }
        }
    }

    private fun saveBioOnServerAndProceed() {
        saveCurrentStepData()

        if (bio.isEmpty()) {
            currentStep++
            loadStep(currentStep)
            return
        }

        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                val result = grpcManager.changeBio(bio)
                if (result.isSuccess) {
                    Log.d(TAG, "Био установлено")
                } else {
                    Log.e(TAG, "Change bio failed: ${result.exceptionOrNull()?.message}")
                }
                currentStep++
                loadStep(currentStep)
            } catch (e: Exception) {
                Log.e(TAG, "Change bio error: ${e.message}", e)
                currentStep++
                loadStep(currentStep)
            }
        }
    }

    private fun verify2faAndProceed() {
        val code = step8Binding?.otpCodeEditText?.text?.toString() ?: return
        if (code.length != 6) return

        binding.nextButton.isEnabled = false

        lifecycleScope.launch {
            try {
                val result = grpcManager.confirmOtpSetup(code)
                if (result.isSuccess) {
                    is2faEnabled = true
                    Toast.makeText(this@RegisterActivity, R.string.register_2fa_configured, Toast.LENGTH_SHORT).show()
                    saveCurrentStepData()
                    currentStep++
                    loadStep(currentStep)
                } else {
                    step8Binding?.otpErrorText?.text = getString(R.string.register_invalid_code)
                    step8Binding?.otpErrorText?.visibility = View.VISIBLE
                    binding.nextButton.isEnabled = true
                }
            } catch (e: Exception) {
                Log.e(TAG, "Verify 2FA error: ${e.message}", e)
                step8Binding?.otpErrorText?.text = getString(R.string.register_error_detail, e.message.orEmpty())
                step8Binding?.otpErrorText?.visibility = View.VISIBLE
                binding.nextButton.isEnabled = true
            }
        }
    }

    private fun resolveThemeColor(attr: Int): Int {
        val typedValue = android.util.TypedValue()
        theme.resolveAttribute(attr, typedValue, true)
        return typedValue.data
    }

    private fun copyToClipboard(text: String) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val clip = ClipData.newPlainText("2FA Code", text)
        clipboard.setPrimaryClip(clip)
    }

    private fun openGoogleAuthenticator() {
        try {
            val intent = packageManager.getLaunchIntentForPackage("com.google.android.apps.authenticator2")
            if (intent != null) {
                startActivity(intent)
            } else {
                val playStoreIntent = Intent(Intent.ACTION_VIEW).apply {
                    data = Uri.parse("market://details?id=com.google.android.apps.authenticator2")
                }
                startActivity(playStoreIntent)
            }
        } catch (e: Exception) {
            Toast.makeText(this, R.string.register_authenticator_open_failed, Toast.LENGTH_SHORT).show()
        }
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
                val userResult = grpcManager.getCurrentUserData()
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

    private fun validateCurrentStep(): Boolean {
        return when (currentStep) {
            1 -> validateFirstName() && validateLastName()
            2 -> {
                if (username.isEmpty()) return false
                val validPattern = Regex("^[a-z0-9_-]+$")
                username.matches(validPattern) && username.length <= MAX_USERNAME_LENGTH
            }
            3 -> {
                if (email.isEmpty()) return false
                android.util.Patterns.EMAIL_ADDRESS.matcher(email).matches()
            }
            5 -> {
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
            5 -> {
                password = step5Binding?.passwordEditText?.text?.toString() ?: ""
            }
            7 -> {
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
        grpcManager.shutdown()
        Log.d(TAG, "gRPC клиенты закрыты")
        step1Binding = null
        step2Binding = null
        step3Binding = null
        step4Binding = null
        step5Binding = null
        step6Binding = null
        step7Binding = null
        step8Binding = null
        step9Binding = null
        otpHelper = null
    }
}
