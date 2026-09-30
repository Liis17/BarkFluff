package com.barkfluff.client.search

import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import barkfluff.shared.Shared
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.MessageSearchHit
import com.barkfluff.client.domain.model.SearchAttachmentPresence
import java.text.DateFormat
import java.util.Date
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

val searchAttachmentTypes = listOf(
    Shared.MessageAttachmentType.IMAGE to R.string.search_attachment_image,
    Shared.MessageAttachmentType.VIDEO to R.string.search_attachment_video,
    Shared.MessageAttachmentType.GIF to R.string.search_attachment_gif,
    Shared.MessageAttachmentType.DOCUMENT to R.string.search_attachment_document,
    Shared.MessageAttachmentType.AUDIO to R.string.search_attachment_audio,
    Shared.MessageAttachmentType.VOICE to R.string.search_attachment_voice,
    Shared.MessageAttachmentType.STICKER to R.string.search_attachment_sticker,
)

@Composable
fun MessageSearchContent(
    state: MessageSearchUiState,
    onRetry: () -> Unit,
    onLoadMore: () -> Unit,
    onAuthorFilter: () -> Unit,
    onDateFilter: () -> Unit,
    onAttachmentFilter: () -> Unit,
    onClearFilters: () -> Unit,
    onMessageClick: (MessageSearchHit) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier.fillMaxWidth()) {
        val locale = LocalConfiguration.current.locales[0]
        val dates = remember(state.filters.fromEpochDay, state.filters.throughEpochDay, locale) {
            val formatter = DateTimeFormatter.ofLocalizedDate(FormatStyle.SHORT).withLocale(locale)
            listOf(state.filters.fromEpochDay, state.filters.throughEpochDay).map { it?.let { day -> formatter.format(LocalDate.ofEpochDay(day)) } ?: "…" }
        }
        Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(selected = state.filters.author != null, onClick = onAuthorFilter, modifier = Modifier.heightIn(min = 48.dp),
                label = { Text(state.filters.author?.name?.ifBlank { null } ?: stringResource(R.string.search_filter_author)) })
            FilterChip(selected = state.filters.fromEpochDay != null || state.filters.throughEpochDay != null,
                onClick = onDateFilter, modifier = Modifier.heightIn(min = 48.dp), label = {
                    Text(if (state.filters.fromEpochDay != null || state.filters.throughEpochDay != null) stringResource(R.string.search_date_range, dates[0], dates[1])
                        else stringResource(R.string.search_filter_date))
                })
            FilterChip(selected = state.filters.attachmentPresence != SearchAttachmentPresence.Any || state.filters.attachmentTypes.isNotEmpty(),
                onClick = onAttachmentFilter, modifier = Modifier.heightIn(min = 48.dp), label = { Text(stringResource(R.string.search_filter_attachments)) })
            if (state.filters.isActive) TextButton(onClick = onClearFilters) { Text(stringResource(R.string.search_reset_filters)) }
        }
        Box(Modifier.fillMaxSize()) {
            when (state.phase) {
                SearchPhase.Idle, SearchPhase.TooShort -> SearchMessageState(R.drawable.ic_search,
                    stringResource(R.string.search_messages_prompt), stringResource(R.string.search_messages_description))
                SearchPhase.Loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
                SearchPhase.Error -> if (state.isUnavailable) SearchMessageState(R.drawable.ic_search,
                    stringResource(R.string.search_messages_unavailable), stringResource(R.string.search_messages_unavailable_description)) else SearchErrorState(onRetry)
                SearchPhase.Empty -> SearchMessageState(R.drawable.ic_search,
                    stringResource(R.string.search_nothing_found), stringResource(R.string.search_try_different))
                SearchPhase.Results -> LazyColumn(
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    items(state.hits, key = { it.messageId }) { hit -> MessageSearchResult(hit, state.query) { onMessageClick(hit) } }
                    if (state.nextCursor != null) item {
                        Box(Modifier.fillMaxWidth().heightIn(min = 48.dp), contentAlignment = Alignment.Center) {
                            if (state.isLoadingMore) CircularProgressIndicator() else TextButton(onClick = onLoadMore) {
                                Text(stringResource(if (state.moreFailed) R.string.search_retry else R.string.search_load_more))
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MessageSearchResult(hit: MessageSearchHit, query: String, onClick: () -> Unit) {
    val highlight = MaterialTheme.colorScheme.primaryContainer
    val highlightText = MaterialTheme.colorScheme.onPrimaryContainer
    val excerpt = remember(hit.text, query, highlight, highlightText) {
        val needle = query.trim()
        val index = if (needle.isEmpty()) -1 else hit.text.indexOf(needle, ignoreCase = true)
        val start = if (index < 0) 0 else (index - 60).coerceAtLeast(0)
        val end = (start + 240).coerceAtMost(hit.text.length)
        buildAnnotatedString {
            if (start > 0) append("…")
            val offset = length
            append(hit.text.substring(start, end))
            if (index >= 0) addStyle(SpanStyle(background = highlight, color = highlightText), offset + index - start,
                offset + (index + needle.length).coerceAtMost(end) - start)
            if (end < hit.text.length) append("…")
        }
    }
    val locale = LocalConfiguration.current.locales[0]
    val timestamp = remember(hit.sentAtMillis, locale) { DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT, locale).format(Date(hit.sentAtMillis)) }
    Surface(Modifier.fillMaxWidth().clickable(role = Role.Button, onClick = onClick),
        shape = RoundedCornerShape(20.dp), color = MaterialTheme.colorScheme.surfaceContainerLow) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(hit.chatTitle.ifBlank { stringResource(R.string.chat_title_default) }, style = MaterialTheme.typography.titleMedium,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(stringResource(R.string.search_message_metadata, hit.author.name.ifBlank { stringResource(R.string.search_unknown_author) }, timestamp),
                style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (excerpt.isNotEmpty()) Text(excerpt, maxLines = 4, overflow = TextOverflow.Ellipsis)
            val attachments = searchAttachmentTypes.filter { it.first in hit.attachmentTypes }.map { stringResource(it.second) }
            if (attachments.isNotEmpty()) Text(attachments.joinToString(", "), style = MaterialTheme.typography.bodySmall)
        }
    }
}
