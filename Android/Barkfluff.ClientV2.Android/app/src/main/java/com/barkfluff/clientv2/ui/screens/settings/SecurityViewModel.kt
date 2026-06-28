package com.barkfluff.clientv2.ui.screens.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.identity.IdentityApiOuterClass.OtpTypeId
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SecurityUiState(
    val loading: Boolean = true,
    val authenticatorEnabled: Boolean = false,
    val emailEnabled: Boolean = false,
)

class SecurityViewModel(private val grpcManager: GrpcManager) : ViewModel() {

    private val _ui = MutableStateFlow(SecurityUiState())
    val ui: StateFlow<SecurityUiState> = _ui.asStateFlow()

    init { load() }

    fun load() {
        viewModelScope.launch {
            grpcManager.listOtpVerification()
                .onSuccess { s ->
                    _ui.update {
                        it.copy(
                            loading = false,
                            authenticatorEnabled = s.authenticatorEnabled,
                            emailEnabled = s.emailEnabled
                        )
                    }
                }
                .onFailure { _ui.update { it.copy(loading = false) } }
        }
    }

    fun changePassword(oldPassword: String, newPassword: String, onResult: (Result<Unit>) -> Unit) {
        viewModelScope.launch { onResult(grpcManager.changePassword(oldPassword, newPassword)) }
    }

    fun beginAuthenticatorSetup(onResult: (Result<GrpcManager.OtpSetupResult>) -> Unit) {
        viewModelScope.launch { onResult(grpcManager.getOtpSetup()) }
    }

    fun confirmAuthenticator(code: String, onResult: (Result<Unit>) -> Unit) {
        viewModelScope.launch {
            val result = grpcManager.confirmOtpSetup(code)
            if (result.isSuccess) load()
            onResult(result)
        }
    }

    fun enableEmail(onResult: (Result<Unit>) -> Unit) {
        viewModelScope.launch {
            val result = grpcManager.enableOtpEmail()
            if (result.isSuccess) load()
            onResult(result)
        }
    }

    fun disable(type: OtpTypeId, code: String, onResult: (Result<Unit>) -> Unit) {
        viewModelScope.launch {
            val result = grpcManager.disableOtpVerification(type, code)
            if (result.isSuccess) load()
            onResult(result)
        }
    }
}
