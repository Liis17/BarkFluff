package com.barkfluff.clientv2.ui.screens.login

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class LoginUiState(
    val loading: Boolean = false,
    val otpMode: Boolean = false,
    val error: String? = null,
)

/** Вход: логин/пароль + 2FA (OTP). Логика — аналог V1 LoginActivity.handleAuthResult. */
class LoginViewModel(
    private val appContext: Context,
    private val grpcManager: GrpcManager,
    private val globalParam: GlobalParam
) : ViewModel() {

    private val _ui = MutableStateFlow(LoginUiState())
    val ui: StateFlow<LoginUiState> = _ui.asStateFlow()

    private var login: String = ""
    private var password: String = ""

    init {
        grpcManager.createIdentityClient(globalParam.socketIdentity)
    }

    fun submitCredentials(loginInput: String, passwordInput: String, onSuccess: () -> Unit) {
        login = loginInput.trim()
        password = passwordInput
        authenticate(otpCode = null, onSuccess = onSuccess)
    }

    fun submitOtp(code: String, onSuccess: () -> Unit) {
        authenticate(otpCode = code.trim(), onSuccess = onSuccess)
    }

    private fun authenticate(otpCode: String?, onSuccess: () -> Unit) {
        viewModelScope.launch {
            _ui.update { it.copy(loading = true, error = null) }
            val email = if (login.contains("@")) login else null
            val username = if (login.contains("@")) null else login

            when (val result = grpcManager.auth(email, username, password, otpCode, appContext)) {
                is GrpcManager.AuthResult.Success -> {
                    globalParam.accessToken = result.accessToken
                    globalParam.accessTokenExpiration = result.accessTokenExpiration
                    globalParam.refreshToken = result.refreshToken
                    globalParam.refreshTokenExpiration = result.refreshTokenExpiration

                    grpcManager.createUsersClient(globalParam.socketUsers, appContext)
                    grpcManager.getCurrentUserData().getOrNull()?.let { saveUser(it) }
                    grpcManager.recreateAllClients(appContext, globalParam)

                    _ui.update { it.copy(loading = false) }
                    onSuccess()
                }
                is GrpcManager.AuthResult.OtpRequired -> {
                    _ui.update { it.copy(loading = false, otpMode = true, error = null) }
                }
                is GrpcManager.AuthResult.Error -> {
                    _ui.update { it.copy(loading = false, error = result.message) }
                }
            }
        }
    }

    private fun saveUser(user: GrpcManager.UserData) {
        globalParam.apply {
            userId = user.userId
            userName = user.username
            firstName = user.firstName
            lastName = user.lastName
            description = user.bio
            pictureFileId = user.profilePictureFileId
            picturePreviewFileId = user.profilePicturePreviewFileId
            pictureUrl = user.profilePictureUrl
            picturePreviewUrl = user.profilePicturePreviewUrl
        }
    }
}
