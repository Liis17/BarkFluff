package com.barkfluff.client.search

import androidx.lifecycle.SavedStateHandle
import com.barkfluff.client.domain.model.ChatSummary
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class ChatSearchViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)
    @After fun tearDown() = Dispatchers.resetMain()

    @Test fun findsExistingDialogBeyondFirstThreePages() = runTest(dispatcher) {
        val chats = (0 until 160).map { index -> chat(index, if (index == 159) "Четвёртый" else "Диалог $index") }
        val gateway = FakeGateway(chats)
        val vm = ChatSearchViewModel(gateway, SavedStateHandle())

        vm.onQueryChanged("четвёртый")
        advanceUntilIdle()

        assertEquals(listOf("00000000-0000-0000-0000-000000000159"), vm.uiState.value.chats.map { it.chat.id })
        assertEquals(SearchPhase.Results, vm.uiState.value.phase)
        assertFalse(vm.uiState.value.isPartial)
    }

    @Test fun offlineSearchShowsCachedMatchesAndMarksThemPartial() = runTest(dispatcher) {
        val gateway = FakeGateway(emptyList(), cached = listOf(chat(1, "Работа")), offline = true)
        val vm = ChatSearchViewModel(gateway, SavedStateHandle())

        vm.onQueryChanged("р")
        advanceUntilIdle()

        assertEquals("Работа", vm.uiState.value.chats.single().title)
        assertEquals(SearchPhase.Results, vm.uiState.value.phase)
        assertTrue(vm.uiState.value.isPartial)
        assertFalse(vm.uiState.value.isRefreshing)
    }

    @Test fun restoresQueryAndRemovesStaleCachedChatAfterFullRefresh() = runTest(dispatcher) {
        val gateway = FakeGateway(listOf(chat(2, "Работа новая")), cached = listOf(chat(1, "Работа старая")))
        val vm = ChatSearchViewModel(gateway, SavedStateHandle(mapOf("query" to "РАБОТА")))
        advanceUntilIdle()

        assertEquals("РАБОТА", vm.uiState.value.query)
        assertEquals(listOf("Работа новая"), vm.uiState.value.chats.map { it.title })
        assertFalse(vm.uiState.value.isPartial)
    }

    private class FakeGateway(
        private val chats: List<SearchChat>,
        private val cached: List<SearchChat> = emptyList(),
        private val offline: Boolean = false,
    ) : SearchChatsGateway {
        override suspend fun cached(): List<SearchChat> = cached
        override suspend fun page(offset: Int, size: Int): Result<SearchChatPage> =
            if (offline) Result.failure(IllegalStateException("offline")) else
                Result.success(SearchChatPage(chats.drop(offset).take(size), chats.size))
        override suspend fun save(chats: List<SearchChat>, totalCount: Int) = Unit
    }

    companion object {
        private fun chat(index: Int, title: String) = SearchChat(ChatSummary(
            id = "00000000-0000-0000-0000-${index.toString().padStart(12, '0')}", title = title, picture = "", isGroupChat = false,
            lastMessage = null, memberIds = listOf(1L, 2L), countUnread = 0L, firstUnreadMessageId = 0L,
        ))
    }
}
