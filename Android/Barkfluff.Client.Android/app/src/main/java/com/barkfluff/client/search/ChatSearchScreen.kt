package com.barkfluff.client.search

import barkfluff.shared.Shared
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.MessageSearchHit

@Composable
fun ChatSearchScreen(
    state: ChatSearchUiState,
    messages: MessageSearchUiState,
    onQueryChanged: (String) -> Unit,
    onTabChanged: (SearchTab) -> Unit,
    onSubmit: () -> Unit,
    onMessageRetry: () -> Unit,
    onLoadMore: () -> Unit,
    onAuthorFilter: () -> Unit,
    onDateFilter: () -> Unit,
    onAttachmentFilter: () -> Unit,
    onClearFilters: () -> Unit,
    onRetry: () -> Unit,
    onBack: () -> Unit,
    onChatClick: (SearchChat) -> Unit,
    onMessageClick: (MessageSearchHit) -> Unit,
    getAvatarUrl: suspend (String) -> String?,
) {
    val focusRequester = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(Unit) {
        withFrameNanos { }
        focusRequester.requestFocus()
        keyboard?.show()
    }
    Surface(Modifier.fillMaxSize().imePadding(), color = searchBackgroundColor()) {
        Column(Modifier.fillMaxSize()) {
            SearchHeader(
                query = messages.query,
                placeholder = stringResource(if (messages.tab == SearchTab.Chats) R.string.search_chats_hint else R.string.search_messages_hint),
                focusRequester = focusRequester,
                onQueryChanged = onQueryChanged,
                onSubmit = { keyboard?.hide(); onSubmit() },
                onClear = { onQueryChanged("") },
                onBack = onBack,
            )
            SearchTabs(
                selected = messages.tab,
                labels = SearchTab.entries.map { stringResource(if (it == SearchTab.Chats) R.string.search_tab_chats else R.string.search_tab_messages) },
                onSelect = onTabChanged,
            )
            if (messages.tab == SearchTab.Messages) {
                MessageSearchContent(messages, onMessageRetry, onLoadMore,
                    onAuthorFilter = { keyboard?.hide(); onAuthorFilter() },
                    onDateFilter = { keyboard?.hide(); onDateFilter() },
                    onAttachmentFilter = { keyboard?.hide(); onAttachmentFilter() },
                    onClearFilters = onClearFilters, onMessageClick = onMessageClick,
                    modifier = Modifier.weight(1f).navigationBarsPadding())
            }
            if (messages.tab == SearchTab.Chats && (state.isRefreshing || state.isPartial)) {
                Row(
                    Modifier.fillMaxWidth().padding(horizontal = 20.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    if (state.isRefreshing) MorphLoadingIndicator(size = 28.dp)
                    Text(
                        stringResource(if (state.isRefreshing) R.string.search_chats_updating else R.string.search_chats_cached),
                        modifier = Modifier.weight(1f),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    if (!state.isRefreshing) TextButton(onClick = onRetry) { Text(stringResource(R.string.search_retry)) }
                }
            }
            if (messages.tab == SearchTab.Chats) Box(Modifier.weight(1f).fillMaxWidth().navigationBarsPadding()) {
                SearchPhaseTransition(state.phase) { phase ->
                    when (phase) {
                        SearchPhase.Idle, SearchPhase.TooShort -> SearchMessageState(
                            R.drawable.ic_search, stringResource(R.string.search_chats_prompt), stringResource(R.string.search_chats_description),
                        )
                        SearchPhase.Loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            MorphLoadingIndicator(size = 56.dp)
                        }
                        SearchPhase.Empty -> SearchMessageState(
                            R.drawable.ic_search, stringResource(R.string.search_nothing_found), stringResource(R.string.search_try_different),
                        )
                        SearchPhase.Error -> SearchErrorState(onRetry)
                        SearchPhase.Results -> LazyColumn(
                            modifier = Modifier.fillMaxSize(),
                            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                            verticalArrangement = Arrangement.spacedBy(2.dp),
                        ) {
                            itemsIndexed(state.chats, key = { _, chat -> chat.chat.id }) { index, chat ->
                                ChatSearchResult(chat, index, state.chats.size, { onChatClick(chat) }, getAvatarUrl, Modifier.animateItem())
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ChatSearchResult(
    chat: SearchChat,
    index: Int,
    count: Int,
    onClick: () -> Unit,
    getAvatarUrl: suspend (String) -> String?,
    modifier: Modifier = Modifier,
) {
    val title = chat.title.ifBlank { stringResource(R.string.chat_title_default) }
    val (kind, kindIcon) = when (chat.chat.chatType) {
        Shared.ChatType.CHAT_TYPE_PRIVATE -> R.string.search_chat_private to R.drawable.ic_lock
        Shared.ChatType.CHAT_TYPE_SECRET -> R.string.search_chat_secret to R.drawable.ic_lock
        else -> if (chat.chat.isGroupChat) R.string.search_chat_group to R.drawable.ic_group
        else R.string.search_chat_direct to R.drawable.ic_person
    }
    SearchGroupedItem(index = index, count = count, onClick = onClick, modifier = modifier) {
        ListItem(
            headlineContent = {
                Text(title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            },
            supportingContent = {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Icon(painterResource(kindIcon), contentDescription = null, modifier = Modifier.size(14.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(stringResource(kind), color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            },
            leadingContent = {
                Box(Modifier.clearAndSetSemantics { }) {
                    SearchAvatar(chat.avatarFileId, title, chat.otherUserId, getAvatarUrl)
                }
            },
            colors = ListItemDefaults.colors(containerColor = androidx.compose.ui.graphics.Color.Transparent),
        )
    }
}
