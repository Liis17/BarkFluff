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
import com.barkfluff.client.databinding.FragmentChatsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class ChatsFragment : Fragment() {

    private var _binding: FragmentChatsBinding? = null
    private val binding get() = _binding!!

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatAdapter: ChatAdapter

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

        globalParam = GlobalParam(requireContext())
        grpcManager = GrpcManager()

        setupToolbar()
        setupChatList()
        setupSearchButton()

        checkTokenAndLoadChats()
    }

    private fun setupSearchButton() {
        binding.searchButton.setOnClickListener {
            val intent = Intent(requireContext(), SearchActivity::class.java)
            startActivity(intent)
        }
    }

    private fun setupToolbar() {
        val serverName = globalParam.serverName
        if (serverName.isNotBlank()) {
            binding.toolbar.title = serverName
        }
    }

    private fun loadUserAvatar() {
        val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
        val avatarFileId = globalParam.pictureFileId
        val avatarPreviewFileId = globalParam.picturePreviewFileId

        Log.d(TAG, "loadUserAvatar: fullName='$fullName', avatarFileId='$avatarFileId', avatarPreviewFileId='$avatarPreviewFileId', userId=${globalParam.userId}")

        val useFileId = avatarPreviewFileId.ifBlank { avatarFileId }

        if (useFileId.isBlank()) {
            Log.d(TAG, "loadUserAvatar: No fileId, showing placeholder")
            AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
            binding.userAvatar.visibility = View.GONE
            return
        }

        AvatarLoader.showPlaceholder(binding.userAvatarPlaceholder, fullName.ifBlank { globalParam.userName }, globalParam.userId)
        binding.userAvatar.visibility = View.GONE

        viewLifecycleOwner.lifecycleScope.launch {
            try {
                val urlResult = grpcManager.getFileDownloadUrl(useFileId)
                if (urlResult.isSuccess) {
                    val url = urlResult.getOrNull()
                    Log.d(TAG, "loadUserAvatar: Got URL for fileId=$useFileId, url=$url")

                    if (url != null) {
                        withContext(Dispatchers.Main) {
                            AvatarLoader.loadByFileId(
                                imageView = binding.userAvatar,
                                placeholderView = binding.userAvatarPlaceholder,
                                fileId = useFileId,
                                displayName = fullName.ifBlank { globalParam.userName },
                                userId = globalParam.userId,
                                size = 64
                            ) {
                                url
                            }
                        }
                    }
                } else {
                    Log.e(TAG, "loadUserAvatar: Failed to get URL - ${urlResult.exceptionOrNull()?.message}")
                }
            } catch (e: Exception) {
                Log.e(TAG, "loadUserAvatar: Exception - ${e.message}")
            }
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
                        Log.d(TAG, "checkTokenAndLoadChats: Загружены pictureFileId='${globalParam.pictureFileId}', picturePreviewFileId='${globalParam.picturePreviewFileId}'")
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
        if (globalParam.socketIdentity.isNotBlank()) {
            grpcManager.createIdentityClient(globalParam.socketIdentity, ctx, includeDeviceInfo = true)
        }
        if (globalParam.socketUsers.isNotBlank()) {
            grpcManager.createUsersClient(globalParam.socketUsers, ctx, includeDeviceInfo = true)
        }
        if (globalParam.socketMessages.isNotBlank()) {
            grpcManager.createMessagesClient(globalParam.socketMessages, ctx, includeDeviceInfo = true)
        }
        if (globalParam.socketFiles.isNotBlank()) {
            grpcManager.createFilesClient(globalParam.socketFiles, ctx, includeDeviceInfo = true)
        }
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

    private suspend fun resolveDisplayItem(chat: GrpcManager.ChatData): ChatAdapter.ChatDisplayItem {
        if (!chat.isGroupChat && chat.title.isBlank()) {
            val otherUserId = chat.memberIds.firstOrNull { it != globalParam.userId }
            if (otherUserId != null) {
                val userResult = grpcManager.getUserData(otherUserId)
                if (userResult.isSuccess) {
                    val user = userResult.getOrNull()!!
                    val name = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
                    // Используем извлечённый fileId (GUID), а не сырой URL Minio,
                    // т.к. URL Minio — внутренний и недоступен с клиента напрямую.
                    // AvatarLoader по fileId получит temp download URL через gRPC.
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
        grpcManager.shutdown()
        _binding = null
    }
}
