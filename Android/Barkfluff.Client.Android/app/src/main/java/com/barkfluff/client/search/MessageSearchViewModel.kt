package com.barkfluff.client.search

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.shared.Shared
import com.barkfluff.client.domain.gateway.MessageSearchGateway
import com.barkfluff.client.domain.model.MessageSearchAuthor
import com.barkfluff.client.domain.model.MessageSearchCursor
import com.barkfluff.client.domain.model.MessageSearchHit
import com.barkfluff.client.domain.model.MessageSearchQuery
import com.barkfluff.client.domain.model.MessageSearchUnavailableException
import com.barkfluff.client.domain.model.SearchAttachmentPresence
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.ZoneId
import javax.inject.Inject

enum class SearchTab { Chats, Messages }

data class MessageSearchFilters(
    val author: MessageSearchAuthor? = null,
    val fromEpochDay: Long? = null,
    val throughEpochDay: Long? = null,
    val attachmentPresence: SearchAttachmentPresence = SearchAttachmentPresence.Any,
    val attachmentTypes: Set<Shared.MessageAttachmentType> = emptySet(),
) {
    val isActive: Boolean get() = author != null || fromEpochDay != null || throughEpochDay != null ||
        attachmentPresence != SearchAttachmentPresence.Any || attachmentTypes.isNotEmpty()

    fun query(text: String, zone: ZoneId = ZoneId.systemDefault()) = MessageSearchQuery(
        text = text.trim(), author = author,
        sentFromSeconds = fromEpochDay?.let { LocalDate.ofEpochDay(it).atStartOfDay(zone).toEpochSecond() },
        sentBeforeSeconds = throughEpochDay?.let { LocalDate.ofEpochDay(it).plusDays(1).atStartOfDay(zone).toEpochSecond() },
        attachmentPresence = attachmentPresence, attachmentTypes = attachmentTypes,
    )
}

data class MessageSearchUiState(
    val query: String = "",
    val tab: SearchTab = SearchTab.Chats,
    val filters: MessageSearchFilters = MessageSearchFilters(),
    val phase: SearchPhase = SearchPhase.Idle,
    val hits: List<MessageSearchHit> = emptyList(),
    val nextCursor: MessageSearchCursor? = null,
    val isLoadingMore: Boolean = false,
    val moreFailed: Boolean = false,
    val isUnavailable: Boolean = false,
)

