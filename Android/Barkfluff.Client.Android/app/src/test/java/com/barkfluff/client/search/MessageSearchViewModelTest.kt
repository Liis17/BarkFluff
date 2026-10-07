package com.barkfluff.client.search

import androidx.lifecycle.SavedStateHandle
import com.barkfluff.client.domain.gateway.MessageSearchGateway
import com.barkfluff.client.domain.model.MessageSearchAuthor
import com.barkfluff.client.domain.model.MessageSearchHit
import com.barkfluff.client.domain.model.MessageSearchPage
import com.barkfluff.client.domain.model.MessageSearchQuery
import com.barkfluff.client.domain.model.MessageSearchCursor
import com.barkfluff.client.domain.model.MessageSearchUnavailableException
import com.barkfluff.client.domain.model.SearchAttachmentPresence
import barkfluff.shared.Shared
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.LocalDate
import java.time.ZoneId

@OptIn(ExperimentalCoroutinesApi::class)
class MessageSearchViewModelTest {
    private val dispatcher = StandardTestDispatcher()
    @Before fun setUp() = Dispatchers.setMain(dispatcher)
    @After fun tearDown() = Dispatchers.resetMain()

    @Test fun authorFilterSearchesWithoutTextOnlyOnMessagesTab() = runTest(dispatcher) {
        val gateway = FakeGateway()
        val vm = MessageSearchViewModel(gateway, SavedStateHandle())
        vm.setAuthor(MessageSearchAuthor(userId = 7, name = "Анна"))
        advanceUntilIdle()
        assertEquals(0, gateway.requests.size)

        vm.onTabChanged(SearchTab.Messages)
        advanceUntilIdle()
        assertEquals(7L, gateway.requests.single().author?.userId)
        assertEquals(SearchPhase.Results, vm.uiState.value.phase)
    }

    @Test fun typingDebouncesAndOnlySearchesLatestText() = runTest(dispatcher) {
        val gateway = FakeGateway()
        val vm = MessageSearchViewModel(gateway, SavedStateHandle())
        vm.onTabChanged(SearchTab.Messages)
        vm.onQueryChanged("старый")
        advanceTimeBy(200)
        vm.onQueryChanged("новый")
        advanceUntilIdle()
        assertEquals(listOf("новый"), gateway.requests.map { it.text })
    }

    @Test fun appendsDistinctResultsAndResetsCursorWhenFiltersChange() = runTest(dispatcher) {
        val cursor = MessageSearchCursor(123, 123456789, 1)
        val gateway = FakeGateway { query -> Result.success(MessageSearchPage(
            if (query.cursor == null) listOf(hit(1)) else listOf(hit(1), hit(2)),
            if (query.cursor == null) cursor else null,
        )) }
        val vm = MessageSearchViewModel(gateway, SavedStateHandle())
        vm.onTabChanged(SearchTab.Messages)
        vm.onQueryChanged("отчёт")
        advanceUntilIdle()
        vm.loadMore()
        advanceUntilIdle()
        assertEquals(cursor, gateway.requests.last().cursor)
        assertEquals(listOf(1L, 2L), vm.uiState.value.hits.map { it.messageId })

        vm.setAuthor(MessageSearchAuthor(userId = 7))
        advanceUntilIdle()
        assertNull(gateway.requests.last().cursor)
        assertEquals(listOf(1L), vm.uiState.value.hits.map { it.messageId })
    }

    @Test fun latePageCannotOverwriteNewQueryResults() = runTest(dispatcher) {
        val latePage = CompletableDeferred<Result<MessageSearchPage>>()
        val gateway = FakeGateway { query ->
            when {
                query.cursor != null -> withContext(NonCancellable) { latePage.await() }
                query.text == "новый" -> Result.success(MessageSearchPage(listOf(hit(70)), null))
                else -> Result.success(MessageSearchPage(listOf(hit(1)), MessageSearchCursor(1, 0, 1)))
            }
        }
        val vm = MessageSearchViewModel(gateway, SavedStateHandle())
        vm.onTabChanged(SearchTab.Messages)
        vm.onQueryChanged("старый")
        advanceUntilIdle()
        vm.loadMore()
        runCurrent()
        vm.onQueryChanged("новый")
        advanceTimeBy(301)
        runCurrent()
        latePage.complete(Result.success(MessageSearchPage(listOf(hit(2)), null)))
        advanceUntilIdle()
        assertEquals(listOf(70L), vm.uiState.value.hits.map { it.messageId })
    }

    @Test fun restoresRemoteAuthorDatesAndAttachmentFilters() = runTest(dispatcher) {
        val saved = SavedStateHandle()
        val original = MessageSearchViewModel(FakeGateway(), saved)
        original.setAuthor(MessageSearchAuthor(userUuid = "9f78b089-6015-45d6-bdaf-ef405d43e7b7", name = "Олег"))
        original.setDates(20000, 20002)
        original.setAttachments(SearchAttachmentPresence.With, setOf(Shared.MessageAttachmentType.IMAGE))
        original.onTabChanged(SearchTab.Messages)
        advanceUntilIdle()
        val restored = MessageSearchViewModel(FakeGateway(), SavedStateHandle(saved.keys().associateWith { saved.get<Any?>(it) }))
        advanceUntilIdle()
        assertEquals(original.uiState.value.filters, restored.uiState.value.filters)
        assertEquals(SearchTab.Messages, restored.uiState.value.tab)
        assertEquals(SearchPhase.Results, restored.uiState.value.phase)
        restored.setAttachments(SearchAttachmentPresence.Without, restored.uiState.value.filters.attachmentTypes)
        assertTrue(restored.uiState.value.filters.attachmentTypes.isEmpty())
    }

    @Test fun unsupportedNodeShowsErrorInsteadOfEmptyResults() = runTest(dispatcher) {
        val vm = MessageSearchViewModel(FakeGateway { Result.failure(MessageSearchUnavailableException()) }, SavedStateHandle())
        vm.onTabChanged(SearchTab.Messages)
        vm.onQueryChanged("текст")
        advanceUntilIdle()
        assertEquals(SearchPhase.Error, vm.uiState.value.phase)
        assertTrue(vm.uiState.value.isUnavailable)
    }

    @Test fun selectedDatesCoverWholeLocalDaysAcrossDst() {
        val zone = ZoneId.of("Europe/Berlin")
        val spring = LocalDate.of(2026, 3, 29).toEpochDay()
        val autumn = LocalDate.of(2026, 10, 25).toEpochDay()
        fun duration(day: Long): Long {
            val query = MessageSearchFilters(fromEpochDay = day, throughEpochDay = day).query("", zone)
            return query.sentBeforeSeconds!! - query.sentFromSeconds!!
        }
        assertEquals(23 * 3600L, duration(spring))
        assertEquals(25 * 3600L, duration(autumn))
    }

    private class FakeGateway(private val handler: suspend (MessageSearchQuery) -> Result<MessageSearchPage> = {
        Result.success(MessageSearchPage(listOf(hit(1)), null))
    }) : MessageSearchGateway {
        val requests = mutableListOf<MessageSearchQuery>()
        override suspend fun search(query: MessageSearchQuery): Result<MessageSearchPage> {
            requests += query
            return handler(query)
        }
    }

    companion object {
        private fun hit(id: Long) = MessageSearchHit(id, "chat", "Работа", true, "", 0,
            MessageSearchAuthor(userId = 7, name = "Анна"), 0, "Отчёт", emptySet())
    }
}
