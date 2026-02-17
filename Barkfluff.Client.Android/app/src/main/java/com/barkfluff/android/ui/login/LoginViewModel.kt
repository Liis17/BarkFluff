package com.barkfluff.android.ui.login

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.android.data.local.AppDataStore
import com.barkfluff.android.data.model.AuthResult
import com.barkfluff.android.repository.AuthRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class LoginUiState(
    val isLoading: Boolean = false,
    val showOtpField: Boolean = false,
    val otpError: Boolean = false,
    val error: String? = null,
    val serverName: String = "",
    val serverColorMain: String = "",
    val navigateToChatList: Boolean = false,
)

@HiltViewModel
class LoginViewModel @Inject constructor(
    private val authRepository: AuthRepository,
    private val dataStore: AppDataStore,
) : ViewModel() {

    private val _uiState = MutableStateFlow(LoginUiState())
    val uiState: StateFlow<LoginUiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            val serverName = dataStore.getServerName()
            val colorMain = dataStore.getColorMain()
            _uiState.update { it.copy(serverName = serverName, serverColorMain = colorMain) }
        }
    }

    fun login(login: String, password: String, otpCode: String?) {
        if (_uiState.value.isLoading) return
        viewModelScope.launch {
            _uiState.update { it.copy(isLoading = true, error = null, otpError = false) }
            when (val result = authRepository.login(login, password, otpCode)) {
                is AuthResult.Success -> {
                    _uiState.update { it.copy(isLoading = false, navigateToChatList = true) }
                }
                is AuthResult.NeedsOtp -> {
                    _uiState.update {
                        it.copy(isLoading = false, showOtpField = true, error = "Введите код двухфакторной аутентификации")
                    }
                }
                is AuthResult.WrongOtp -> {
                    _uiState.update {
                        it.copy(isLoading = false, showOtpField = true, otpError = true, error = "Неверный код. Попробуйте ещё раз")
                    }
                }
                is AuthResult.Error -> {
                    _uiState.update { it.copy(isLoading = false, error = result.message) }
                }
            }
        }
    }

    fun onNavigatedToChatList() {
        _uiState.update { it.copy(navigateToChatList = false) }
    }

    fun reloadServerInfo() {
        viewModelScope.launch {
            val serverName = dataStore.getServerName()
            val colorMain = dataStore.getColorMain()
            _uiState.update { it.copy(serverName = serverName, serverColorMain = colorMain) }
        }
    }

    fun clearError() {
        _uiState.update { it.copy(error = null) }
    }
}