@HiltViewModel
class MessageSearchViewModel @Inject constructor(
    private val gateway: MessageSearchGateway,
    private val savedState: SavedStateHandle,
) : ViewModel() {
    private val _uiState = MutableStateFlow(MessageSearchUiState(
        query = savedState["message_query"] ?: "",
        tab = SearchTab.entries.firstOrNull { it.name == savedState.get<String>("search_tab") } ?: SearchTab.Chats,
        filters = MessageSearchFilters(
            author = savedState.get<String>("author_key")?.let {
                MessageSearchAuthor(savedState["author_id"] ?: 0L, savedState["author_uuid"] ?: "", savedState["author_name"] ?: "")
            },
            fromEpochDay = savedState["date_from"], throughEpochDay = savedState["date_through"],
            attachmentPresence = SearchAttachmentPresence.entries.firstOrNull { it.name == savedState.get<String>("attachment_presence") }
                ?: SearchAttachmentPresence.Any,
            attachmentTypes = savedState.get<IntArray>("attachment_types")?.map { Shared.MessageAttachmentType.forNumber(it) }?.filterNotNull()?.toSet() ?: emptySet(),
        ),
    ))
    val uiState = _uiState.asStateFlow()
    private var searchJob: Job? = null
    private var pageJob: Job? = null
    private var revision = 0L

    init { search() }

    fun onTabChanged(tab: SearchTab) {
        if (_uiState.value.tab == tab) return
        savedState["search_tab"] = tab.name
        _uiState.value = _uiState.value.copy(tab = tab)
        search()
    }

    fun onQueryChanged(query: String) {
        val limited = query.take(256)
        if (_uiState.value.query == limited) return
        savedState["message_query"] = limited
        _uiState.value = _uiState.value.copy(query = limited)
        search(debounce = true)
    }

    fun setAuthor(author: MessageSearchAuthor?) = setFilters(_uiState.value.filters.copy(author = author))
    fun setDates(fromEpochDay: Long?, throughEpochDay: Long?) =
        setFilters(_uiState.value.filters.copy(fromEpochDay = fromEpochDay, throughEpochDay = throughEpochDay))

    fun setAttachments(presence: SearchAttachmentPresence, types: Set<Shared.MessageAttachmentType>) =
        setFilters(_uiState.value.filters.copy(
            attachmentPresence = if (types.isNotEmpty() && presence != SearchAttachmentPresence.Without) SearchAttachmentPresence.With else presence,
            attachmentTypes = if (presence == SearchAttachmentPresence.Without) emptySet() else types,
        ))

    fun clearFilters() = setFilters(MessageSearchFilters())
    fun submitQuery() = search()
    fun retry() = search()

    private fun setFilters(filters: MessageSearchFilters) {
        if (_uiState.value.filters == filters) return
        savedState["author_key"] = filters.author?.key
        savedState["author_id"] = filters.author?.userId
        savedState["author_uuid"] = filters.author?.userUuid
        savedState["author_name"] = filters.author?.name
        savedState["date_from"] = filters.fromEpochDay
        savedState["date_through"] = filters.throughEpochDay
        savedState["attachment_presence"] = filters.attachmentPresence.name
        savedState["attachment_types"] = filters.attachmentTypes.map { it.number }.toIntArray()
        _uiState.value = _uiState.value.copy(filters = filters)
        search()
    }

    private fun search(debounce: Boolean = false) {
        searchJob?.cancel()
        pageJob?.cancel()
        val requestRevision = ++revision
        val state = _uiState.value
        val active = state.tab == SearchTab.Messages && (state.query.isNotBlank() || state.filters.isActive)
        _uiState.value = state.copy(
            phase = if (active) SearchPhase.Loading else SearchPhase.Idle,
            hits = emptyList(), nextCursor = null, isLoadingMore = false, moreFailed = false, isUnavailable = false,
        )
        if (!active) return
        val query = state.filters.query(state.query)
        searchJob = viewModelScope.launch {
            if (debounce) delay(300)
            val result = gateway.search(query)
            if (revision != requestRevision) return@launch
            result.fold(
                onSuccess = { page ->
                    _uiState.value = _uiState.value.copy(
                        hits = page.hits.distinctBy { it.messageId }, nextCursor = page.nextCursor,
                        phase = if (page.hits.isEmpty()) SearchPhase.Empty else SearchPhase.Results,
                    )
                },
                onFailure = { _uiState.value = _uiState.value.copy(phase = SearchPhase.Error, isUnavailable = it is MessageSearchUnavailableException) },
            )
        }
    }

    fun loadMore() {
        val state = _uiState.value
        val cursor = state.nextCursor ?: return
        if (state.isLoadingMore || state.phase != SearchPhase.Results) return
        val requestRevision = revision
        _uiState.value = state.copy(isLoadingMore = true, moreFailed = false)
        pageJob = viewModelScope.launch {
            val result = gateway.search(state.filters.query(state.query).copy(cursor = cursor))
            if (revision != requestRevision) return@launch
            result.fold(
                onSuccess = { page -> _uiState.value = _uiState.value.copy(
                    hits = (state.hits + page.hits).distinctBy { it.messageId }, nextCursor = page.nextCursor, isLoadingMore = false,
                ) },
                onFailure = { _uiState.value = _uiState.value.copy(isLoadingMore = false, moreFailed = true) },
            )
        }
    }
}
