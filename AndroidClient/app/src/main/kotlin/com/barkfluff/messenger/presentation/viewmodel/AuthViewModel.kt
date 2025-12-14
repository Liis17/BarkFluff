package com.barkfluff.messenger.presentation.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.messenger.data.repository.AuthRepository
import com.barkfluff.messenger.domain.model.AuthToken
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class AuthEvent {
    data class LoginSuccess(val token: AuthToken) : AuthEvent()
    data class RegisterSuccess(val email: String) : AuthEvent()
    data class ConfirmSuccess(val token: AuthToken) : AuthEvent()
    data class ResetPasswordSent(val email: String) : AuthEvent()
    data class PasswordResetSuccess(val message: String) : AuthEvent()
    data class OtpEnabled(val setupInfo: com.barkfluff.messenger.data.repository.OtpSetupInfo) : AuthEvent()
    object OtpConfirmed : AuthEvent()
    data class Error(val message: String) : AuthEvent()
}

data class AuthState(
    val isLoading: Boolean = false,
    val email: String = "",
    val username: String = "",
    val password: String = "",
    val firstName: String = "",
    val lastName: String = "",
    val otpCode: String = "",
    val verificationCode: String = ""
)

@HiltViewModel
class AuthViewModel @Inject constructor(
    private val authRepository: AuthRepository
) : ViewModel() {

    private val _state = MutableStateFlow(AuthState())
    val state: StateFlow<AuthState> = _state.asStateFlow()

    private val _events = MutableSharedFlow<AuthEvent>()
    val events = _events.asSharedFlow()

    fun login(emailOrUsername: String, password: String, otpCode: String? = null) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.login(emailOrUsername, password, otpCode)
                .onSuccess { token ->
                    _events.emit(AuthEvent.LoginSuccess(token))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Login failed"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun register(
        email: String,
        username: String,
        password: String,
        firstName: String,
        lastName: String
    ) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.register(email, username, password, firstName, lastName)
                .onSuccess {
                    _events.emit(AuthEvent.RegisterSuccess(email))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Registration failed"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun confirmAccount(email: String, code: String) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.confirmAccount(email, code)
                .onSuccess { token ->
                    _events.emit(AuthEvent.ConfirmSuccess(token))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Confirmation failed"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun resetPassword(email: String) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.resetPassword(email)
                .onSuccess {
                    _events.emit(AuthEvent.ResetPasswordSent(email))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Failed to send reset code"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun confirmResetPassword(email: String, code: String, newPassword: String) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.confirmResetPassword(email, code, newPassword)
                .onSuccess {
                    _events.emit(AuthEvent.PasswordResetSuccess("Password reset successfully"))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Failed to reset password"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun setPassword(oldPassword: String, newPassword: String) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.setPassword(oldPassword, newPassword)
                .onSuccess {
                    _events.emit(AuthEvent.PasswordResetSuccess("Password changed successfully"))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Failed to change password"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun enableOtp(otpType: com.barkfluff.messenger.data.repository.OtpType) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.enableOtp(otpType)
                .onSuccess { setupInfo ->
                    _events.emit(AuthEvent.OtpEnabled(setupInfo))
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Failed to enable 2FA"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun confirmOtp(otpType: com.barkfluff.messenger.data.repository.OtpType, code: String) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            authRepository.confirmOtp(otpType, code)
                .onSuccess {
                    _events.emit(AuthEvent.OtpConfirmed)
                }
                .onFailure { error ->
                    _events.emit(AuthEvent.Error(error.message ?: "Failed to confirm 2FA"))
                }

            _state.value = _state.value.copy(isLoading = false)
        }
    }

    fun listOtpVerification(): List<com.barkfluff.messenger.data.repository.OtpMethod> {
        var methods = emptyList<com.barkfluff.messenger.data.repository.OtpMethod>()
        viewModelScope.launch {
            authRepository.listOtpVerification()
                .onSuccess { otpMethods ->
                    methods = otpMethods
                }
        }
        return methods
    }

    suspend fun getOtpMethods(): Result<List<com.barkfluff.messenger.data.repository.OtpMethod>> {
        return authRepository.listOtpVerification()
    }

    fun logout() {
        viewModelScope.launch {
            authRepository.logout()
        }
    }
}
