package com.barkfluff.client.share

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityShareReceiverBinding
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch

/**
 * Принимает системные ACTION_SEND / ACTION_SEND_MULTIPLE и даёт пользователю выбрать
 * чат-получатель. После выбора чата открывает [ShareConfirmBottomSheet], который ставит
 * задачу в [com.barkfluff.client.send.MediaSendService].
 */
class ShareReceiverActivity : AppCompatActivity() {

    private lateinit var binding: ActivityShareReceiverBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatAdapter: ChatAdapter

    /** Прочитываемый из бот-шита payload — держится только в этой Activity на время share-сессии. */
    var payload: SharePayload? = null
        private set

    companion object {
        private const val TAG = "ShareReceiver"
        private const val TOKEN_BUFFER_MINUTES = 5
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityShareReceiverBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // applicationContext — чтобы SharedPreferences/EncryptedSharedPreferences не были
        // привязаны к жизненному циклу этой Activity (исключаем race на холодном старте).
        globalParam = GlobalParam(applicationContext)
        val app = applicationContext as BarkFluffApplication
        grpcManager = app.grpcManager

        binding.toolbar.setNavigationOnClickListener { finish() }

        applyWindowInsets()

        handleShareIntent(intent)
    }

    private fun applyWindowInsets() {
        // Тема edge-to-edge: тулбар должен «съехать» под статус-бар, а нижний край списка —
        // учесть жесто-навигацию.
        ViewCompat.setOnApplyWindowInsetsListener(binding.appBar) { v, insets ->
            val top = insets.getInsets(WindowInsetsCompat.Type.systemBars()).top
            v.updatePadding(top = top)
            insets
        }
        val basePaddingBottom = binding.chatsRecyclerView.paddingBottom
        ViewCompat.setOnApplyWindowInsetsListener(binding.chatsRecyclerView) { v, insets ->
            val bottom = insets.getInsets(WindowInsetsCompat.Type.systemBars()).bottom
            v.updatePadding(bottom = basePaddingBottom + bottom)
            insets
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        // singleTop: при втором share поверх живой Activity Android вызывает onNewIntent
        // вместо onCreate. Без этого override мы бы использовали старый intent.
        setIntent(intent)
        handleShareIntent(intent)
    }

    private fun handleShareIntent(intent: Intent?) {
        val refresh = globalParam.refreshToken
        Log.d(TAG, "handleShareIntent: hasRefreshToken=${!refresh.isNullOrBlank()}, " +
                "socketUsers='${globalParam.socketUsers}', socketMessages='${globalParam.socketMessages}', " +
                "action=${intent?.action}, type=${intent?.type}")

        if (refresh.isNullOrBlank()) {
            Toast.makeText(this, R.string.share_not_authorized, Toast.LENGTH_LONG).show()
            finish()
            return
        }

        payload = parseIncomingIntent(intent)
        if (payload == null) {
            Toast.makeText(this, R.string.share_empty, Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        setupRecycler()
        loadChats()
    }

    private fun parseIncomingIntent(intent: Intent?): SharePayload? {
        intent ?: return null
        return when (intent.action) {
            Intent.ACTION_SEND -> parseSend(intent)
            Intent.ACTION_SEND_MULTIPLE -> parseSendMultiple(intent)
            else -> null
        }
    }

    private fun parseSend(intent: Intent): SharePayload? {
        val type = intent.type.orEmpty()
        val uri: Uri? = getStream(intent)

        if (uri != null) {
            val mime = type.ifBlank { contentResolver.getType(uri).orEmpty() }
            tryTakeUriPermission(uri)
            return SharePayload.SingleFile(uri, mime)
        }

        val text = intent.getStringExtra(Intent.EXTRA_TEXT)
            ?: intent.getStringExtra(Intent.EXTRA_SUBJECT)
        return if (!text.isNullOrBlank()) SharePayload.Text(text) else null
    }

    private fun parseSendMultiple(intent: Intent): SharePayload? {
        val uris: List<Uri> = getStreams(intent)
        if (uris.isEmpty()) return null
        val items = uris.map { uri ->
            tryTakeUriPermission(uri)
            val mime = contentResolver.getType(uri).orEmpty()
                .ifBlank { intent.type.orEmpty() }
            SharePayload.MultipleFiles.Item(uri, mime)
        }
        return SharePayload.MultipleFiles(items)
    }

    @Suppress("DEPRECATION")
    private fun getStream(intent: Intent): Uri? {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
        } else {
            intent.getParcelableExtra(Intent.EXTRA_STREAM)
        }
    }

    @Suppress("DEPRECATION", "UNCHECKED_CAST")
    private fun getStreams(intent: Intent): List<Uri> {
        val list = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java)
        } else {
            intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)
        }
        return list ?: emptyList()
    }

    private fun tryTakeUriPermission(uri: Uri) {
        try {
            contentResolver.takePersistableUriPermission(
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION
            )
        } catch (_: SecurityException) {
            // Большинство share-источников отдают однократные read-grant URI — это нормально.
        }
    }

    private fun setupRecycler() {
        if (::chatAdapter.isInitialized) return
        chatAdapter = ChatAdapter(
            onChatClick = { chat -> onChatClicked(chat) },
            getFileUrl = { fileId ->
                val r = grpcManager.getFileDownloadUrl(fileId)
                if (r.isSuccess) r.getOrNull() else null
            }
        )
        chatAdapter.currentUserId = globalParam.userId
        binding.chatsRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@ShareReceiverActivity)
            adapter = chatAdapter
        }
    }

    private fun loadChats() {
        binding.loadingIndicator.visibility = View.VISIBLE
        binding.emptyState.visibility = View.GONE
        binding.chatsRecyclerView.visibility = View.GONE
        lifecycleScope.launch {
            if (!ensureTokenAndClients()) {
                binding.loadingIndicator.visibility = View.GONE
                Toast.makeText(this@ShareReceiverActivity, R.string.share_not_authorized, Toast.LENGTH_LONG).show()
                finish()
                return@launch
            }

            val result = grpcManager.getChats()
            if (result.isFailure) {
                Log.e(TAG, "getChats failed", result.exceptionOrNull())
                binding.loadingIndicator.visibility = View.GONE
                binding.emptyState.visibility = View.VISIBLE
                return@launch
            }

            val chats = result.getOrNull().orEmpty()
            if (chats.isEmpty()) {
                binding.loadingIndicator.visibility = View.GONE
                binding.emptyState.visibility = View.VISIBLE
                return@launch
            }

            val items = coroutineScope {
                chats.map { chat -> async { resolveDisplayItem(chat) } }.map { it.await() }
            }
            chatAdapter.submitList(items)
            binding.loadingIndicator.visibility = View.GONE
            binding.chatsRecyclerView.visibility = View.VISIBLE
        }
    }

    private suspend fun ensureTokenAndClients(): Boolean {
        return try {
            val tokenOk = grpcManager.ensureTokenValid(applicationContext)
            if (!tokenOk) {
                Log.w(TAG, "ensureTokenValid returned false")
                return false
            }
            grpcManager.initAllClients(applicationContext, globalParam)
            val ok = grpcManager.messagesClient != null && grpcManager.usersClient != null
            if (!ok) {
                Log.w(TAG, "initAllClients did not create messages/users clients: " +
                        "messages=${grpcManager.messagesClient != null}, " +
                        "users=${grpcManager.usersClient != null}, " +
                        "socketUsers='${globalParam.socketUsers}', " +
                        "socketMessages='${globalParam.socketMessages}'")
            }
            ok
        } catch (e: Exception) {
            Log.e(TAG, "ensureTokenAndClients failed", e)
            false
        }
    }

    private suspend fun resolveDisplayItem(chat: GrpcManager.ChatData): ChatAdapter.ChatDisplayItem {
        if (!chat.isGroupChat && chat.title.isBlank()) {
            val otherUserId = chat.memberIds.firstOrNull { it != globalParam.userId }
            if (otherUserId != null) {
                val userResult = grpcManager.getUserData(otherUserId)
                if (userResult.isSuccess) {
                    val user = userResult.getOrNull()!!
                    val name = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
                    val avatarFileId = user.profilePicturePreviewFileId.ifBlank { user.profilePictureFileId }
                    return ChatAdapter.ChatDisplayItem(
                        chatData = chat,
                        displayTitle = name,
                        displayAvatarFileId = avatarFileId.ifBlank { null },
                        otherUserId = otherUserId
                    )
                }
            }
        }
        return ChatAdapter.ChatDisplayItem(
            chatData = chat,
            displayTitle = chat.title.ifBlank { "Чат" },
            displayAvatarFileId = chat.pictureFileId.ifBlank { null }
        )
    }

    private fun onChatClicked(chat: GrpcManager.ChatData) {
        val displayItem = chatAdapter.currentList.find { !it.isFooter && it.chatData.id == chat.id }
        val title = displayItem?.displayTitle ?: chat.title.ifBlank { "Чат" }

        val p = payload ?: run { finish(); return }
        val sheet = ShareConfirmBottomSheet.newInstance(
            chatId = chat.id,
            chatTitle = title,
            payload = p
        )
        sheet.show(supportFragmentManager, "share_confirm")
    }
}
