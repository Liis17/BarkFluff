package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.FragmentChatsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class ChatsFragment : Fragment() {

    private var _binding: FragmentChatsBinding? = null
    private val binding get() = _binding!!

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatAdapter: ChatAdapter
    private lateinit var realtimeService: RealtimeService

    companion object {
        private const val TAG = "ChatsFragment"
        private const val TOKEN_BUFFER_MINUTES = 5
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentChatsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        val app = requireActivity().application as BarkFluffApplication
        globalParam = GlobalParam(requireContext())
        grpcManager = app.grpcManager
        realtimeService = app.realtimeService

        setupToolbar()
        setupChatList()
        setupSearchButton()

        subscribeToRealtimeEvents()
        checkTokenAndLoadChats()
    }

    override fun onResume() {
        super.onResume()
        val app = requireActivity().application as BarkFluffApplication
        if (app.cameFromBackground) {
            app.cameFromBackground = false
            // Перезагружаем список чатов при возврате из фона,
            // т.к. реалтайм-события в фоне не приходят
            if (grpcManager.messagesClient != null) {
                Log.d(TAG, "onResume: app came from background, checking token and reloading chats")
                viewLifecycleOwner.lifecycleScope.launch {
                    // Сначала проверяем и обновляем токен при необходимости
                    val tokenValid = grpcManager.ensureTokenValid(requireContext())
                    if (!tokenValid) {
                        Log.w(TAG, "onResume: Token refresh failed, navigating to login")
                        navigateToLogin()
                        return@launch
                    }
                    loadChats()
                }
            }
        }
    }

    private fun setupSearchButton() {
        binding.searchButton.setOnClickListener {
            val intent = Intent(requireContext(), SearchActivity::class.java)
            startActivity(intent)
        }
    }

    private fun setupToolbar() {
        // Убираем стандартный title toolbar'а, используем свой TextView
        binding.toolbar.title = null
        // Показываем имя пользователя
        updateToolbarTitle()
    }

    private fun updateToolbarTitle(isConnecting: Boolean = false) {
        if (isConnecting) {
            binding.toolbarTitle.text = getString(R.string.connecting)
        } else {
            val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
            binding.toolbarTitle.text = fullName.ifBlank { globalParam.userName }
        }
    }

    private fun loadUserAvatar() {
        val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
        val avatarFileId = globalParam.pictureFileId
        val avatarPreviewFileId = globalParam.picturePreviewFileId

        Log.d(TAG, "loadUserAvatar: fullName='$fullName', avatarFileId='$avatarFileId', avatarPreviewFileId='$avatarPreviewFileId', userId=${globalParam.userId}")

        // Пробуем загрузить из URL напрямую если он есть
        val urlToUse = globalParam.picturePreviewUrl.ifBlank { globalParam.profilePictureUrl }

        if (urlToUse.isNotBlank()) {
            Log.d(TAG, "loadUserAvatar: Loading from URL=$urlToUse")
            // Сначала показываем placeholder пока изображение загружается
            AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
            binding.userAvatar.visibility = View.GONE
            
            AvatarLoader.load(
                imageView = binding.userAvatar,
                placeholderView = binding.userAvatarPlaceholder,
                avatarUrl = urlToUse,
                displayName = fullName.ifBlank { globalParam.userName },
                userId = globalParam.userId
            )
            return
        }

        val useFileId = avatarPreviewFileId.ifBlank { avatarFileId }

        if (useFileId.isBlank()) {
            Log.d(TAG, "loadUserAvatar: No fileId, showing placeholder")
            AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
            binding.userAvatar.visibility = View.GONE
            return
        }

        // Сначала показываем placeholder пока изображение загружается
        AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
        binding.userAvatar.visibility = View.GONE
        
        AvatarLoader.loadByFileId(
            imageView = binding.userAvatar,
            placeholderView = binding.userAvatarPlaceholder,
            fileId = useFileId,
            displayName = fullName.ifBlank { globalParam.userName },
            userId = globalParam.userId,
            size = 64
        ) {
            val result = grpcManager.getFileDownloadUrl(useFileId)
            if (result.isSuccess) result.getOrNull() else null
        }
    }

    private fun setupChatList() {
        chatAdapter = ChatAdapter({ chat ->
            onChatClicked(chat)
        }) { fileId ->
            Log.d(TAG, "setupChatList: Requesting URL for fileId=$fileId")
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

        chatAdapter.currentUserId = globalParam.userId

        binding.chatRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext())
            adapter = chatAdapter
            setHasFixedSize(false)
        }
    }

    private fun checkTokenAndLoadChats() {
        viewLifecycleOwner.lifecycleScope.launch {
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

            if (globalParam.pictureFileId.isBlank() && globalParam.picturePreviewFileId.isBlank()) {
                Log.d(TAG, "checkTokenAndLoadChats: Аватар не загружен, загружаем данные пользователя")
                val userDataResult = grpcManager.getCurrentUserData()
                if (userDataResult.isSuccess) {
                    val userData = userDataResult.getOrNull()
                    if (userData != null) {
                        globalParam.pictureFileId = userData.profilePictureFileId
                        globalParam.picturePreviewFileId = userData.profilePicturePreviewFileId
                        globalParam.picturePreviewUrl = userData.profilePicturePreviewUrl
                        globalParam.profilePictureUrl = userData.profilePictureUrl
                        Log.d(TAG, "checkTokenAndLoadChats: Загружены pictureFileId='${globalParam.pictureFileId}', picturePreviewFileId='${globalParam.picturePreviewFileId}', picturePreviewUrl='${globalParam.picturePreviewUrl}', profilePictureUrl='${globalParam.profilePictureUrl}'")
                    }
                }
            }

            loadUserAvatar()
            loadChats()
        }
    }

    private fun initGrpcClients() {
        val ctx = requireContext()
        Log.d(TAG, "initGrpcClients: identity=${globalParam.socketIdentity}, users=${globalParam.socketUsers}, messages=${globalParam.socketMessages}, files=${globalParam.socketFiles}")
        grpcManager.initAllClients(ctx, globalParam)
        Log.d(TAG, "initGrpcClients: Clients initialized, filesClient is null: ${grpcManager.filesClient == null}")
    }

    private fun loadChats() {
        viewLifecycleOwner.lifecycleScope.launch {
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

                // Обновляем подписку на онлайн-статусы всех участников чатов
                val allMemberIds = chats.flatMap { it.memberIds }.distinct()
                realtimeService.changeOnlineSubscription(allMemberIds)
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

    // Job для отложенного показа "Соединение..."
    private var connectionCheckJob: Job? = null

    private fun subscribeToRealtimeEvents() {
        // Подписка на новые сообщения
        viewLifecycleOwner.lifecycleScope.launch {
            realtimeService.newMessages.collect { event ->
                handleNewMessage(event)
            }
        }
        // Подписка на прочтение сообщений
        viewLifecycleOwner.lifecycleScope.launch {
            realtimeService.messagesRead.collect { event ->
                handleMessageRead(event)
            }
        }
        // Подписка на состояние соединения с задержкой показа
        viewLifecycleOwner.lifecycleScope.launch {
            realtimeService.connectionState.collect { state ->
                when (state) {
                    RealtimeService.ConnectionState.CONNECTED -> {
                        // Отменяем отложенный показ "Соединение..."
                        connectionCheckJob?.cancel()
                        connectionCheckJob = null
                        withContext(Dispatchers.Main) {
                            updateToolbarTitle(isConnecting = false)
                        }
                    }
                    RealtimeService.ConnectionState.CONNECTING,
                    RealtimeService.ConnectionState.DISCONNECTED -> {
                        // Показываем "Соединение..." только если не соединились в течение 1 секунды
                        if (connectionCheckJob == null) {
                            connectionCheckJob = launch {
                                delay(1000) // Ждём 1 секунду
                                withContext(Dispatchers.Main) {
                                    updateToolbarTitle(isConnecting = true)
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private fun handleNewMessage(event: barkfluff.updates.UpdatesApiOuterClass.NewMessageEvent) {
        val msg = event.message
        val found = chatAdapter.updateChatWithNewMessage(
            chatId = event.chatId,
            senderId = msg.senderId,
            messageId = msg.id,
            text = msg.content?.text ?: "",
            sentAt = msg.sentAt.seconds * 1000,
            currentUserId = globalParam.userId
        )

        if (!found) {
            // Новый чат — резолвим информацию об отправителе и добавляем
            viewLifecycleOwner.lifecycleScope.launch {
                try {
                    val senderId = msg.senderId
                    val lastMessage = GrpcManager.LastMessageData(
                        id = msg.id,
                        senderId = senderId,
                        text = msg.content?.text ?: "",
                        sentAt = msg.sentAt.seconds * 1000,
                        readBy = listOf(senderId)
                    )
                    val chatData = GrpcManager.ChatData(
                        id = event.chatId,
                        title = "",
                        picture = "",
                        isGroupChat = false,
                        lastMessage = lastMessage,
                        memberIds = listOf(globalParam.userId, senderId),
                        countUnread = if (senderId != globalParam.userId) 1 else 0,
                        firstUnreadMessageId = msg.id
                    )
                    val displayItem = resolveDisplayItem(chatData)
                    chatAdapter.addNewChat(displayItem)
                } catch (e: Exception) {
                    Log.e(TAG, "Failed to add new chat for chatId=${event.chatId}", e)
                }
            }
        }

        showEmptyState(false)
    }

    private fun handleMessageRead(event: barkfluff.updates.UpdatesApiOuterClass.MessageReadEvent) {
        chatAdapter.updateReadStatus(
            chatId = event.chatId,
            messageId = event.messageId,
            newReadBy = event.newReadByList,
            currentUserId = globalParam.userId
        )
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

        // Для группового чата также извлекаем fileId из URL картинки
        val chatPictureFileId = chat.pictureFileId
        Log.d(TAG, "resolveDisplayItem: Групповой чат chatId=${chat.id}, title=${chat.title}, pictureFileId=$chatPictureFileId")
        return ChatAdapter.ChatDisplayItem(
            chatData = chat,
            displayTitle = chat.title.ifBlank { "Чат" },
            displayAvatarFileId = chatPictureFileId.ifBlank { null }
        )
    }

    private fun onChatClicked(chat: GrpcManager.ChatData) {
        // Находим display item для получения дополнительной информации
        val displayItem = chatAdapter.currentList.find { it.chatData.id == chat.id }

        val intent = Intent(requireContext(), ChatActivity::class.java).apply {
            putExtra("chat_id", chat.id)
            putExtra("chat_title", displayItem?.displayTitle ?: chat.title.ifBlank { "Чат" })
            putExtra("chat_avatar_file_id", displayItem?.displayAvatarFileId ?: chat.pictureFileId.ifBlank { null })
            putExtra("is_group_chat", chat.isGroupChat)
            putExtra("other_user_id", displayItem?.otherUserId ?: 0L)
        }

        // Устанавливаем чат как открытый перед запуском Activity
        OpenChatManager.setOpenChat(chat.id)

        startActivity(intent)
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

            val createResult = grpcManager.createIdentityClient(identityAddress, requireContext())
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
        val intent = Intent(requireContext(), LoginActivity::class.java)
        startActivity(intent)
        requireActivity().finish()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
