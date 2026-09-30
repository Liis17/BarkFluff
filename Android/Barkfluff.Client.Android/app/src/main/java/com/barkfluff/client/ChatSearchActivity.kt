package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.core.view.WindowCompat
import barkfluff.shared.Shared
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.search.BarkFluffSearchTheme
import com.barkfluff.client.search.ChatSearchScreen
import com.barkfluff.client.search.ChatSearchViewModel
import com.barkfluff.client.search.SearchChat
import com.google.android.material.color.DynamicColors
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class ChatSearchActivity : AppCompatActivity() {
    private val viewModel: ChatSearchViewModel by viewModels()
    @Inject lateinit var fileMediaGateway: FileMediaGateway
    private var hasResumed = false

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        setContent {
            val state by viewModel.uiState.collectAsState()
            BarkFluffSearchTheme {
                ChatSearchScreen(
                    state = state,
                    onQueryChanged = viewModel::onQueryChanged,
                    onRetry = viewModel::retry,
                    onBack = { onBackPressedDispatcher.onBackPressed() },
                    onChatClick = ::openChat,
                    getAvatarUrl = { fileId -> fileMediaGateway.downloadUrl(fileId).getOrNull() },
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        if (hasResumed) viewModel.retry()
        hasResumed = true
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
