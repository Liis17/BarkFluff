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

    fun logout() {
        viewModelScope.launch {
            authRepository.logout()
        }
    }
}
