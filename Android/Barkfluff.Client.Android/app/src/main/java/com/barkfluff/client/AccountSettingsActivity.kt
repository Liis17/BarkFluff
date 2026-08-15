package com.barkfluff.client

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityAccountSettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.launch
import java.io.ByteArrayOutputStream
import java.io.File

class AccountSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityAccountSettingsBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager

    companion object {
        private const val TAG = "AccountSettingsActivity"
    }

    private val pickMedia = registerForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) {
            startUCrop(uri)
        }
    }

    private val ucropLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            val uri = UCrop.getOutput(result.data!!)
            if (uri != null) {
                uploadAvatar(uri)
            }
        } else if (result.resultCode == UCrop.RESULT_ERROR) {
            val error = UCrop.getError(result.data!!)
            Toast.makeText(this, getString(R.string.settings_error_detail, error?.message.orEmpty()), Toast.LENGTH_SHORT).show()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityAccountSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        globalParam = GlobalParam(this)
        grpcManager = app.grpcManager

        setupToolbar()
        setupClickListeners()
        updateUI()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }
    }

    private fun setupClickListeners() {
        binding.buttonChangePhoto.setOnClickListener {
            pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
        }

        binding.itemFirstName.setOnClickListener {
            showEditDialog(getString(R.string.account_first_name), globalParam.firstName) { newValue ->
                lifecycleScope.launch {
                    val result = grpcManager.changeName(newValue, globalParam.lastName)
                    if (result.isSuccess) {
                        globalParam.firstName = newValue
                        updateUI()
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.itemLastName.setOnClickListener {
            showEditDialog(getString(R.string.account_last_name), globalParam.lastName, allowEmpty = true) { newValue ->
                lifecycleScope.launch {
                    val result = grpcManager.changeName(globalParam.firstName, newValue)
                    if (result.isSuccess) {
                        globalParam.lastName = newValue
                        updateUI()
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.itemUsername.setOnClickListener {
            showEditDialog(getString(R.string.account_username), globalParam.userName) { newValue ->
                lifecycleScope.launch {
                    val checkResult = grpcManager.checkUsername(newValue)
                    if (checkResult.isSuccess && checkResult.getOrNull() == false) {
                        val result = grpcManager.changeUsername(newValue)
                        if (result.isSuccess) {
                            globalParam.userName = newValue
                            updateUI()
                        } else {
                            Toast.makeText(this@AccountSettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                        }
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, R.string.account_username_taken, Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.itemBio.setOnClickListener {
            showEditDialog(getString(R.string.account_bio), globalParam.description, allowEmpty = true) { newValue ->
                lifecycleScope.launch {
                    val result = grpcManager.changeBio(newValue)
                    if (result.isSuccess) {
                        globalParam.description = newValue
                        updateUI()
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, getString(R.string.settings_error_detail, result.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.buttonLogout.setOnClickListener {
            MaterialAlertDialogBuilder(this)
                .setTitle(R.string.account_logout_title)
                .setMessage(R.string.account_logout_message)
                .setPositiveButton(R.string.account_logout_action) { _, _ ->
                    globalParam.clearUserData()
                    val intent = Intent(this, LoginActivity::class.java)
                    intent.flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
                    startActivity(intent)
                    finishAffinity()
                }
                .setNegativeButton(R.string.btn_cancel, null)
                .show()
        }
    }

    private fun updateUI() {
        binding.textFirstName.text = globalParam.firstName
        binding.textLastName.text = globalParam.lastName
        binding.textUsername.text = getString(R.string.register_username_format, globalParam.userName)
        binding.textBio.text = globalParam.description.ifBlank { getString(R.string.not_specified) }
        loadAvatar()
    }

    private fun loadAvatar() {
        val displayName = getString(
            R.string.register_full_name_format,
            globalParam.firstName,
            globalParam.lastName
        ).trim()

        val urlToUse = globalParam.profilePictureUrl

        if (urlToUse.isNotBlank()) {
            AvatarLoader.load(
                imageView = binding.avatarImage,
                placeholderView = binding.avatarPlaceholder,
                avatarUrl = urlToUse,
                displayName = displayName,
                userId = globalParam.userId
            )
        } else {
            val fileId = globalParam.pictureFileId
            if (fileId.isNotEmpty()) {
                AvatarLoader.loadByFileId(
                    binding.avatarImage,
                    binding.avatarPlaceholder,
                    fileId,
                    displayName,
                    globalParam.userId,
                    size = 192
                ) {
                    val result = grpcManager.getFileDownloadUrl(fileId)
                    if (result.isSuccess) result.getOrNull() else null
                }
            } else {
                AvatarLoader.showPlaceholder(binding.avatarPlaceholder, displayName, globalParam.userId)
                binding.avatarImage.visibility = View.GONE
                binding.avatarPlaceholder.visibility = View.VISIBLE
            }
        }
    }

    private fun showEditDialog(title: String, currentValue: String, allowEmpty: Boolean = false, onSave: (String) -> Unit) {
        val inputLayout = TextInputLayout(this).apply {
            setPadding(
                resources.getDimensionPixelSize(android.R.dimen.notification_large_icon_width) / 3,
                0,
                resources.getDimensionPixelSize(android.R.dimen.notification_large_icon_width) / 3,
                0
            )
        }
        val editText = TextInputEditText(inputLayout.context).apply {
            setText(currentValue)
        }
        inputLayout.addView(editText)

        MaterialAlertDialogBuilder(this)
            .setTitle(title)
            .setView(inputLayout)
            .setPositiveButton(R.string.btn_save) { _, _ ->
                val newValue = editText.text?.toString()?.trim() ?: ""
                if ((allowEmpty || newValue.isNotEmpty()) && newValue != currentValue) {
                    onSave(newValue)
                }
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    private fun startUCrop(uri: Uri) {
        val destinationUri = Uri.fromFile(File(cacheDir, "cropped_avatar_${System.currentTimeMillis()}.jpg"))

        val options = UCrop.Options().apply {
            setCompressionFormat(Bitmap.CompressFormat.JPEG)
            setCompressionQuality(80)
            setToolbarColor(getColor(android.R.color.white))
            setStatusBarColor(getColor(android.R.color.black))
            withAspectRatio(1f, 1f)
            withMaxResultSize(512, 512)
        }

        val uCrop = UCrop.of(uri, destinationUri)
            .withAspectRatio(1f, 1f)
            .withMaxResultSize(512, 512)
            .withOptions(options)

        ucropLauncher.launch(uCrop.getIntent(this))
    }

    private fun uploadAvatar(uri: Uri) {
        try {
            val inputStream = contentResolver.openInputStream(uri)
            val bitmap = BitmapFactory.decodeStream(inputStream)
            inputStream?.close()

            val outputStream = ByteArrayOutputStream()
            bitmap.compress(Bitmap.CompressFormat.JPEG, 80, outputStream)
            val bytes = outputStream.toByteArray()

            lifecycleScope.launch {
                try {
                    val uploadResult = grpcManager.uploadUserAvatar(bytes)
                    if (uploadResult.isSuccess) {
                        val fileId = uploadResult.getOrNull()!!
                        Log.d(TAG, "Аватар загружен, fileId: $fileId")

                        val setResult = grpcManager.setProfilePicture(fileId)
                        if (setResult.isSuccess) {
                            // Обновляем данные пользователя
                            val userDataResult = grpcManager.getCurrentUserData()
                            if (userDataResult.isSuccess) {
                                val userData = userDataResult.getOrNull()!!
                                globalParam.pictureFileId = userData.profilePictureFileId
                                globalParam.picturePreviewFileId = userData.profilePicturePreviewFileId
                                globalParam.picturePreviewUrl = userData.profilePicturePreviewUrl
                                globalParam.profilePictureUrl = userData.profilePictureUrl
                            }
                            updateUI()
                            Toast.makeText(this@AccountSettingsActivity, R.string.account_avatar_updated, Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(this@AccountSettingsActivity, R.string.account_avatar_set_error, Toast.LENGTH_SHORT).show()
                        }
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, getString(R.string.account_avatar_upload_error, uploadResult.exceptionOrNull()?.message.orEmpty()), Toast.LENGTH_SHORT).show()
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Ошибка загрузки аватара", e)
                    Toast.makeText(this@AccountSettingsActivity, R.string.account_avatar_load_error, Toast.LENGTH_SHORT).show()
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка чтения изображения", e)
            Toast.makeText(this, R.string.account_image_read_error, Toast.LENGTH_SHORT).show()
        }
    }
}
