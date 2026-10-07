package com.barkfluff.client.search

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.shared.Shared
import dagger.hilt.android.lifecycle.HiltViewModel
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

data class ChatSearchUiState(
    val query: String = "",
    val phase: SearchPhase = SearchPhase.Idle,
    val chats: List<SearchChat> = emptyList(),
    val isRefreshing: Boolean = true,
    val isPartial: Boolean = true,
)

@HiltViewModel
class ChatSearchViewModel @Inject constructor(
    private val gateway: SearchChatsGateway,
    private val savedStateHandle: SavedStateHandle,
) : ViewModel() {
    private val _uiState = MutableStateFlow(ChatSearchUiState(query = savedStateHandle["query"] ?: ""))
    val uiState = _uiState.asStateFlow()
    private var catalog = emptyList<SearchChat>()
    private var loadJob: Job? = null
    private var loading = true
    private var complete = false
    private var failed = false

    init { retry() }

    fun onQueryChanged(query: String) {
        savedStateHandle["query"] = query
        _uiState.value = _uiState.value.copy(query = query)
        publish()
    }

    fun retry() {
        loadJob?.cancel()
        loadJob = viewModelScope.launch {
            loading = true
            complete = false
            failed = false
            publish()
            try {
                val cached = runCatching { gateway.cached() }.getOrDefault(emptyList())
                currentCoroutineContext().ensureActive()
                if (catalog.isEmpty()) catalog = valid(cached)
                val local = valid(cached).filter { it.chat.chatType == Shared.ChatType.CHAT_TYPE_SECRET }
                publish()
                val remote = linkedMapOf<String, SearchChat>()
                var offset = 0
                var totalCount: Int
                do {
                    val page = gateway.page(offset, PAGE_SIZE).getOrThrow()
                    currentCoroutineContext().ensureActive()
                    totalCount = page.totalCount
                    if (page.chats.isEmpty() && offset < totalCount) {
                        throw IllegalStateException("Incomplete chat page")
                    }
                    offset += page.chats.size
                    valid(page.chats).forEach { remote[it.chat.id] = it }
                    catalog = (catalog + local + remote.values).associateBy { it.chat.id }.values.toList()
                    publish()
                } while (offset < totalCount)
                catalog = local + remote.values
                complete = true
                loading = false
                publish()
                runCatching { gateway.save(remote.values.toList(), totalCount) }
                currentCoroutineContext().ensureActive()
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                loading = false
                failed = true
                publish()
            }
        }
    }

    private fun publish() {
        val query = _uiState.value.query.trim()
        val matches = catalog.filter { it.title.contains(query, ignoreCase = true) }
            .sortedWith(compareByDescending<SearchChat> { it.chat.lastActivityAt }.thenBy { it.chat.id })
        _uiState.value = _uiState.value.copy(
            chats = if (query.isEmpty()) emptyList() else matches,
            phase = when {
                query.isEmpty() -> SearchPhase.Idle
                matches.isNotEmpty() -> SearchPhase.Results
                loading -> SearchPhase.Loading
                failed -> SearchPhase.Error
                else -> SearchPhase.Empty
            },
            isRefreshing = loading,
            isPartial = !complete,
        )
    }

    private fun valid(chats: List<SearchChat>): List<SearchChat> = chats.filter {
        runCatching { UUID.fromString(it.chat.id) }.isSuccess
    }

    companion object { const val PAGE_SIZE = 50 }
}
