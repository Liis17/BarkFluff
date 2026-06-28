package com.barkfluff.clientv2.ui.screens.chats

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ChatsUiState(
    val loading: Boolean = true,
    val chats: List<GrpcManager.ChatData> = emptyList(),
    val error: String? = null,
)

class ChatsViewModel(
    private val grpcManager: GrpcManager,
    private val realtimeService: RealtimeService
) : ViewModel() {

    private val _ui = MutableStateFlow(ChatsUiState())
    val ui: StateFlow<ChatsUiState> = _ui.asStateFlow()

    init {
        // Запускаем realtime-стримы (идемпотентно) и подписываемся на события для обновления списка.
        realtimeService.resume()
        load()
        observeRealtime()
    }

    fun load() {
        viewModelScope.launch {
            _ui.update { it.copy(loading = it.chats.isEmpty(), error = null) }
            grpcManager.getChats()
                .onSuccess { list -> _ui.update { it.copy(loading = false, chats = list) } }
                .onFailure { e -> _ui.update { it.copy(loading = false, error = e.message ?: "Не удалось загрузить чаты") } }
        }
    }

    private fun observeRealtime() {
        viewModelScope.launch { realtimeService.newMessages.collect { load() } }
        viewModelScope.launch { realtimeService.messagesRead.collect { load() } }
    }
}
