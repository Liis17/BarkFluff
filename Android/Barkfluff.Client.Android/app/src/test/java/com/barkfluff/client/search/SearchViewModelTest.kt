package com.barkfluff.client.search

import com.barkfluff.client.grpc.GrpcManager
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestDispatcher
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.Dispatchers
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class SearchViewModelTest {

    private lateinit var dispatcher: TestDispatcher

    @Before
    fun setUp() {
        dispatcher = StandardTestDispatcher()
        Dispatchers.setMain(dispatcher)
    }

    @After
    fun tearDown() {
        Dispatchers.resetMain()
    }

    @Test
    fun queryShorterThanMinimumDoesNotCallGateway() = runTest(dispatcher) {
        val gateway = FakeSearchUsersGateway()
        val viewModel = SearchViewModel(gateway)

        viewModel.onQueryChanged("ab")
        advanceUntilIdle()

        assertEquals(SearchPhase.TooShort, viewModel.uiState.value.phase)
        assertTrue(gateway.queries.isEmpty())
    }

    @Test
    fun queryIsDebouncedFor300Milliseconds() = runTest(dispatcher) {
        val gateway = FakeSearchUsersGateway()
        val viewModel = SearchViewModel(gateway)

        viewModel.onQueryChanged("alice")
        advanceTimeBy(SearchViewModel.SEARCH_DEBOUNCE_MS - 1)
        runCurrent()
        assertTrue(gateway.queries.isEmpty())

        advanceTimeBy(1)
        advanceUntilIdle()

        assertEquals(listOf("alice"), gateway.queries)
    }

    @Test
    fun newerQueryCancelsOlderRequestAndKeepsLatestResult() = runTest(dispatcher) {
        val gateway = CancellingSearchUsersGateway()
        val viewModel = SearchViewModel(gateway)

        viewModel.onQueryChanged("old")
        advanceTimeBy(SearchViewModel.SEARCH_DEBOUNCE_MS)
        runCurrent()
        gateway.oldRequestStarted.await()

        viewModel.onQueryChanged("new")
        advanceTimeBy(SearchViewModel.SEARCH_DEBOUNCE_MS)
        advanceUntilIdle()

        assertEquals(SearchPhase.Results, viewModel.uiState.value.phase)
        assertEquals("new", viewModel.uiState.value.query)
        assertEquals("New User", viewModel.uiState.value.users.single().displayFullName)
        assertTrue(gateway.oldRequestCancelled)
    }

    @Test
    fun successMapsDisplayFieldsAndAvatarFallback() = runTest(dispatcher) {
        val gateway = FakeSearchUsersGateway {
            Result.success(
                listOf(
                    user(
                        id = 7,
                        username = "alice",
                        firstName = "Alice",
                        lastName = "Smith",
                        previewFileId = "preview-id",
                        fullFileId = "full-id"
                    )
                )
            )
        }
        val viewModel = SearchViewModel(gateway)

        viewModel.onQueryChanged("alice")
        advanceUntilIdle()

        val result = viewModel.uiState.value
        assertEquals(SearchPhase.Results, result.phase)
        assertEquals("Alice Smith", result.users.single().displayFullName)
        assertEquals("@alice", result.users.single().displayUsername)
        assertEquals("preview-id", result.users.single().displayAvatarFileId)
    }

    @Test
    fun emptyResponseUsesEmptyPhase() = runTest(dispatcher) {
        val gateway = FakeSearchUsersGateway { Result.success(emptyList()) }
        val viewModel = SearchViewModel(gateway)

        viewModel.onQueryChanged("nobody")
        advanceUntilIdle()

        assertEquals(SearchPhase.Empty, viewModel.uiState.value.phase)
    }

    @Test
    fun errorUsesErrorPhaseAndRetryRunsImmediately() = runTest(dispatcher) {
        val attempts = AtomicInteger(0)
        val gateway = FakeSearchUsersGateway {
            if (attempts.getAndIncrement() == 0) {
                Result.failure(IllegalStateException("offline"))
            } else {
                Result.success(listOf(user(9, "bob", "Bob", "Jones")))
            }
        }
        val viewModel = SearchViewModel(gateway)

        viewModel.onQueryChanged("bob")
        advanceUntilIdle()
        assertEquals(SearchPhase.Error, viewModel.uiState.value.phase)

        viewModel.retry()
        advanceUntilIdle()

        assertEquals(SearchPhase.Results, viewModel.uiState.value.phase)
        assertEquals(2, gateway.queries.size)
    }

    private class FakeSearchUsersGateway(
        private val response: suspend (String) -> Result<List<GrpcManager.UserData>> = {
            Result.success(emptyList())
        }
    ) : SearchUsersGateway {
        val queries = mutableListOf<String>()

        override suspend fun search(query: String): Result<List<GrpcManager.UserData>> {
            queries += query
            return response(query)
        }
    }

    private class CancellingSearchUsersGateway : SearchUsersGateway {
        val queries = mutableListOf<String>()
        val oldRequestStarted = CompletableDeferred<Unit>()
        var oldRequestCancelled = false

        override suspend fun search(query: String): Result<List<GrpcManager.UserData>> {
            queries += query
            if (query == "old") {
                oldRequestStarted.complete(Unit)
                try {
                    CompletableDeferred<Unit>().await()
                } finally {
                    oldRequestCancelled = true
                }
            }
            return Result.success(listOf(user(2, "new", "New", "User")))
        }
    }

    companion object {
        private fun user(
            id: Long,
            username: String,
            firstName: String,
            lastName: String,
            previewFileId: String = "",
            fullFileId: String = ""
        ) = GrpcManager.UserData(
            userId = id,
            username = username,
            firstName = firstName,
            lastName = lastName,
            bio = "",
            profilePictureUrl = "",
            profilePicturePreviewUrl = "",
            profilePictureFileId = fullFileId,
            profilePicturePreviewFileId = previewFileId,
            registrationDate = 0L
        )
    }
}
