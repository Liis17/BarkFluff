package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.view.View
import android.widget.LinearLayout
import android.widget.RadioGroup
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.core.view.WindowCompat
import barkfluff.shared.Shared
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.model.MessageSearchAuthor
import com.barkfluff.client.domain.model.MessageSearchHit
import com.barkfluff.client.domain.model.SearchAttachmentPresence
import com.barkfluff.client.search.BarkFluffSearchTheme
import com.barkfluff.client.search.ChatSearchScreen
import com.barkfluff.client.search.ChatSearchViewModel
import com.barkfluff.client.search.MessageSearchViewModel
import com.barkfluff.client.search.SearchTab
import com.barkfluff.client.search.searchAttachmentTypes
import com.barkfluff.client.search.SearchChat
import com.google.android.material.color.DynamicColors
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.radiobutton.MaterialRadioButton
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.datepicker.MaterialDatePicker
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class ChatSearchActivity : AppCompatActivity() {
    private val viewModel: ChatSearchViewModel by viewModels()
    private val messages: MessageSearchViewModel by viewModels()
    @Inject lateinit var fileMediaGateway: FileMediaGateway
    private var hasResumed = false
    private val authorPicker = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == RESULT_OK) result.data?.let { data ->
            val id = data.getLongExtra(SearchActivity.EXTRA_AUTHOR_ID, 0)
            if (id > 0) messages.setAuthor(MessageSearchAuthor(userId = id, name = data.getStringExtra(SearchActivity.EXTRA_AUTHOR_NAME).orEmpty()))
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        if (messages.uiState.value.query.isEmpty()) messages.onQueryChanged(viewModel.uiState.value.query)
        setContent {
            val state by viewModel.uiState.collectAsState()
            val messageState by messages.uiState.collectAsState()
            BarkFluffSearchTheme {
                ChatSearchScreen(
                    state = state,
                    messages = messageState,
                    onQueryChanged = { query -> messages.onQueryChanged(query); viewModel.onQueryChanged(messages.uiState.value.query) },
                    onTabChanged = messages::onTabChanged,
                    onSubmit = messages::submitQuery,
                    onMessageRetry = messages::retry,
                    onLoadMore = messages::loadMore,
                    onAuthorFilter = ::showAuthorFilter,
                    onDateFilter = ::showDateFilter,
                    onAttachmentFilter = ::showAttachmentFilter,
                    onClearFilters = messages::clearFilters,
                    onRetry = viewModel::retry,
                    onBack = { onBackPressedDispatcher.onBackPressed() },
                    onChatClick = ::openChat,
                    onMessageClick = ::openMessage,
                    getAvatarUrl = { fileId -> fileMediaGateway.downloadUrl(fileId).getOrNull() },
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        if (hasResumed) {
            viewModel.retry()
            if (messages.uiState.value.tab == SearchTab.Messages) messages.retry()
        }
        hasResumed = true
    }

    private fun showAuthorFilter() {
        val me = MessageSearchAuthor(userId = GlobalParam(this).userId, name = getString(R.string.search_author_me))
        val selected = messages.uiState.value.filters.author
        val authors = (listOf(me) + listOfNotNull(selected) + messages.uiState.value.hits.map { it.author }).distinctBy { it.key }
        val labels = listOf(getString(R.string.search_author_any)) + authors.map { it.name.ifBlank { getString(R.string.search_unknown_author) } } + getString(R.string.search_author_choose)
        val checked = if (selected == null) 0 else authors.indexOfFirst { it.key == selected.key } + 1
        MaterialAlertDialogBuilder(this).setTitle(R.string.search_filter_author)
            .setSingleChoiceItems(labels.toTypedArray(), checked) { dialog, index ->
                dialog.dismiss()
                when (index) {
                    0 -> messages.setAuthor(null)
                    labels.lastIndex -> authorPicker.launch(Intent(this, SearchActivity::class.java).putExtra(SearchActivity.EXTRA_MODE, SearchActivity.MODE_PICK_AUTHOR))
                    else -> messages.setAuthor(authors[index - 1])
                }
            }.setNegativeButton(R.string.btn_cancel, null).show()
    }

    private fun showDateFilter() {
        val filters = messages.uiState.value.filters
        val picker = MaterialDatePicker.Builder.dateRangePicker().setTitleText(R.string.search_filter_date)
            .setSelection(androidx.core.util.Pair(filters.fromEpochDay?.times(86_400_000L), filters.throughEpochDay?.times(86_400_000L)))
            .setNegativeButtonText(R.string.search_reset_filters).build()
        picker.addOnPositiveButtonClickListener { range -> messages.setDates(range.first?.div(86_400_000L), range.second?.div(86_400_000L)) }
        picker.addOnNegativeButtonClickListener { messages.setDates(null, null) }
        picker.show(supportFragmentManager, "message_search_dates")
    }

    private fun showAttachmentFilter() {
        val filters = messages.uiState.value.filters
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val inset = (24 * resources.displayMetrics.density).toInt()
            setPadding(inset, 0, inset, 0)
        }
        val group = RadioGroup(this)
        val choices = SearchAttachmentPresence.entries.map { presence ->
            MaterialRadioButton(this).apply {
                id = View.generateViewId()
                text = getString(when (presence) {
                    SearchAttachmentPresence.Any -> R.string.search_attachments_any
                    SearchAttachmentPresence.With -> R.string.search_attachments_with
                    SearchAttachmentPresence.Without -> R.string.search_attachments_without
                })
                minimumHeight = (48 * resources.displayMetrics.density).toInt()
                group.addView(this)
            }
        }
        group.check(choices[filters.attachmentPresence.ordinal].id)
        content.addView(group)
        val checks = searchAttachmentTypes.map { (type, label) ->
            MaterialCheckBox(this).apply {
                text = getString(label)
                isChecked = type in filters.attachmentTypes
                isEnabled = filters.attachmentPresence != SearchAttachmentPresence.Without
                minimumHeight = (48 * resources.displayMetrics.density).toInt()
                content.addView(this)
            }
        }
        group.setOnCheckedChangeListener { _, checkedId -> checks.forEach { it.isEnabled = checkedId != choices[SearchAttachmentPresence.Without.ordinal].id } }
        val scroll = android.widget.ScrollView(this).apply { addView(content) }
        MaterialAlertDialogBuilder(this).setTitle(R.string.search_filter_attachments).setView(scroll)
            .setNegativeButton(R.string.btn_cancel, null).setPositiveButton(R.string.search_filter_apply) { _, _ ->
                val presence = SearchAttachmentPresence.entries[choices.indexOfFirst { it.id == group.checkedRadioButtonId }]
                messages.setAttachments(presence, searchAttachmentTypes.filterIndexed { index, _ -> checks[index].isChecked }.map { it.first }.toSet())
            }.show()
    }

    private fun openMessage(hit: MessageSearchHit) {
        startActivity(Intent(this, ChatActivity::class.java).apply {
            putExtra("chat_id", hit.chatId)
            putExtra("chat_title", hit.chatTitle.ifBlank { getString(R.string.chat_title_default) })
            putExtra("chat_avatar_file_id", hit.chatPictureFileId.ifBlank { null })
            putExtra("is_group_chat", hit.isGroupChat)
            putExtra("other_user_id", hit.otherUserId)
            putExtra("target_message_id", hit.messageId)
        })
    }

    private fun openChat(result: SearchChat) {
        val chat = result.chat
        val title = result.title.ifBlank { getString(R.string.chat_title_default) }
        val chatIntent = when (chat.chatType) {
            Shared.ChatType.CHAT_TYPE_PRIVATE -> ChatActivity.privateChatIntent(
                this, chat.id, title, chat.privateInviteState.number, chat.privateInviterUserId,
            )
            Shared.ChatType.CHAT_TYPE_SECRET -> ChatActivity.secretChatIntent(this, chat.id)
            else -> Intent(this, ChatActivity::class.java).apply {
                putExtra("chat_id", chat.id)
                putExtra("chat_title", title)
                putExtra("chat_avatar_file_id", result.avatarFileId)
                putExtra("is_group_chat", chat.isGroupChat)
                putExtra("other_user_id", result.otherUserId)
            }
        }
        startActivity(chatIntent)
    }
}
