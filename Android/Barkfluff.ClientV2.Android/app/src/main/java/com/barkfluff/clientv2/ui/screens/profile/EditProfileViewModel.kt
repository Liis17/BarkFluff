package com.barkfluff.clientv2.ui.screens.profile

import android.content.Context
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.ImageCompressor
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

enum class UsernameStatus { IDLE, CHECKING, AVAILABLE, TAKEN, INVALID }

data class EditProfileUiState(
    val firstName: String = "",
    val lastName: String = "",
    val username: String = "",
    val bio: String = "",
    val avatarUrl: String = "",
    val usernameStatus: UsernameStatus = UsernameStatus.IDLE,
    val uploadingAvatar: Boolean = false,
    val saving: Boolean = false,
    val error: String? = null,
    val done: Boolean = false,
)

class EditProfileViewModel(
    private val appContext: Context,
    private val grpcManager: GrpcManager,
    private val globalParam: GlobalParam,
) : ViewModel() {

    private val originalUsername = globalParam.userName

    private val _ui = MutableStateFlow(
        EditProfileUiState(
            firstName = globalParam.firstName,
            lastName = globalParam.lastName,
            username = globalParam.userName,
            bio = globalParam.description,
            avatarUrl = globalParam.picturePreviewUrl,
        )
    )
    val ui: StateFlow<EditProfileUiState> = _ui.asStateFlow()

    private var usernameCheckJob: Job? = null

    fun setFirstName(value: String) = _ui.update { it.copy(firstName = value, error = null) }
    fun setLastName(value: String) = _ui.update { it.copy(lastName = value, error = null) }
    fun setBio(value: String) = _ui.update { it.copy(bio = value, error = null) }

    fun setUsername(value: String) {
        _ui.update { it.copy(username = value, error = null) }
        usernameCheckJob?.cancel()
        when {
            value == originalUsername -> _ui.update { it.copy(usernameStatus = UsernameStatus.IDLE) }
            !isValidUsername(value) -> _ui.update { it.copy(usernameStatus = UsernameStatus.INVALID) }
            else -> {
                _ui.update { it.copy(usernameStatus = UsernameStatus.CHECKING) }
                usernameCheckJob = viewModelScope.launch {
                    delay(500)
                    grpcManager.checkUsername(value)
                        .onSuccess { exists ->
                            _ui.update {
                                it.copy(usernameStatus = if (exists) UsernameStatus.TAKEN else UsernameStatus.AVAILABLE)
                            }
                        }
                        .onFailure { _ui.update { it.copy(usernameStatus = UsernameStatus.IDLE) } }
                }
            }
        }
    }

    fun pickAvatar(uri: Uri) {
        viewModelScope.launch {
            _ui.update { it.copy(uploadingAvatar = true, error = null) }
            val bytes = withContext(Dispatchers.IO) { ImageCompressor.compressImage(uri, appContext) }
                .getOrNull()
            if (bytes == null) {
                _ui.update { it.copy(uploadingAvatar = false, error = "Не удалось обработать изображение") }
                return@launch
            }
            grpcManager.uploadUserAvatar(bytes)
                .onSuccess { fileId ->
                    grpcManager.setProfilePicture(fileId)
                    val url = grpcManager.getFileDownloadUrl(fileId).getOrNull() ?: ""
                    if (url.isNotBlank()) globalParam.picturePreviewUrl = url
                    _ui.update { it.copy(uploadingAvatar = false, avatarUrl = url.ifBlank { it.avatarUrl }) }
                }
                .onFailure { e ->
                    _ui.update { it.copy(uploadingAvatar = false, error = e.message ?: "Не удалось загрузить аватар") }
                }
        }
    }

    fun save() {
        val s = _ui.value
        if (s.usernameStatus == UsernameStatus.TAKEN || s.usernameStatus == UsernameStatus.INVALID) {
            _ui.update { it.copy(error = "Имя пользователя недоступно") }
            return
        }
        viewModelScope.launch {
            _ui.update { it.copy(saving = true, error = null) }
            try {
                if (s.firstName != globalParam.firstName || s.lastName != globalParam.lastName) {
                    grpcManager.changeName(s.firstName.trim(), s.lastName.trim()).getOrThrow()
                    globalParam.firstName = s.firstName.trim()
                    globalParam.lastName = s.lastName.trim()
                }
                if (s.username != originalUsername) {
                    grpcManager.changeUsername(s.username.trim()).getOrThrow()
                    globalParam.userName = s.username.trim()
                }
                if (s.bio != globalParam.description) {
                    grpcManager.changeBio(s.bio.trim()).getOrThrow()
                    globalParam.description = s.bio.trim()
                }
                _ui.update { it.copy(saving = false, done = true) }
            } catch (e: Exception) {
                _ui.update { it.copy(saving = false, error = e.message ?: "Не удалось сохранить") }
            }
        }
    }

    private fun isValidUsername(value: String): Boolean =
        value.length in 3..32 && value.all { it.isLetterOrDigit() || it == '_' }
}
