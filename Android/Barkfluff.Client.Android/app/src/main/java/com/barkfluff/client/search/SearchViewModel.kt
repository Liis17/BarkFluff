package com.barkfluff.client.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.domain.model.UserProfile
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch
import javax.inject.Inject

enum class SearchPhase {
    Idle,
    TooShort,
    Loading,
    Results,
    Empty,
    Error
}

data class SearchUser(
    val userData: UserProfile,
    val displayFullName: String,
    val displayUsername: String,
    val displayAvatarFileId: String?
)

data class SearchUiState(
    val query: String = "",
    val phase: SearchPhase = SearchPhase.Idle,
    val users: List<SearchUser> = emptyList()
)

private data class SearchRequest(
    val query: String,
    val useDebounce: Boolean,
    val id: Long
)

@HiltViewModel
class SearchViewModel @Inject constructor(
    private val searchUsersGateway: SearchUsersGateway
) : ViewModel() {

    private val _uiState = MutableStateFlow(SearchUiState())
    val uiState = _uiState.asStateFlow()

    /** A StateFlow keeps the latest request available even if the collector is still starting. */
    private val searchRequests = MutableStateFlow<SearchRequest?>(null)
    private var nextRequestId = 0L

    init {
        viewModelScope.launch {
            searchRequests.collectLatest { request ->
                request ?: return@collectLatest

                val query = request.query.trim()
                if (query.length < MIN_SEARCH_LENGTH) return@collectLatest
                if (request.useDebounce) delay(SEARCH_DEBOUNCE_MS)
                loadUsers(query)
            }
        }
    }

    fun onQueryChanged(query: String) {
        val normalizedQuery = query.trim()
        val phase = when {
            normalizedQuery.isEmpty() -> SearchPhase.Idle
            normalizedQuery.length < MIN_SEARCH_LENGTH -> SearchPhase.TooShort
            else -> SearchPhase.Loading
        }

        _uiState.value = SearchUiState(query = query, phase = phase)
        enqueueSearch(query, useDebounce = true)
    }

    fun submitQuery() {
        val query = _uiState.value.query.trim()
        if (query.length < MIN_SEARCH_LENGTH) {
            _uiState.value = _uiState.value.copy(
                phase = if (query.isEmpty()) SearchPhase.Idle else SearchPhase.TooShort,
                users = emptyList()
            )
            enqueueSearch(_uiState.value.query, useDebounce = true)
            return
        }

        _uiState.value = _uiState.value.copy(phase = SearchPhase.Loading, users = emptyList())
        enqueueSearch(query, useDebounce = false)
    }

    fun retry() {
        val query = _uiState.value.query.trim()
        if (query.length < MIN_SEARCH_LENGTH) {
            onQueryChanged(_uiState.value.query)
            return
        }

        _uiState.value = _uiState.value.copy(phase = SearchPhase.Loading, users = emptyList())
        enqueueSearch(query, useDebounce = false)
    }

    private fun enqueueSearch(query: String, useDebounce: Boolean) {
        searchRequests.value = SearchRequest(
            query = query,
            useDebounce = useDebounce,
            id = ++nextRequestId
        )
    }

    private suspend fun loadUsers(query: String) {
        val result = searchUsersGateway.search(query)

        // GrpcTransportFacade wraps CancellationException in Result.failure. The checks below make sure
        // an obsolete response can never replace the state for the newer query.
        if (!currentCoroutineContext().isActive) return
        if (_uiState.value.query.trim() != query) return

        result.fold(
            onSuccess = { users ->
                val displayItems = users.map(::toSearchUser)
                _uiState.value = _uiState.value.copy(
                    phase = if (displayItems.isEmpty()) SearchPhase.Empty else SearchPhase.Results,
                    users = displayItems
                )
            },
            onFailure = {
                _uiState.value = _uiState.value.copy(
                    phase = SearchPhase.Error,
                    users = emptyList()
                )
            }
        )
    }

    private fun toSearchUser(user: UserProfile): SearchUser {
        val displayName = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
        val displayUsername = user.username.takeIf { it.isNotBlank() }?.let { "@$it" }.orEmpty()
        val avatarFileId = user.profilePicturePreviewFileId
            .ifBlank { user.profilePictureFileId }
            .ifBlank { null }

        return SearchUser(
            userData = user,
            displayFullName = displayName,
            displayUsername = displayUsername,
            displayAvatarFileId = avatarFileId
        )
    }

    companion object {
        const val MIN_SEARCH_LENGTH = 3
        const val SEARCH_DEBOUNCE_MS = 300L
    }
}
