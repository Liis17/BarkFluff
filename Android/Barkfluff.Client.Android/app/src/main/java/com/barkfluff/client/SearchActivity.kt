package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.text.InputType
import android.util.Log
import android.widget.LinearLayout
import android.widget.Toast
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.WindowCompat
import androidx.lifecycle.lifecycleScope
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.UserDirectoryGateway
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.repository.PrivateChatRepository
import com.barkfluff.client.search.BarkFluffSearchTheme
import com.barkfluff.client.search.SearchScreen
import com.barkfluff.client.search.SearchUser
import com.barkfluff.client.search.SearchViewModel
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

/**
 * Самостоятельный экран поиска пользователей.
 *
 * UI живёт в Compose, а backend-контракт, интенты и private-mode остаются прежними.
 * activity_search.xml намеренно не используется: его переиспользует AddGroupMemberActivity.
 */
@AndroidEntryPoint
class SearchActivity : AppCompatActivity() {

    private val searchViewModel: SearchViewModel by viewModels()

    @javax.inject.Inject lateinit var userDirectoryGateway: UserDirectoryGateway
    @javax.inject.Inject lateinit var fileMediaGateway: FileMediaGateway
    @javax.inject.Inject lateinit var privateChatRepository: PrivateChatRepository
    private var isPrivateMode = false
    private var isActionInProgress by mutableStateOf(false)

    companion object {
        private const val TAG = "SearchActivity"
        const val EXTRA_MODE = "search_mode"
        const val MODE_PRIVATE = "private"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        WindowCompat.setDecorFitsSystemWindows(window, false)

        isPrivateMode = intent.getStringExtra(EXTRA_MODE) == MODE_PRIVATE

        setContent {
            val uiState by searchViewModel.uiState.collectAsState()

            BarkFluffSearchTheme {
                SearchScreen(
                    isPrivateMode = isPrivateMode,
                    uiState = uiState,
                    isActionInProgress = isActionInProgress,
                    onQueryChanged = searchViewModel::onQueryChanged,
                    onSubmit = searchViewModel::submitQuery,
                    onRetry = searchViewModel::retry,
                    onClear = { searchViewModel.onQueryChanged("") },
                    onBack = { onBackPressedDispatcher.onBackPressed() },
                    onUserClick = ::onUserClick,
                    getAvatarUrl = { fileId -> fileMediaGateway.downloadUrl(fileId).getOrNull() }
                )
            }
        }
    }

    private fun onUserClick(user: SearchUser) {
        if (isActionInProgress) return
        if (isPrivateMode) {
            showPrivateChatPassword(user.userData)
        } else {
            openChatWithUser(user.userData)
        }
    }

    private fun openChatWithUser(userData: UserProfile) {
        Log.d(TAG, "openChatWithUser: userId=${userData.userId}, username=${userData.username}")

        lifecycleScope.launch {
            isActionInProgress = true
            try {
                val result = userDirectoryGateway.personChatId(userData.userId)
                if (result.isSuccess) {
                    val chatId = result.getOrNull()
                    Log.d(TAG, "openChatWithUser: Got chatId=$chatId")

                    val displayName = "${userData.firstName} ${userData.lastName}"
                        .trim()
                        .ifBlank { userData.username }
                    val avatarFileId = userData.profilePicturePreviewFileId
                        .ifBlank { userData.profilePictureFileId }
                        .ifBlank { null }

                    val intent = Intent(this@SearchActivity, ChatActivity::class.java).apply {
                        putExtra("chat_id", chatId)
                        putExtra("chat_title", displayName)
                        putExtra("chat_avatar_file_id", avatarFileId)
                        putExtra("is_group_chat", false)
                        putExtra("other_user_id", userData.userId)
                    }
                    startActivity(intent)
                } else {
                    Log.e(TAG, "Ошибка получения chatId", result.exceptionOrNull())
                    Toast.makeText(
                        this@SearchActivity,
                        getString(
                            R.string.chat_open_failed_detail,
                            result.exceptionOrNull()?.message.orEmpty()
                        ),
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } finally {
                isActionInProgress = false
            }
        }
    }

    private fun showPrivateChatPassword(userData: UserProfile) {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val margin = (24 * resources.displayMetrics.density).toInt()
            setPadding(margin, 0, margin, 0)
        }
        val passwordLayout = TextInputLayout(
            this,
            null,
            com.google.android.material.R.attr.textInputOutlinedStyle
        ).apply {
            hint = getString(R.string.private_chat_password_hint)
        }
        val password = TextInputEditText(passwordLayout.context).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        passwordLayout.addView(password)
        val remember = MaterialCheckBox(this).apply {
            text = getString(R.string.private_chat_remember_password)
            isChecked = false
        }
        content.addView(passwordLayout)
        content.addView(remember)

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.private_chat_password_title)
            .setMessage(R.string.private_chat_password_description)
            .setView(content)
            .setNegativeButton(R.string.btn_cancel, null)
            .setPositiveButton(R.string.encrypted_chat_create) { _, _ ->
                val passphrase = password.text?.toString().orEmpty()
                if (passphrase.length < 6) {
                    Toast.makeText(
                        this,
                        R.string.private_chat_password_too_short,
                        Toast.LENGTH_SHORT
                    ).show()
                    return@setPositiveButton
                }
                createPrivateChat(userData, passphrase, remember.isChecked)
            }
            .show()
    }

    private fun createPrivateChat(
        userData: UserProfile,
        passphrase: String,
        rememberKey: Boolean
    ) {
        lifecycleScope.launch {
            isActionInProgress = true
            try {
                val result = privateChatRepository.createPrivateChat(
                    userData.userId,
                    passphrase,
                    rememberKey
                )
                result.onSuccess { creation ->
                    val title = "${userData.firstName} ${userData.lastName}"
                        .trim()
                        .ifBlank { userData.username }
                    startActivity(
                        ChatActivity.privateChatIntent(
                            this@SearchActivity,
                            chatId = creation.chat.id,
                            title = title
                        )
                    )
                    finish()
                }.onFailure {
                    Toast.makeText(
                        this@SearchActivity,
                        getString(R.string.private_chat_open_failed, it.message.orEmpty()),
                        Toast.LENGTH_LONG
                    ).show()
                }
            } finally {
                isActionInProgress = false
            }
        }
    }
}
