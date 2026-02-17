package com.barkfluff.android.ui.serverselection

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.android.data.model.ServerInfo
import com.barkfluff.android.repository.ServerRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class ServerSelectionUiState(
    val servers: List<ServerInfo> = emptyList(),
    val isLoadingList: Boolean = true,
    val isConnecting: Boolean = false,
    val error: String? = null,
    val navigateToLogin: Boolean = false,
)

@HiltViewModel
class ServerSelectionViewModel @Inject constructor(
    private val serverRepository: ServerRepository,
) : ViewModel() {

    private val _uiState = MutableStateFlow(ServerSelectionUiState())
    val uiState: StateFlow<ServerSelectionUiState> = _uiState.asStateFlow()

    init {
        loadServers()
    }

    private fun loadServers() {
        viewModelScope.launch {
            _uiState.update { it.copy(isLoadingList = true, error = null) }
            serverRepository.listServers()
                .onSuccess { servers ->
                    _uiState.update { it.copy(servers = servers, isLoadingList = false) }
                }
                .onFailure { error ->
                    _uiState.update {
                        it.copy(isLoadingList = false, error = "Не удалось загрузить серверы: ${error.message}")
                    }
                }
        }
    }

    fun connectToServer(host: String, port: Int) {
        if (_uiState.value.isConnecting) return
        viewModelScope.launch {
            _uiState.update { it.copy(isConnecting = true, error = null) }
            serverRepository.connectToServer(host, port)
                .onSuccess {
                    _uiState.update { it.copy(isConnecting = false, navigateToLogin = true) }
                }
                .onFailure { error ->
                    _uiState.update {
                        it.copy(isConnecting = false, error = "Ошибка подключения: ${error.message}")
                    }
                }
        }
    }

    fun onNavigatedToLogin() {
        _uiState.update { it.copy(navigateToLogin = false) }
    }

    fun clearError() {
        _uiState.update { it.copy(error = null) }
    }
}
