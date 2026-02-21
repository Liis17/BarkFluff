package com.barkfluff.client

import android.Manifest
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
import android.provider.MediaStore
import android.view.LayoutInflater
import android.view.View
import android.widget.ImageView
import android.widget.LinearLayout
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
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch
import java.io.ByteArrayOutputStream

/**
 * Активность регистрации
 * Реализует 9 шагов как в десктопном приложении:
 * 1. Имя и фамилия
 * 2. Логин
 * 3. Email
 * 4. Код подтверждения
 * 5. Пароль
 * 6. Аватар
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
    }

    private lateinit var binding: ActivityRegisterBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    private var currentStep = 1
    private var isLastStep = false

    // Данные регистрации
    private var firstName = ""
    private var lastName = ""
    private var username = ""
    private var email = ""
    private var password = ""
    private var bio = ""
    private var avatarBytes: ByteArray? = null
    private var userId: String? = null
    private var is2faEnabled = false

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
            cropAndSetAvatar(uri)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityRegisterBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager()

        // Отображаем имя сервера
        binding.serverNameText.text = globalParam.serverName.ifBlank { "BarkFluff" }

        setupClickListeners()
        loadStep(1)
    }

    private fun setupClickListeners() {
        binding.nextButton.setOnClickListener {
            if (validateCurrentStep()) {
                if (currentStep < TOTAL_STEPS) {
                    saveCurrentStepData()
                    currentStep++
                    loadStep(currentStep)
                } else {
                    completeRegistration()
                }
            }
        }

        binding.backButton.setOnClickListener {
            if (currentStep > 1) {
                saveCurrentStepData()
                currentStep--
                loadStep(currentStep)
            }
        }
    }

    private fun loadStep(step: Int) {
        // Очищаем предыдущий view
        binding.contentFrame.removeAllViews()

        // Обновляем индикатор шага
        binding.stepIndicatorText.text = "Шаг $step из $TOTAL_STEPS"
        binding.stepProgressBar.setProgressCompat((step - 1) * 100 / TOTAL_STEPS, true)

        // Показываем/скрываем кнопки
        binding.backButton.visibility = if (step > 1) View.VISIBLE else View.GONE
        binding.nextButton.text = if (step == TOTAL_STEPS) "Готово" else "Далее"

        // Загружаем соответствующий layout
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

        updateUI()
    }

    private fun updateUI() {
        // Обновляем состояние кнопок
        binding.nextButton.isEnabled = when (currentStep) {
            8 -> !is2faEnabled || step8Binding?.otpCodeEditText?.text?.length == 6
            else -> true
        }
    }

    private fun setupStep1() {
        val b = step1Binding ?: return

        // Восстанавливаем данные если есть
        b.firstNameEditText.setText(firstName)
        b.lastNameEditText.setText(lastName)

        // Валидация имени
        b.firstNameEditText.doAfterTextChanged {
            firstName = it?.toString()?.trim() ?: ""
            validateFirstName()
        }

        b.lastNameEditText.doAfterTextChanged {
            lastName = it?.toString()?.trim() ?: ""
            validateLastName()
        }
    }

    private fun validateFirstName(): Boolean {
        val b = step1Binding ?: return false
        return if (firstName.length < MIN_NAME_LENGTH && firstName.isNotEmpty()) {
            b.firstNameValidationText.text = "Минимум $MIN_NAME_LENGTH символа"
            b.firstNameValidationText.visibility = View.VISIBLE
            false
        } else if (firstName.length > MAX_NAME_LENGTH) {
            b.firstNameValidationText.text = "Максимум $MAX_NAME_LENGTH символов"
            b.firstNameValidationText.visibility = View.VISIBLE
            false
        } else {
            b.firstNameValidationText.visibility = View.GONE
            true
        }
    }

    private fun validateLastName(): Boolean {
        val b = step1Binding ?: return false
        return if (lastName.length > MAX_NAME_LENGTH) {
            b.lastNameValidationText.text = "Максимум $MAX_NAME_LENGTH символов"
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
            validateUsername()
        }
    }

    private fun validateUsername(): Boolean {
        val b = step2Binding ?: return false

        if (username.isEmpty()) {
            b.usernameValidationText.text = "Введите логин"
            b.usernameValidationText.visibility = View.VISIBLE
            return false
        }

        // Проверка формата: латиница, цифры, _, -
        val validPattern = Regex("^[a-z0-9_-]+$")
        if (!username.matches(validPattern)) {
            b.usernameValidationText.text = "Только латиница, цифры, _ и -"
            b.usernameValidationText.visibility = View.VISIBLE
            return false
        }

        if (username.length > MAX_USERNAME_LENGTH) {
            b.usernameValidationText.text = "Максимум $MAX_USERNAME_LENGTH символов"
            b.usernameValidationText.visibility = View.VISIBLE
            return false
        }

        b.usernameValidationText.visibility = View.GONE
        return true
    }

    private fun setupStep3() {
        val b = step3Binding ?: return

        b.emailEditText.setText(email)

        b.emailEditText.doAfterTextChanged {
            email = it?.toString()?.trim()?.lowercase() ?: ""
            validateEmail()
        }
    }

    private fun validateEmail(): Boolean {
        val b = step3Binding ?: return false

        if (email.isEmpty()) {
            b.emailValidationText.text = "Введите email"
            b.emailValidationText.visibility = View.VISIBLE
            return false
        }

        // Простая валидация email
        val emailPattern = android.util.Patterns.EMAIL_ADDRESS
        if (!emailPattern.matcher(email).matches()) {
            b.emailValidationText.text = "Некорректный email"
            b.emailValidationText.visibility = View.VISIBLE
            return false
        }

        b.emailValidationText.visibility = View.GONE
        return true
    }

    private fun setupStep4() {
        val b = step4Binding ?: return

        b.verificationCodeEditText.doAfterTextChanged {
            updateUI()
        }
    }

    private fun setupStep5() {
        val b = step5Binding ?: return

        b.passwordEditText.doAfterTextChanged {
            password = it?.toString() ?: ""
            updatePasswordStrength(password)
        }

        b.confirmPasswordEditText.doAfterTextChanged {
            val confirm = it?.toString() ?: ""
            validatePasswordMatch(confirm)
        }
    }

    private fun updatePasswordStrength(password: String) {
        val b = step5Binding ?: return

        var score = 0
        var hasMinLength = false
        var hasUpperCase = false
        var hasLowerCase = false
        var hasDigit = false
        var hasSpecial = false

        if (password.length >= MIN_PASSWORD_LENGTH) {
            score += 20
            hasMinLength = true
        }
        if (password.any { it.isUpperCase() }) {
            score += 20
            hasUpperCase = true
        }
        if (password.any { it.isLowerCase() }) {
            score += 20
            hasLowerCase = true
        }
        if (password.any { it.isDigit() }) {
            score += 20
            hasDigit = true
        }
        if (password.any { !it.isLetterOrDigit() }) {
            score += 20
            hasSpecial = true
        }

        b.passwordStrengthBar.setProgressCompat(score, true)

        // Обновляем индикатор сложности
        val difficultyText = when {
            score == 0 -> "Начните вводить пароль"
            score < 40 -> "Слабый"
            score < 60 -> "Средний"
            score < 80 -> "Хороший"
            else -> "Надежный"
        }
        b.passwordDifficultyIndicator.text = difficultyText

        // Обновляем чеклист
        updateRequirement(b.reqMinLength, hasMinLength)
        updateRequirement(b.reqUpperCase, hasUpperCase)
        updateRequirement(b.reqLowerCase, hasLowerCase)
        updateRequirement(b.reqDigit, hasDigit)
        updateRequirement(b.reqSpecialChar, hasSpecial)
    }

    private fun updateRequirement(textView: android.widget.TextView, met: Boolean) {
        val color = if (met) ContextCompat.getColor(this, android.R.color.holo_green_light) else ContextCompat.getColor(this, android.R.color.darker_gray)
        textView.text = if (met) "● ${textView.text.toString().substring(2)}" else "○ ${textView.text.toString().substring(2)}"
        textView.setTextColor(color)
    }

    private fun validatePasswordMatch(confirm: String): Boolean {
        val b = step5Binding ?: return false

        if (confirm.isEmpty()) {
            b.passwordMatchText.text = ""
            return true
        }

        return if (password != confirm) {
            b.passwordMatchText.text = "Пароли не совпадают"
            b.passwordMatchText.visibility = View.VISIBLE
            false
        } else {
            b.passwordMatchText.text = "Пароли совпадают"
            b.passwordMatchText.visibility = View.VISIBLE
            true
        }
    }

    private fun setupStep6() {
        val b = step6Binding ?: return

        b.uploadAvatarButton.setOnClickListener {
            checkPermissionAndPickImage()
        }

        b.skipAvatarButton.setOnClickListener {
            avatarBytes = null
            // Переход на следующий шаг
            if (currentStep < TOTAL_STEPS) {
                currentStep++
                loadStep(currentStep)
            }
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
                    Manifest.permission.READ_MEDIA_IMAGES
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                requestPermissions(arrayOf(Manifest.permission.READ_MEDIA_IMAGES), 100)
                return
            }
        } else if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.Q) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_EXTERNAL_STORAGE
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                requestPermissions(arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE), 100)
                return
            }
        }

        pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
    }

    private fun cropAndSetAvatar(uri: Uri) {
        try {
            // Получаем bitmap и сжимаем его
            val inputStream = contentResolver.openInputStream(uri)
            val bitmap = BitmapFactory.decodeStream(inputStream)
            inputStream?.close()

            // Создаем квадратное изображение (кроп по центру)
            val size = minOf(bitmap.width, bitmap.height)
            val x = (bitmap.width - size) / 2
            val y = (bitmap.height - size) / 2
            val croppedBitmap = Bitmap.createBitmap(bitmap, x, y, size, size)

            // Сжимаем в JPEG
            val outputStream = ByteArrayOutputStream()
            croppedBitmap.compress(Bitmap.CompressFormat.JPEG, 80, outputStream)
            avatarBytes = outputStream.toByteArray()

            // Показываем результат
            step6Binding?.croppedImageView?.setImageBitmap(croppedBitmap)
            step6Binding?.croppedImageView?.visibility = View.VISIBLE
            step6Binding?.avatarPlaceholder?.visibility = View.GONE

            Toast.makeText(this, "Фото выбрано", Toast.LENGTH_SHORT).show()
        } catch (e: Exception) {
            Toast.makeText(this, "Ошибка загрузки фото: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }

    private fun setupStep7() {
        val b = step7Binding ?: return

        // Обновляем предпросмотр
        b.previewFullName.text = "$firstName $lastName".trim()
        b.previewUsername.text = "@$username"

        // Устанавливаем аватар если есть
        avatarBytes?.let { bytes ->
            val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
            b.previewAvatar.setImageBitmap(bitmap)
        }

        b.bioEditText.setText(bio)

        b.bioEditText.doAfterTextChanged {
            bio = it?.toString() ?: ""
            if (bio.length > MAX_BIO_LENGTH) {
                b.bioInputLayout.error = "Максимум $MAX_BIO_LENGTH символов"
            } else {
                b.bioInputLayout.error = null
            }
        }
    }

    private fun setupStep8() {
        val b = step8Binding ?: return

        b.setup2faButton.setOnClickListener {
            b.twoFaSetupPanel.visibility = View.VISIBLE
            setup2fa()
        }

        b.skip2faButton.setOnClickListener {
            is2faEnabled = false
            // Переход на следующий шаг
            if (currentStep < TOTAL_STEPS) {
                currentStep++
                loadStep(currentStep)
            }
        }

        b.copyCodeButton.setOnClickListener {
            val code = b.twoFaSecretCode.text.toString()
            copyToClipboard(code)
            Toast.makeText(this, "Код скопирован", Toast.LENGTH_SHORT).show()
        }

        b.openAuthenticatorButton.setOnClickListener {
            openGoogleAuthenticator()
        }

        b.verifyOtpButton.setOnClickListener {
            val code = b.otpCodeEditText.text.toString()
            if (code.length == 6) {
                verify2faCode(code)
            }
        }
    }

    private fun setup2fa() {
        lifecycleScope.launch {
            val result = grpcManager.getOtpSetup()
            if (result.isSuccess) {
                val otpResult = result.getOrNull()
                if (otpResult != null) {
                    step8Binding?.twoFaSecretCode?.text = otpResult.justCode
                }
            } else {
                Toast.makeText(this@RegisterActivity, "Ошибка настройки 2FA", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun verify2faCode(code: String) {
        lifecycleScope.launch {
            val result = grpcManager.confirmOtpSetup(code)
            if (result.isSuccess) {
                is2faEnabled = true
                Toast.makeText(this@RegisterActivity, "2FA настроена", Toast.LENGTH_SHORT).show()
                step8Binding?.twoFaSetupPanel?.visibility = View.GONE
                updateUI()
            } else {
                step8Binding?.otpErrorText?.text = "Неверный код"
                step8Binding?.otpErrorText?.visibility = View.VISIBLE
            }
        }
    }

    /**
     * Завершение регистрации - создание аккаунта
     */
    private fun completeRegistration() {
        val b = step9Binding ?: return

        b.finalLoadingIndicator.visibility = View.VISIBLE
        b.goToLoginButton.isEnabled = false

        lifecycleScope.launch {
            try {
                // Создаем Identity и Users клиенты
                val identityResult = grpcManager.createIdentityClient(globalParam.socketIdentity)
                if (identityResult.isFailure) {
                    showError("Ошибка подключения к серверу")
                    return@launch
                }

                val usersResult = grpcManager.createUsersClient(globalParam.socketUsers)
                if (usersResult.isFailure) {
                    showError("Ошибка подключения к серверу")
                    return@launch
                }

                // Шаг 1: Создаем аккаунт
                val createResult = grpcManager.createAccount(firstName, lastName, email, username)
                if (createResult.isFailure) {
                    showError(createResult.exceptionOrNull()?.message ?: "Ошибка создания аккаунта")
                    return@launch
                }

                userId = createResult.getOrNull()

                // Шаг 2: Подтверждаем код с почты
                val verificationCode = step4Binding?.verificationCodeEditText?.text?.toString() ?: ""
                val confirmResult = grpcManager.confirmAccount(userId!!, verificationCode)
                if (confirmResult.isFailure) {
                    showError(confirmResult.exceptionOrNull()?.message ?: "Ошибка подтверждения")
                    return@launch
                }

                // Сохраняем refresh токен
                val confirmData = confirmResult.getOrNull()
                if (confirmData != null) {
                    globalParam.refreshToken = confirmData.refreshToken
                    globalParam.refreshTokenExpiration = confirmData.refreshTokenExpiration
                }

                // Шаг 3: Устанавливаем пароль
                val passwordResult = grpcManager.setPassword(password)
                if (passwordResult.isFailure) {
                    showError(passwordResult.exceptionOrNull()?.message ?: "Ошибка установки пароля")
                    return@launch
                }

                // Шаг 4: Загружаем аватар если есть
                // Примечание: Загрузка аватара требует HTTP PUT запрос на S3
                // Это будет реализовано отдельно через OkHttp
                if (avatarBytes != null) {
                    // TODO: Реализовать загрузку аватара через S3
                    /*
                    val filesResult = grpcManager.createFilesClient(globalParam.socketFiles)
                    if (filesResult.isSuccess) {
                        val uploadUrlResult = grpcManager.getUploadUrl(FilesApiOuterClass.UploadFileType.USER_AVATAR)
                        if (uploadUrlResult.isSuccess) {
                            val uploadData = uploadUrlResult.getOrNull()!!
                            // Выполнить HTTP PUT запрос на uploadUrl.url с avatarBytes
                            // Затем вызвать grpcManager.setProfilePicture(uploadData.fileId)
                        }
                    }
                    */
                }

                // Шаг 5: Устанавливаем био
                if (bio.isNotEmpty()) {
                    grpcManager.changeBio(bio)
                }

                b.finalLoadingIndicator.visibility = View.GONE
                Toast.makeText(this@RegisterActivity, "Аккаунт успешно создан!", Toast.LENGTH_LONG).show()
            } catch (e: Exception) {
                showError("Ошибка: ${e.message}")
            }
        }
    }

    private fun showError(message: String) {
        MaterialAlertDialogBuilder(this)
            .setTitle("Ошибка")
            .setMessage(message)
            .setPositiveButton("OK", null)
            .show()
    }

    private fun copyToClipboard(text: String) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val clip = ClipData.newPlainText("2FA Code", text)
        clipboard.setPrimaryClip(clip)
    }

    private fun openGoogleAuthenticator() {
        try {
            // Пытаемся открыть Google Authenticator
            val intent = packageManager.getLaunchIntentForPackage("com.google.android.apps.authenticator2")
            if (intent != null) {
                startActivity(intent)
            } else {
                // Если не установлено, открываем Play Store
                val playStoreIntent = Intent(Intent.ACTION_VIEW).apply {
                    data = Uri.parse("market://details?id=com.google.android.apps.authenticator2")
                }
                startActivity(playStoreIntent)
            }
        } catch (e: Exception) {
            Toast.makeText(this, "Не удалось открыть аутентификатор", Toast.LENGTH_SHORT).show()
        }
    }

    private fun setupStep9() {
        val b = step9Binding ?: return

        b.goToLoginButton.setOnClickListener {
            finish() // Возвращаемся на LoginActivity
        }
    }

    private fun validateCurrentStep(): Boolean {
        return when (currentStep) {
            1 -> validateFirstName() && validateLastName()
            2 -> validateUsername()
            3 -> validateEmail()
            4 -> {
                val code = step4Binding?.verificationCodeEditText?.text?.toString() ?: ""
                if (code.length != 6) {
                    step4Binding?.verificationCodeValidationText?.text = "Введите 6-значный код"
                    step4Binding?.verificationCodeValidationText?.visibility = View.VISIBLE
                    return false
                }
                true
            }
            5 -> {
                if (password.length < MIN_PASSWORD_LENGTH) {
                    step5Binding?.passwordMatchText?.text = "Минимум $MIN_PASSWORD_LENGTH символов"
                    step5Binding?.passwordMatchText?.visibility = View.VISIBLE
                    return false
                }
                val confirm = step5Binding?.confirmPasswordEditText?.text?.toString() ?: ""
                password == confirm
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
                // Код подтверждения будет использован сразу
            }
            5 -> {
                password = step5Binding?.passwordEditText?.text?.toString() ?: ""
            }
            7 -> {
                bio = step7Binding?.bioEditText?.text?.toString()?.trim() ?: ""
            }
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 100) {
            if (grantResults.isNotEmpty() && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
            } else {
                Toast.makeText(this, "Разрешение на доступ к фото не предоставлено", Toast.LENGTH_SHORT).show()
            }
        }
    }

    override fun onBackPressed() {
        if (currentStep > 1) {
            currentStep--
            loadStep(currentStep)
        } else {
            super.onBackPressed()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
        step1Binding = null
        step2Binding = null
        step3Binding = null
        step4Binding = null
        step5Binding = null
        step6Binding = null
        step7Binding = null
        step8Binding = null
        step9Binding = null
    }
}
