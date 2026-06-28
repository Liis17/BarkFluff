package com.barkfluff.clientv2.ui.screens.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SearchUiState(
    val query: String = "",
    val loading: Boolean = false,
    val results: List<GrpcManager.UserData> = emptyList(),
    val error: String? = null,
)

private const val MIN_QUERY_LENGTH = 3
private const val DEBOUNCE_MS = 300L

class SearchViewModel(
    private val grpcManager: GrpcManager
) : ViewModel() {

    private val _ui = MutableStateFlow(SearchUiState())
    val ui: StateFlow<SearchUiState> = _ui.asStateFlow()

    private var searchJob: Job? = null

    fun onQueryChange(query: String) {
        _ui.update { it.copy(query = query) }
        searchJob?.cancel()

        val trimmed = query.trim()
        if (trimmed.length < MIN_QUERY_LENGTH) {
            _ui.update { it.copy(results = emptyList(), loading = false, error = null) }
            return
        }

        searchJob = viewModelScope.launch {
            delay(DEBOUNCE_MS)
            _ui.update { it.copy(loading = true, error = null) }
            grpcManager.searchUsers(trimmed)
                .onSuccess { list -> _ui.update { it.copy(loading = false, results = list) } }
                .onFailure { e -> _ui.update { it.copy(loading = false, error = e.message ?: "Ошибка поиска") } }
        }
    }

    fun openChat(userId: Long, onResolved: (String) -> Unit) {
        viewModelScope.launch {
            grpcManager.getPersonChatId(userId)
                .onSuccess { chatId -> if (chatId.isNotBlank()) onResolved(chatId) }
                .onFailure { e -> _ui.update { it.copy(error = e.message ?: "Не удалось открыть чат") } }
        }
    }
}
