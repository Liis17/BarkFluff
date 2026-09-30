package com.barkfluff.client.search

import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.MessageSearchAuthor
import com.barkfluff.client.domain.model.MessageSearchCursor
import com.barkfluff.client.domain.model.MessageSearchHit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MessageSearchScreenTest {
    @get:Rule val compose = createComposeRule()
    private val context get() = InstrumentationRegistry.getInstrumentation().targetContext

    @Test fun resultOpensExactMessageAndOffersNextPage() {
        var clicked = 0L
        var paged = false
        val hit = MessageSearchHit(900, "chat", "Работа", true, "", 0,
            MessageSearchAuthor(userId = 7, name = "Анна"), 0, "План готов", emptySet())
        render(MessageSearchUiState(query = "план", phase = SearchPhase.Results, hits = listOf(hit),
            nextCursor = MessageSearchCursor(0, 0, 900)), onMessage = { clicked = it.messageId }, onPage = { paged = true })
        compose.onNodeWithText("Работа").assertIsDisplayed().assertHasClickAction().performClick()
        assertEquals(900L, clicked)
        compose.onNodeWithText(context.getString(R.string.search_load_more)).performClick()
        assertTrue(paged)
    }

    @Test fun activeAuthorFilterCanBeChangedAndReset() {
        var authorClicked = false
        var reset = false
        render(MessageSearchUiState(filters = MessageSearchFilters(author = MessageSearchAuthor(userId = 7, name = "Анна"))),
            onAuthor = { authorClicked = true }, onReset = { reset = true })
        compose.onNodeWithText("Анна").assertHasClickAction().performClick()
        compose.onNodeWithText(context.getString(R.string.search_reset_filters)).performClick()
        assertTrue(authorClicked)
        assertTrue(reset)
    }

    @Test fun unsupportedNodeShowsDedicatedState() {
        render(MessageSearchUiState(phase = SearchPhase.Error, isUnavailable = true))
        compose.onNodeWithText(context.getString(R.string.search_messages_unavailable)).assertIsDisplayed()
        compose.onNodeWithText(context.getString(R.string.search_nothing_found)).assertDoesNotExist()
    }

    private fun render(state: MessageSearchUiState, onMessage: (MessageSearchHit) -> Unit = {}, onPage: () -> Unit = {},
        onAuthor: () -> Unit = {}, onReset: () -> Unit = {}) {
        compose.setContent { BarkFluffSearchTheme {
            MessageSearchContent(state, onRetry = {}, onLoadMore = onPage, onAuthorFilter = onAuthor,
                onDateFilter = {}, onAttachmentFilter = {}, onClearFilters = onReset, onMessageClick = onMessage)
        } }
    }
}
