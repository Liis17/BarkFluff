package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.deeplink.DeepLinkCommand
import com.barkfluff.client.deeplink.DeepLinkHandler
import kotlinx.coroutines.launch

/**
 * Прозрачная Activity — точка входа для deep link URI (bf:// и bfdev://).
 * Не имеет собственного UI.
 */
class DeepLinkActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "DeepLinkActivity"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        handleDeepLink(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleDeepLink(intent)
    }

    private fun handleDeepLink(intent: Intent?) {
        val uri = intent?.data
        if (uri == null) {
            finish()
            return
        }

        Log.d(TAG, "Deep link received: host=${uri.host}")

        val command = DeepLinkHandler.parse(uri)

        when (command) {
            is DeepLinkCommand.Unknown -> {
                Toast.makeText(this, "Неизвестная команда", Toast.LENGTH_SHORT).show()
                finish()
            }
            is DeepLinkCommand.OpenUserChat -> {
                val app = applicationContext as BarkFluffApplication
                val globalParam = GlobalParam(this)
                val hasToken = globalParam.refreshToken != null
                val grpcReady = app.grpcManager.isInitialized()
                val cameFromBackground = app.cameFromBackground

                if (hasToken && grpcReady && !cameFromBackground) {
                    resolveAndOpenChat(command.username)
                } else {
                    // gRPC не готов или не авторизован — сохраняем и идём через SplashActivity
                    app.pendingDeepLink = uri
                    startActivity(Intent(this, SplashActivity::class.java))
                    finish()
                }
            }
        }
    }

    private fun resolveAndOpenChat(username: String) {
        val app = applicationContext as BarkFluffApplication
        val grpcManager = app.grpcManager

        lifecycleScope.launch {
            val searchResult = grpcManager.searchUsers(username, size = 20)
            if (searchResult.isFailure) {
                Toast.makeText(this@DeepLinkActivity, "Ошибка поиска пользователя", Toast.LENGTH_SHORT).show()
                finish()
                return@launch
            }

            val users = searchResult.getOrNull() ?: emptyList()
            val user = users.find { it.username.equals(username, ignoreCase = true) }

            if (user == null) {
                Toast.makeText(this@DeepLinkActivity, "Пользователь @$username не найден", Toast.LENGTH_SHORT).show()
                finish()
                return@launch
            }

            val chatResult = grpcManager.getPersonChatId(user.userId)
            if (chatResult.isFailure) {
                Toast.makeText(this@DeepLinkActivity, "Не удалось открыть чат", Toast.LENGTH_SHORT).show()
                finish()
                return@launch
            }

            val chatId = chatResult.getOrNull()!!
            val displayName = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
            val avatarFileId = user.profilePicturePreviewFileId.ifBlank { user.profilePictureFileId }.ifBlank { null }

            OpenChatManager.setOpenChat(chatId)

            val chatIntent = Intent(this@DeepLinkActivity, ChatActivity::class.java).apply {
                putExtra("chat_id", chatId)
                putExtra("chat_title", displayName)
                putExtra("chat_avatar_file_id", avatarFileId)
                putExtra("is_group_chat", false)
                putExtra("other_user_id", user.userId)
                flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            }
            startActivity(chatIntent)
            finish()
        }
    }
}
