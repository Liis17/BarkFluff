package com.barkfluff.clientv2.ui.screens.chat

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ForwardChatsUiState(
    val loading: Boolean = true,
    val chats: List<GrpcManager.ChatData> = emptyList(),
)

/** Список чатов для выбора цели пересылки. */
class ForwardChatsViewModel(grpcManager: GrpcManager) : ViewModel() {

    private val _ui = MutableStateFlow(ForwardChatsUiState())
    val ui: StateFlow<ForwardChatsUiState> = _ui.asStateFlow()

    init {
        viewModelScope.launch {
            grpcManager.getChats()
                .onSuccess { list -> _ui.update { it.copy(loading = false, chats = list) } }
                .onFailure { _ui.update { it.copy(loading = false) } }
        }
    }
}
