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
            Toast.makeText(this, "Ошибка: ${error?.message}", Toast.LENGTH_SHORT).show()
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
            showEditDialog("Имя", globalParam.firstName) { newValue ->
                lifecycleScope.launch {
                    val result = grpcManager.changeName(newValue, globalParam.lastName)
                    if (result.isSuccess) {
                        globalParam.firstName = newValue
                        updateUI()
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.itemLastName.setOnClickListener {
            showEditDialog("Фамилия", globalParam.lastName) { newValue ->
                lifecycleScope.launch {
                    val result = grpcManager.changeName(globalParam.firstName, newValue)
                    if (result.isSuccess) {
                        globalParam.lastName = newValue
                        updateUI()
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.itemUsername.setOnClickListener {
            showEditDialog("Имя пользователя", globalParam.userName) { newValue ->
                lifecycleScope.launch {
                    val checkResult = grpcManager.checkUsername(newValue)
                    if (checkResult.isSuccess && checkResult.getOrNull() == true) {
                        val result = grpcManager.changeUsername(newValue)
                        if (result.isSuccess) {
                            globalParam.userName = newValue
                            updateUI()
                        } else {
                            Toast.makeText(this@AccountSettingsActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                        }
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, "Имя пользователя уже занято", Toast.LENGTH_SHORT).show()
                    }
                }
            }
        }

        binding.buttonLogout.setOnClickListener {
            MaterialAlertDialogBuilder(this)
                .setTitle("Выход")
                .setMessage("Вы уверены, что хотите выйти из аккаунта?")
                .setPositiveButton("Выйти") { _, _ ->
                    globalParam.clearUserData()
                    val intent = Intent(this, LoginActivity::class.java)
                    intent.flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
                    startActivity(intent)
                    finishAffinity()
                }
                .setNegativeButton("Отмена", null)
                .show()
        }
    }

    private fun updateUI() {
        binding.textFirstName.text = globalParam.firstName
        binding.textLastName.text = globalParam.lastName
        binding.textUsername.text = "@${globalParam.userName}"
        loadAvatar()
    }

    private fun loadAvatar() {
        val fileId = globalParam.picturePreviewFileId.ifEmpty { globalParam.pictureFileId }
        val displayName = "${globalParam.firstName} ${globalParam.lastName}".trim()

        if (fileId.isNotEmpty()) {
            AvatarLoader.loadByFileId(
                binding.avatarImage,
                binding.avatarPlaceholder,
                fileId,
                displayName,
                globalParam.userId
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

    private fun showEditDialog(title: String, currentValue: String, onSave: (String) -> Unit) {
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
            .setPositiveButton("Сохранить") { _, _ ->
                val newValue = editText.text?.toString()?.trim() ?: ""
                if (newValue.isNotEmpty() && newValue != currentValue) {
                    onSave(newValue)
                }
            }
            .setNegativeButton("Отмена", null)
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
                            }
                            updateUI()
                            Toast.makeText(this@AccountSettingsActivity, "Аватар обновлен", Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(this@AccountSettingsActivity, "Ошибка установки аватара", Toast.LENGTH_SHORT).show()
                        }
                    } else {
                        Toast.makeText(this@AccountSettingsActivity, "Ошибка загрузки: ${uploadResult.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Ошибка загрузки аватара", e)
                    Toast.makeText(this@AccountSettingsActivity, "Ошибка загрузки аватара", Toast.LENGTH_SHORT).show()
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка чтения изображения", e)
            Toast.makeText(this, "Ошибка чтения изображения", Toast.LENGTH_SHORT).show()
        }
    }
}
