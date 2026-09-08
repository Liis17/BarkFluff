package com.barkfluff.client.search

import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.barkfluff.client.domain.model.UserProfile
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class SearchScreenTest {

    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun idleStateShowsCenteredSearchPrompt() {
        render(SearchUiState())

        composeRule.onNodeWithText("Кого ищем?").assertIsDisplayed()
        composeRule.onNodeWithText("Введите имя, фамилию или username (минимум 3 символа)")
            .assertIsDisplayed()
    }

    @Test
    fun tooShortStateShowsMinimumLengthHint() {
        render(SearchUiState(query = "ab", phase = SearchPhase.TooShort))

        composeRule.onNodeWithText("Введите минимум 3 символа").assertIsDisplayed()
    }

    @Test
    fun loadingStateShowsProgressLabel() {
        render(SearchUiState(query = "alice", phase = SearchPhase.Loading))

        composeRule.onNodeWithText("Ищем пользователей…").assertIsDisplayed()
    }

    @Test
    fun resultsStateShowsTonalUserItemAndClick() {
        var clickedUserId = 0L
        render(
            SearchUiState(
                query = "alice",
                phase = SearchPhase.Results,
                users = listOf(searchUser(42, "alice", "Alice", "Smith"))
            ),
            onUserClick = { clickedUserId = it.userData.userId }
        )

        composeRule.onNodeWithText("Alice Smith").assertIsDisplayed()
        composeRule.onNodeWithText("@alice").assertIsDisplayed()
        composeRule.onNodeWithContentDescription("Alice Smith, @alice")
            .assertHasClickAction()
            .performClick()
        assertEquals(42L, clickedUserId)
    }

    @Test
    fun emptyStateShowsNoResultsCopy() {
        render(SearchUiState(query = "nobody", phase = SearchPhase.Empty))

        composeRule.onNodeWithText("Ничего не найдено").assertIsDisplayed()
        composeRule.onNodeWithText("Попробуйте изменить поисковой запрос").assertIsDisplayed()
    }

    @Test
    fun errorStateShowsRetryAction() {
        var retryClicked = false
        render(
            SearchUiState(query = "alice", phase = SearchPhase.Error),
            onRetry = { retryClicked = true }
        )

        composeRule.onNodeWithText("Не удалось выполнить поиск").assertIsDisplayed()
        composeRule.onNodeWithText("Повторить").performClick()
        assertTrue(retryClicked)
    }

    @Test
    fun clearBackAndSearchSemanticsAreAvailable() {
        var cleared = false
        var backed = false
        var searched = ""
        render(
            SearchUiState(query = "alice", phase = SearchPhase.Loading),
            onQueryChanged = { searched = it },
            onClear = { cleared = true },
            onBack = { backed = true }
        )

        composeRule.onNode(hasSetTextAction()).assertExists().performTextInput("x")
        composeRule.onNodeWithContentDescription("Очистить поиск")
            .assertHasClickAction()
            .performClick()
        composeRule.onNodeWithContentDescription("Назад")
            .assertHasClickAction()
            .performClick()

        assertTrue(searched.isNotEmpty())
        assertTrue(cleared)
        assertTrue(backed)
    }

    private fun render(
        state: SearchUiState,
        onQueryChanged: (String) -> Unit = {},
        onSubmit: () -> Unit = {},
        onRetry: () -> Unit = {},
        onClear: () -> Unit = {},
        onBack: () -> Unit = {},
        onUserClick: (SearchUser) -> Unit = {}
    ) {
        composeRule.setContent {
            BarkFluffSearchTheme {
                SearchScreen(
                    isPrivateMode = false,
                    uiState = state,
                    isActionInProgress = false,
                    onQueryChanged = onQueryChanged,
                    onSubmit = onSubmit,
                    onRetry = onRetry,
                    onClear = onClear,
                    onBack = onBack,
                    onUserClick = onUserClick,
                    getAvatarUrl = { null }
                )
            }
        }
    }

    private fun searchUser(
        id: Long,
        username: String,
        firstName: String,
        lastName: String
    ) = SearchUser(
        userData = UserProfile(
            userId = id,
            username = username,
            firstName = firstName,
            lastName = lastName,
            bio = "",
            profilePictureUrl = "",
            profilePicturePreviewUrl = "",
            registrationDate = 0L
        ),
        displayFullName = "$firstName $lastName",
        displayUsername = "@$username",
        displayAvatarFileId = null
    )
}
