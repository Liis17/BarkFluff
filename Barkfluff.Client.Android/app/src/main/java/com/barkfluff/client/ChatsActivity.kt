package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.GravityCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityChatsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.imageview.ShapeableImageView
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

/**
 * Экран чатов с боковой панелью
 * Загружает список чатов через MessagesApi.ListChats
 */
class ChatsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityChatsBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatAdapter: ChatAdapter

    companion object {
        private const val TAG = "ChatsActivity"
        private const val TOKEN_BUFFER_MINUTES = 5
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityChatsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager()

        setupToolbar()
        setupChatList()
        setupDrawer()

        checkTokenAndLoadChats()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener {
            binding.drawerLayout.openDrawer(GravityCompat.START)
        }

        val serverName = globalParam.serverName
        if (serverName.isNotBlank()) {
            binding.toolbar.title = serverName
        }
    }

    private fun setupChatList() {
        chatAdapter = ChatAdapter({ chat ->
            onChatClicked(chat)
        }) { fileId ->
            // Callback для получения URL по fileId
            Log.d(TAG, "setupChatList: Requesting URL for fileId=$fileId")
            runBlocking {
                val result = grpcManager.getFileDownloadUrl(fileId)
                if (result.isSuccess) {
                    val url = result.getOrNull()
                    Log.d(TAG, "setupChatList: Got URL for fileId=$fileId, url=$url")
                    url
                } else {
                    Log.e(TAG, "setupChatList: Failed to get URL for fileId=$fileId, error=${result.exceptionOrNull()?.message}")
                    null
                }
            }
        }

        binding.chatRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@ChatsActivity)
            adapter = chatAdapter
            setHasFixedSize(false)
        }
    }

    private fun setupDrawer() {
        val drawerView = binding.drawerContent.root

        // Профиль
        val fullNameText = drawerView.findViewById<TextView>(R.id.drawerFullName)
        val usernameText = drawerView.findViewById<TextView>(R.id.drawerUsername)
        val avatarImage = drawerView.findViewById<ShapeableImageView>(R.id.drawerAvatar)
        val avatarPlaceholder = drawerView.findViewById<TextView>(R.id.drawerAvatarPlaceholder)
        val versionText = drawerView.findViewById<TextView>(R.id.drawerAppVersion)

        val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
        fullNameText.text = fullName.ifBlank { "Пользователь" }
        usernameText.text = if (globalParam.userName.isNotBlank()) "@${globalParam.userName}" else ""

        // Загрузка аватара через fileId
        val avatarFileId = globalParam.pictureFileId
        AvatarLoader.loadByFileId(
            imageView = avatarImage,
            placeholderView = avatarPlaceholder,
            fileId = avatarFileId.ifBlank { null },
            displayName = fullName.ifBlank { globalParam.userName },
            userId = globalParam.userId
        ) {
            // Callback для получения URL по fileId
            if (avatarFileId.isNotBlank()) {
                runBlocking {
                    grpcManager.getFileDownloadUrl(avatarFileId).getOrNull()
                }
            } else {
                null
            }
        }

        versionText.text = "BarkFluff v${GlobalParam.getAppVersion(this)}"

        // Обработчики меню
        drawerView.findViewById<LinearLayout>(R.id.menuFavorites)?.setOnClickListener {
            Snackbar.make(binding.root, "Избранное - скоро", Snackbar.LENGTH_SHORT).show()
            binding.drawerLayout.closeDrawer(GravityCompat.START)
        }

        drawerView.findViewById<LinearLayout>(R.id.menuSettings)?.setOnClickListener {
            Snackbar.make(binding.root, "Настройки - скоро", Snackbar.LENGTH_SHORT).show()
            binding.drawerLayout.closeDrawer(GravityCompat.START)
        }
    }

    private fun checkTokenAndLoadChats() {
        lifecycleScope.launch {
            val hasRefreshToken = globalParam.refreshToken != null
            val hasAccessToken = globalParam.accessToken != null
            val isAccessTokenExpired = isAccessTokenExpired()

            when {
                !hasRefreshToken -> {
                    navigateToLogin()
                    return@launch
                }
                hasRefreshToken && (!hasAccessToken || isAccessTokenExpired) -> {
                    val refreshResult = tryRefreshToken()
                    if (!refreshResult) {
                        navigateToLogin()
                        return@launch
                    }
                }
            }

            initGrpcClients()
            loadChats()
        }
    }

    private fun initGrpcClients() {
        Log.d(TAG, "initGrpcClients: identity=${globalParam.socketIdentity}, users=${globalParam.socketUsers}, messages=${globalParam.socketMessages}, files=${globalParam.socketFiles}")
        if (globalParam.socketIdentity.isNotBlank()) {
            grpcManager.createIdentityClient(globalParam.socketIdentity, this, includeDeviceInfo = true)
        }
        if (globalParam.socketUsers.isNotBlank()) {
            grpcManager.createUsersClient(globalParam.socketUsers, this, includeDeviceInfo = true)
        }
        if (globalParam.socketMessages.isNotBlank()) {
            grpcManager.createMessagesClient(globalParam.socketMessages, this, includeDeviceInfo = true)
        }
        if (globalParam.socketFiles.isNotBlank()) {
            grpcManager.createFilesClient(globalParam.socketFiles, this, includeDeviceInfo = true)
        }
        Log.d(TAG, "initGrpcClients: Clients initialized, filesClient is null: ${grpcManager.filesClient == null}")
    }

    private fun loadChats() {
        lifecycleScope.launch {
            showLoading(true)

            val result = grpcManager.getChats()
            if (result.isSuccess) {
                val chats = result.getOrNull() ?: emptyList()
                Log.d(TAG, "Загружено ${chats.size} чатов")

                val displayItems = chats.map { chat ->
                    resolveDisplayItem(chat)
                }

                chatAdapter.submitList(displayItems)
                showEmptyState(displayItems.isEmpty())
            } else {
                Log.e(TAG, "Ошибка загрузки чатов", result.exceptionOrNull())
                Snackbar.make(
                    binding.root,
                    "Ошибка загрузки чатов: ${result.exceptionOrNull()?.message}",
                    Snackbar.LENGTH_LONG
                ).show()
                showEmptyState(true)
            }

            showLoading(false)
        }
    }

    /**
     * Определяет отображаемое имя и аватар чата.
     * Для ЛС подтягивает данные собеседника через UsersApi.
     * Использует preview fileId для аватаров.
     * Если сервер вернул URL вместо fileId, извлекаем GUID или используем URL напрямую.
     */
    private suspend fun resolveDisplayItem(chat: GrpcManager.ChatData): ChatAdapter.ChatDisplayItem {
        if (!chat.isGroupChat && chat.title.isBlank()) {
            val otherUserId = chat.memberIds.firstOrNull { it != globalParam.userId }
            if (otherUserId != null) {
                val userResult = grpcManager.getUserData(otherUserId)
                if (userResult.isSuccess) {
                    val user = userResult.getOrNull()!!
                    val name = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
                    // Используем preview fileId если есть, иначе original
                    var avatarFileId = user.profilePicturePreviewFileId.ifBlank {
                        user.profilePictureFileId
                    }
                    // Если сервер вернул URL вместо fileId, извлекаем GUID
                    avatarFileId = extractGuidOrUseUrl(avatarFileId)
                    Log.d(TAG, "resolveDisplayItem: ЛС userId=$otherUserId, name=$name, avatarFileId=$avatarFileId")
                    return ChatAdapter.ChatDisplayItem(
                        chatData = chat,
                        displayTitle = name,
                        displayAvatarFileId = avatarFileId.ifBlank { null },
                        otherUserId = otherUserId
                    )
                }
            }
        }

        // Для групповых чатов используем picture fileId чата
        var avatarFileId = chat.picturePreviewFileId.ifBlank { chat.pictureFileId }
        // Если сервер вернул URL вместо fileId, извлекаем GUID
        avatarFileId = extractGuidOrUseUrl(avatarFileId)
        Log.d(TAG, "resolveDisplayItem: Групповой чат chatId=${chat.id}, title=${chat.title}, avatarFileId=$avatarFileId")
        return ChatAdapter.ChatDisplayItem(
            chatData = chat,
            displayTitle = chat.title.ifBlank { "Чат" },
            displayAvatarFileId = avatarFileId.ifBlank { null }
        )
    }

    /**
     * Извлекает GUID из URL или возвращает URL если это не URL.
     * Сервер может возвращать как fileId (GUID), так и полный URL.
     */
    private fun extractGuidOrUseUrl(urlOrGuid: String): String {
        if (urlOrGuid.isBlank()) return urlOrGuid
        
        // Если это URL (начинается с http), извлекаем GUID из конца
        if (urlOrGuid.startsWith("http://") || urlOrGuid.startsWith("https://")) {
            // URL формата: https://files.barkfluff.com/web/download/019c70b8-5dd2-71bc-a310-5db99a6fe5e3
            val lastSegment = urlOrGuid.substringAfterLast("/")
            // Проверяем что это GUID (36 символов с дефисами)
            if (lastSegment.matches(Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOption.IGNORE_CASE))) {
                return lastSegment
            }
            // Если не GUID, возвращаем URL как есть (для прямого использования)
            return urlOrGuid
        }
        
        // Это уже GUID или что-то другое
        return urlOrGuid
    }

    private fun onChatClicked(chat: GrpcManager.ChatData) {
        // TODO: Открытие чата (ChatActivity)
        Snackbar.make(binding.root, "Чат: ${chat.id}", Snackbar.LENGTH_SHORT).show()
    }

    private fun showLoading(show: Boolean) {
        binding.loadingIndicator.visibility = if (show) View.VISIBLE else View.GONE
    }

    private fun showEmptyState(show: Boolean) {
        binding.emptyState.visibility = if (show) View.VISIBLE else View.GONE
        binding.chatRecyclerView.visibility = if (show) View.GONE else View.VISIBLE
    }

    private fun isAccessTokenExpired(): Boolean {
        val accessTokenExpiration = globalParam.accessTokenExpiration
        if (accessTokenExpiration <= 0) return true

        val now = System.currentTimeMillis()
        val bufferMillis = TOKEN_BUFFER_MINUTES * 60 * 1000L
        return now + bufferMillis >= accessTokenExpiration
    }

    private suspend fun tryRefreshToken(): Boolean {
        return try {
            val identityAddress = globalParam.socketIdentity
            if (identityAddress.isBlank()) return false

            val createResult = grpcManager.createIdentityClient(identityAddress, this)
            if (createResult.isFailure) return false

            val refreshToken = globalParam.refreshToken ?: return false

            val refreshResult = grpcManager.refreshAccessToken(refreshToken, globalParam.refreshTokenExpiration)
            if (refreshResult.isSuccess) {
                val (newAccessToken, newAccessTokenExpiration, newRefreshToken, newRefreshTokenExpiration) = refreshResult.getOrNull()!!
                globalParam.accessToken = newAccessToken
                globalParam.accessTokenExpiration = newAccessTokenExpiration
                globalParam.refreshToken = newRefreshToken
                globalParam.refreshTokenExpiration = newRefreshTokenExpiration
                true
            } else {
                false
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка обновления токена", e)
            false
        }
    }

    private fun navigateToLogin() {
        val intent = Intent(this, LoginActivity::class.java)
        startActivity(intent)
        finish()
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        if (binding.drawerLayout.isDrawerOpen(GravityCompat.START)) {
            binding.drawerLayout.closeDrawer(GravityCompat.START)
        } else {
            MaterialAlertDialogBuilder(this)
                .setTitle("Выход из приложения")
                .setMessage("Вы действительно хотите выйти?")
                .setPositiveButton("Выйти") { _, _ ->
                    super.onBackPressed()
                }
                .setNegativeButton("Отмена", null)
                .show()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        grpcManager.shutdown()
    }
}
