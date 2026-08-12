package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.core.os.bundleOf
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import androidx.activity.result.contract.ActivityResultContracts
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.adapter.ChatSkeletonAdapter
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDisplay
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.adapter.FolderTabsAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.databinding.FragmentChatsBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FirebaseTokenHelper
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class ChatsFragment : Fragment() {

    private var _binding: FragmentChatsBinding? = null
    private val binding get() = _binding!!

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatAdapter: ChatAdapter
    private lateinit var foldersAdapter: FolderTabsAdapter
    private lateinit var realtimeService: RealtimeService
    private lateinit var chatCacheRepository: ChatCacheRepository
    private lateinit var chatDraftRepository: ChatDraftRepository
    private lateinit var skeletonAdapter: ChatSkeletonAdapter
    private var cacheScope: CacheScope? = null
    private var cachedDisplays: Map<String, CachedChatDisplay> = emptyMap()
    private var hasAppliedRemoteChats = false
    private var loadChatsJob: Job? = null
    private var syncStatus: SyncStatus? = null
    private var isRealtimeReconnecting = false
    private var titleAnimationGeneration = 0
    private var localDraftStates: Map<String, Boolean> = emptyMap()

    // Папки чатов
    private var folders: List<GrpcManager.ChatFolder> = emptyList()
    private var allChats: List<GrpcManager.ChatData> = emptyList()
    private var selectedFolderId: String? = null  // null = «Все чаты»
    private var totalChatsCount = 0
    private var isLoadingNextPage = false

    private val foldersSettingsLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { _ ->
        // По возвращении из настроек папок — перезагрузить
        loadChats()
    }

    companion object {
        private const val TAG = "ChatsFragment"
        private const val TOKEN_BUFFER_MINUTES = 5
        private const val TITLE_FADE_OUT_DURATION_MS = 90L
        private const val TITLE_FADE_IN_DURATION_MS = 160L
        const val MAIN_UNREAD_RESULT_KEY = "main_chats_unread"
        const val MAIN_UNREAD_COUNT = "count"
    }

    private enum class SyncStatus {
        UPDATING,
        OFFLINE
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
        chatCacheRepository = app.chatCacheRepository
        chatDraftRepository = app.chatDraftRepository
        cacheScope = CacheScope.from(globalParam)

        setupToolbar()
        setupChatList()
        setupFolderTabs()
        setupSearchButton()
        showSkeleton()

        subscribeToRealtimeEvents()
        viewLifecycleOwner.lifecycleScope.launch {
            chatDraftRepository.drafts.collect { drafts ->
                localDraftStates = drafts.mapValues { it.value.isActive }
                if (::chatAdapter.isInitialized) applyFolderFilter()
            }
        }
        viewLifecycleOwner.lifecycleScope.launch { chatDraftRepository.loadLocal() }
        hydrateChatsFromCache()
        checkTokenAndLoadChats()
    }

    private fun setupFolderTabs() {
        foldersAdapter = FolderTabsAdapter { folderId ->
            if (selectedFolderId != folderId) {
                selectedFolderId = folderId
                foldersAdapter.updateSelection(folderId)
                applyFolderFilter()
            }
        }
        binding.foldersRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
            adapter = foldersAdapter
        }
    }

    override fun onResume() {
        super.onResume()
        // Перерендерить сегмент папок с актуальными настройками компактности
        if (_binding != null && ::foldersAdapter.isInitialized && folders.isNotEmpty()) {
            renderFolderTabs()
            applyFolderFilter()
        }
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
                        Log.w(TAG, "onResume: Token refresh failed, keeping cached chats")
                        showSyncOffline()
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
        updateToolbarTitle(animate = false)
        binding.toolbarRetryButton.setOnClickListener {
            checkTokenAndLoadChats()
        }
    }

    private fun updateToolbarTitle(animate: Boolean = true) {
        val title = when {
            isRealtimeReconnecting -> getString(R.string.connecting)
            syncStatus == SyncStatus.UPDATING -> getString(R.string.chats_sync_updating)
            syncStatus == SyncStatus.OFFLINE -> getString(R.string.chats_sync_offline)
            else -> {
                val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
                fullName.ifBlank { globalParam.userName }
            }
        }

        if (binding.toolbarTitle.text == title) return

        binding.toolbarTitle.animate().cancel()
        if (!animate) {
            binding.toolbarTitle.alpha = 1f
            binding.toolbarTitle.translationY = 0f
            binding.toolbarTitle.text = title
            return
        }

        val generation = ++titleAnimationGeneration
        val offset = 4 * resources.displayMetrics.density
        binding.toolbarTitle.animate()
            .alpha(0f)
            .translationY(-offset)
            .setDuration(TITLE_FADE_OUT_DURATION_MS)
            .withEndAction {
                if (_binding == null || generation != titleAnimationGeneration) return@withEndAction
                binding.toolbarTitle.text = title
                binding.toolbarTitle.translationY = offset
                binding.toolbarTitle.animate()
                    .alpha(1f)
                    .translationY(0f)
                    .setDuration(TITLE_FADE_IN_DURATION_MS)
                    .start()
            }
            .start()
    }

    private fun loadUserAvatar() {
        val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
        val avatarFileId = globalParam.pictureFileId
        val avatarPreviewFileId = globalParam.picturePreviewFileId

        Log.d(TAG, "loadUserAvatar: fullName='$fullName', avatarFileId='$avatarFileId', avatarPreviewFileId='$avatarPreviewFileId', userId=${globalParam.userId}")

        // Пробуем загрузить из URL напрямую если он есть
        val urlToUse = globalParam.picturePreviewUrl.ifBlank { globalParam.profilePictureUrl }

        if (urlToUse.isNotBlank()) {
            Log.d(TAG, "loadUserAvatar: Loading from URL")
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
                Log.d(TAG, "setupChatList: Got URL for fileId=$fileId")
                url
            } else {
                Log.e(TAG, "setupChatList: Failed to get URL for fileId=$fileId, error=${result.exceptionOrNull()?.message}")
                null
            }
        }

        chatAdapter.currentUserId = globalParam.userId
        skeletonAdapter = ChatSkeletonAdapter()

        binding.chatRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext())
            adapter = skeletonAdapter
            setHasFixedSize(false)
            addOnScrollListener(object : RecyclerView.OnScrollListener() {
                override fun onScrolled(recyclerView: RecyclerView, dx: Int, dy: Int) {
                    if (dy <= 0 || isLoadingNextPage || allChats.size >= totalChatsCount) return
                    val layoutManager = recyclerView.layoutManager as? LinearLayoutManager ?: return
                    if (layoutManager.findLastVisibleItemPosition() >= chatAdapter.itemCount - 6) loadNextPage()
                }
            })
        }
    }

    private fun hydrateChatsFromCache() {
        val scope = cacheScope ?: run {
            showSkeleton()
            return
        }
        viewLifecycleOwner.lifecycleScope.launch {
            val snapshot = runCatching { chatCacheRepository.readChatList(scope) }.getOrNull()
            if (snapshot == null) {
                showSkeleton()
                return@launch
            }
            if (hasAppliedRemoteChats) return@launch

            allChats = snapshot.chats
            folders = snapshot.folders
            cachedDisplays = snapshot.displays
            totalChatsCount = snapshot.totalCount
            renderFolderTabs()
            applyFolderFilter()
            realtimeService.changeOnlineSubscription(allChats.flatMap { it.memberIds }.distinct())
        }
    }

    private fun checkTokenAndLoadChats() {

        viewLifecycleOwner.lifecycleScope.launch {
            showSyncUpdating()
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
                        showSyncOffline()
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
                        Log.d(TAG, "checkTokenAndLoadChats: Загружены pictureFileId='${globalParam.pictureFileId}', picturePreviewFileId='${globalParam.picturePreviewFileId}'")
                    }
                }
            }

            loadUserAvatar()
            loadChats()
            sendFirebaseToken()
        }
    }

    private fun initGrpcClients() {
        val ctx = requireContext()
        Log.d(TAG, "initGrpcClients: identity=${globalParam.socketIdentity}, users=${globalParam.socketUsers}, messages=${globalParam.socketMessages}, files=${globalParam.socketFiles}")
        grpcManager.initAllClients(ctx, globalParam)
        Log.d(TAG, "initGrpcClients: Clients initialized, filesClient is null: ${grpcManager.filesClient == null}")
    }

    private fun loadChats() {
        loadChatsJob?.cancel()
        loadChatsJob = viewLifecycleOwner.lifecycleScope.launch {
            showSyncUpdating()

            // Параллельно: чаты и папки. Чаты критичны, папки — нет (если упали — log + пустой список).
            val (chatsResult, foldersResult) = coroutineScope {
                val chatsDeferred = async { grpcManager.getChatsPage() }
                val foldersDeferred = async { grpcManager.getChatFolders() }
                chatsDeferred.await() to foldersDeferred.await()
            }

            if (chatsResult.isSuccess) {
                val page = chatsResult.getOrNull()!!
                val refreshedChats = page.chats.toMutableList()
                var nextOffset = refreshedChats.size
                for (pageIndex in 1 until 3) {
                    if (nextOffset >= page.totalCount) break
                    val nextPageResult = grpcManager.getChatsPage(offset = nextOffset)
                    val nextPage = nextPageResult.getOrNull() ?: break
                    refreshedChats += nextPage.chats
                    nextOffset += nextPage.chats.size
                    if (nextPage.chats.isEmpty()) break
                }
                hasAppliedRemoteChats = true
                allChats = (allChats + refreshedChats).associateBy { it.id }.values
                    .sortedByDescending { it.lastActivityAt }
                totalChatsCount = page.totalCount
                Log.d(TAG, "Загружено ${allChats.size} из $totalChatsCount чатов")

                folders = if (foldersResult.isSuccess) foldersResult.getOrNull() ?: emptyList() else {
                    Log.w(TAG, "Не удалось загрузить папки, продолжаем с пустым списком", foldersResult.exceptionOrNull())
                    folders
                }

                // Если выбранная папка пропала — сбрасываем на «Все чаты»
                if (selectedFolderId != null && folders.none { it.folderId == selectedFolderId }) {
                    selectedFolderId = null
                }

                renderFolderTabs()
                applyFolderFilter()

                // Обновляем подписку на онлайн-статусы всех участников чатов
                val allMemberIds = allChats.flatMap { it.memberIds }.distinct()
                realtimeService.changeOnlineSubscription(allMemberIds)
                cacheScope?.let { scope ->
                    runCatching {
                        chatCacheRepository.saveChatPage(
                            scope = scope,
                            chats = refreshedChats,
                            totalCount = totalChatsCount,
                            folders = foldersResult.getOrNull()
                        )
                    }.onFailure { Log.w(TAG, "Не удалось сохранить кеш чатов", it) }
                }
                hideSyncStatus()
            } else {
                Log.e(TAG, "Ошибка загрузки чатов", chatsResult.exceptionOrNull())
                Snackbar.make(
                    binding.root,
                    getString(
                        R.string.chat_load_error,
                        chatsResult.exceptionOrNull()?.message.orEmpty()
                    ),
                    Snackbar.LENGTH_LONG
                ).show()
                showSyncOffline()
                if (allChats.isEmpty()) showEmptyState(true)
            }

        }
    }

    private fun loadNextPage() {
        if (isLoadingNextPage || allChats.size >= totalChatsCount) return
        isLoadingNextPage = true
        viewLifecycleOwner.lifecycleScope.launch {
            grpcManager.getChatsPage(offset = allChats.size).onSuccess { page ->
                val merged = (allChats + page.chats).associateBy { it.id }.values
                    .sortedByDescending { it.lastActivityAt }
                allChats = merged
                totalChatsCount = page.totalCount
                cacheScope?.let { scope ->
                    viewLifecycleOwner.lifecycleScope.launch {
                        runCatching { chatCacheRepository.saveChatPage(scope, page.chats, page.totalCount) }
                    }
                }
                applyFolderFilter()
                realtimeService.changeOnlineSubscription(allChats.flatMap { it.memberIds }.distinct())
            }.onFailure {
                Log.w(TAG, "Не удалось загрузить следующую страницу чатов", it)
            }
            isLoadingNextPage = false
        }
    }

    private fun renderFolderTabs() {
        if (folders.isEmpty()) {
            binding.foldersRecyclerView.visibility = View.GONE
            return
        }
        binding.foldersRecyclerView.visibility = View.VISIBLE

        val allChatsItem = FolderTabsAdapter.Item(
            id = null,
            icon = "",
            name = getString(R.string.all_chats),
            unreadCount = computeAllChatsUnread()
        )
        val folderItems = folders.map { folder ->
            FolderTabsAdapter.Item(
                id = folder.folderId,
                icon = folder.folderIcon,
                name = folder.folderName,
                unreadCount = computeFolderUnread(folder.chatIds)
            )
        }
        foldersAdapter.submit(
            newItems = listOf(allChatsItem) + folderItems,
            compact = globalParam.compactFolders,
            noOutline = globalParam.folderTabsNoOutline,
            selected = selectedFolderId
        )
    }

    private fun refreshFolderTabs() {
        if (::foldersAdapter.isInitialized && folders.isNotEmpty()) {
            renderFolderTabs()
        }
    }

    /** Множество id чатов, входящих хотя бы в одну пользовательскую папку. */
    private fun chatsInUserFolders(): Set<String> {
        if (folders.isEmpty()) return emptySet()
        val s = HashSet<String>()
        for (f in folders) s.addAll(f.chatIds)
        return s
    }

    private fun computeFolderUnread(folderChatIds: List<String>): Int {
        if (folderChatIds.isEmpty() || allChats.isEmpty()) return 0
        val ids = folderChatIds.toSet()
        var sum = 0
        for (chat in allChats) {
            if (chat.id in ids) sum += chat.countUnread.toInt()
        }
        return sum
    }

    private fun computeAllChatsUnread(): Int {
        if (allChats.isEmpty()) return 0
        val exclude = globalParam.excludeFolderChatsFromAll
        val inFolders = if (exclude) chatsInUserFolders() else emptySet()
        var sum = 0
        for (chat in allChats) {
            if (exclude && chat.id in inFolders) continue
            sum += chat.countUnread.toInt()
        }
        return sum
    }

    private fun publishMainUnread() {
        if (!isAdded) return
        val unreadCount = allChats.fold(0L) { total, chat ->
            (total + chat.countUnread).coerceAtMost(Int.MAX_VALUE.toLong())
        }.toInt()
        parentFragmentManager.setFragmentResult(
            MAIN_UNREAD_RESULT_KEY,
            bundleOf(MAIN_UNREAD_COUNT to unreadCount)
        )
    }

    private fun applyFolderFilter() {
        publishMainUnread()
        val filtered: List<GrpcManager.ChatData> = if (selectedFolderId == null) {
            if (globalParam.excludeFolderChatsFromAll && folders.isNotEmpty()) {
                val inFolders = chatsInUserFolders()
                allChats.filter { it.id !in inFolders }
            } else {
                allChats
            }
        } else {
            val folder = folders.firstOrNull { it.folderId == selectedFolderId }
            if (folder == null) allChats
            else {
                val ids = folder.chatIds.toSet()
                allChats.filter { it.id in ids }
            }
        }.map { chat ->
            localDraftStates[chat.id]?.let { chat.copy(hasDraft = it) } ?: chat
        }
        val sorted = filtered.sortedByDescending { it.lastActivityAt }
        viewLifecycleOwner.lifecycleScope.launch {
            if (binding.chatRecyclerView.adapter !== chatAdapter) {
                binding.chatRecyclerView.adapter = chatAdapter
            }
            val displayItems = sorted.map { chat -> resolveDisplayItem(chat) }
            cachedDisplays = displayItems.associate { item ->
                item.chatData.id to CachedChatDisplay(
                    item.displayTitle,
                    item.displayAvatarFileId,
                    item.otherUserId
                )
            }
            cacheScope?.let { scope ->
                displayItems.forEach { item ->
                    runCatching {
                        chatCacheRepository.saveDisplay(
                            scope,
                            item.chatData.id,
                            CachedChatDisplay(item.displayTitle, item.displayAvatarFileId, item.otherUserId)
                        )
                    }
                }
            }
            chatAdapter.submitList(displayItems)
            showEmptyState(displayItems.isEmpty())
        }
    }

    // Job для отложенного показа "Соединение..."
    private var connectionCheckJob: Job? = null

    private fun subscribeToRealtimeEvents() {
        // Подписка на новые сообщения
        viewLifecycleOwner.lifecycleScope.launch {
            chatCacheRepository.clearedEvents.collect {
                allChats = emptyList()
                folders = emptyList()
                cachedDisplays = emptyMap()
                totalChatsCount = 0
                binding.foldersRecyclerView.visibility = View.GONE
                chatAdapter.submitList(emptyList())
                publishMainUnread()
                showSkeleton()
            }
        }
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
        viewLifecycleOwner.lifecycleScope.launch {
            realtimeService.privateMessages.collect { event ->
                handlePrivateMessage(event.chatId, event.message.senderId, event.message.sentAt.seconds * 1000)
            }
        }
        viewLifecycleOwner.lifecycleScope.launch {
            realtimeService.privateMessagesRead.collect { event ->
                if (event.userId == globalParam.userId) {
                    mirrorReadInAllChats(event.chatId)
                    refreshFolderTabs()
                    applyFolderFilter()
                }
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
                        isRealtimeReconnecting = false
                        updateToolbarTitle()
                    }
                    RealtimeService.ConnectionState.CONNECTING,
                    RealtimeService.ConnectionState.DISCONNECTED -> {
                        // Показываем "Соединение..." только если не соединились в течение 1 секунды
                        if (connectionCheckJob == null) {
                            connectionCheckJob = launch {
                                delay(1000) // Ждём 1 секунду
                                isRealtimeReconnecting = true
                                updateToolbarTitle()
                            }
                        }
                    }
                    RealtimeService.ConnectionState.IDLE -> Unit
                }
            }
        }
    }

    private fun handleNewMessage(event: barkfluff.updates.UpdatesApiOuterClass.NewMessageEvent) {
        val msg = event.message

        // Зеркалим состояние в allChats для корректного подсчёта бейджей папок.
        mirrorNewMessageInAllChats(event.chatId, msg.senderId, msg.id, msg.content?.text ?: "", msg.sentAt.seconds * 1000)
        publishMainUnread()
        refreshFolderTabs()
        persistChatList()

        // Если активна папка — отфильтровать события по чатам в ней.
        val selectedFolder = folders.firstOrNull { it.folderId == selectedFolderId }
        if (selectedFolder != null && event.chatId !in selectedFolder.chatIds) {
            // Чат не в текущей папке — пропускаем UI-обновление.
            // При следующем переключении вкладки / pull-to-refresh всё подтянется.
            return
        }

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
        // Зеркалим обнуление непрочитанных текущего пользователя в allChats и пересчитываем бейджи папок.
        if (event.newReadByList.contains(globalParam.userId)) {
            mirrorReadInAllChats(event.chatId)
            refreshFolderTabs()
            publishMainUnread()
        }
        persistChatList()
    }

    private fun handlePrivateMessage(chatId: String, senderId: Long, sentAtMillis: Long) {
        val index = allChats.indexOfFirst { it.id == chatId }
        if (index < 0) {
            loadChats()
            return
        }
        val existing = allChats[index]
        val updated = existing.copy(
            lastActivityAt = sentAtMillis,
            countUnread = if (senderId == globalParam.userId) existing.countUnread else existing.countUnread + 1
        )
        allChats = allChats.toMutableList().also { it[index] = updated }
            .sortedByDescending { it.lastActivityAt }
        refreshFolderTabs()
        applyFolderFilter()
        persistChatList()
    }

    private fun mirrorNewMessageInAllChats(
        chatId: String,
        senderId: Long,
        messageId: Long,
        text: String,
        sentAtMillis: Long
    ) {
        val idx = allChats.indexOfFirst { it.id == chatId }
        if (idx < 0) {
            // Новый чат — добавим минимальный объект, бейдж пересчитается корректно.
            val isOwn = senderId == globalParam.userId
            val newChat = GrpcManager.ChatData(
                id = chatId,
                title = "",
                picture = "",
                pictureFileId = "",
                isGroupChat = false,
                lastMessage = GrpcManager.LastMessageData(
                    id = messageId,
                    senderId = senderId,
                    text = text,
                    sentAt = sentAtMillis,
                    readBy = listOf(senderId)
                ),
                memberIds = listOf(globalParam.userId, senderId),
                countUnread = if (isOwn) 0L else 1L,
                firstUnreadMessageId = if (isOwn) 0L else messageId
            )
            allChats = (allChats + newChat).sortedByDescending { it.lastActivityAt }
            return
        }
        val existing = allChats[idx]
        val isOwn = senderId == globalParam.userId
        val updated = existing.copy(
            lastMessage = GrpcManager.LastMessageData(
                id = messageId,
                senderId = senderId,
                text = text,
                sentAt = sentAtMillis,
                readBy = listOf(senderId)
            ),
            countUnread = if (isOwn) existing.countUnread else existing.countUnread + 1
        )
        allChats = allChats.toMutableList().also { it[idx] = updated }
            .sortedByDescending { it.lastActivityAt }
    }

    private fun mirrorReadInAllChats(chatId: String) {
        val idx = allChats.indexOfFirst { it.id == chatId }
        if (idx < 0) return
        val existing = allChats[idx]
        if (existing.countUnread == 0L) return
        val updated = existing.copy(countUnread = 0L)
        allChats = allChats.toMutableList().also { it[idx] = updated }
    }
    private fun persistChatList() {
        cacheScope?.let { scope ->
            viewLifecycleOwner.lifecycleScope.launch {
                runCatching { chatCacheRepository.saveChatPage(scope, allChats, totalChatsCount) }
                    .onFailure { Log.w(TAG, "Не удалось обновить кеш чатов", it) }
            }
        }
    }

    fun scrollToTop() {
        if (_binding != null) binding.chatRecyclerView.smoothScrollToPosition(0)
    }

    private suspend fun resolveDisplayItem(chat: GrpcManager.ChatData): ChatAdapter.ChatDisplayItem {

        if (!hasAppliedRemoteChats) {
            cachedDisplays[chat.id]?.let { display ->
                return ChatAdapter.ChatDisplayItem(
                    chatData = chat,
                    displayTitle = display.title,
                    displayAvatarFileId = display.avatarFileId,
                    otherUserId = display.otherUserId
                )
            }
        }
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
            displayTitle = chat.title.ifBlank { getString(R.string.chat_title_default) },
            displayAvatarFileId = chatPictureFileId.ifBlank { null }
        )
    }

    private fun onChatClicked(chat: GrpcManager.ChatData) {
        // Находим display item для получения дополнительной информации
        val displayItem = chatAdapter.currentList.find { !it.isFooter && it.chatData.id == chat.id }

        if (chat.chatType == barkfluff.shared.Shared.ChatType.CHAT_TYPE_PRIVATE) {
            startActivity(ChatActivity.privateChatIntent(
                requireContext(),
                chatId = chat.id,
                title = displayItem?.displayTitle ?: chat.title.ifBlank { getString(R.string.create_chat_private) },
                inviteState = chat.privateInviteState.number,
                inviterUserId = chat.privateInviterUserId
            ))
            return
        }

        val intent = Intent(requireContext(), ChatActivity::class.java).apply {
            putExtra("chat_id", chat.id)
            putExtra("chat_title", displayItem?.displayTitle ?: chat.title.ifBlank { getString(R.string.chat_title_default) })
            putExtra("chat_avatar_file_id", displayItem?.displayAvatarFileId ?: chat.pictureFileId.ifBlank { null })
            putExtra("is_group_chat", chat.isGroupChat)
            putExtra("other_user_id", displayItem?.otherUserId ?: 0L)
        }

        // Устанавливаем чат как открытый перед запуском Activity
        OpenChatManager.setOpenChat(chat.id)

        startActivity(intent)
    }

    private fun showSkeleton() {
        binding.loadingIndicator.visibility = View.GONE
        binding.emptyState.visibility = View.GONE
        binding.chatRecyclerView.visibility = View.VISIBLE
        if (::skeletonAdapter.isInitialized && binding.chatRecyclerView.adapter !== skeletonAdapter) {
            binding.chatRecyclerView.adapter = skeletonAdapter
        }
    }

    private fun showSyncUpdating() {
        syncStatus = SyncStatus.UPDATING
        binding.toolbarRetryButton.visibility = View.GONE
        updateToolbarTitle()
    }

    private fun showSyncOffline() {
        syncStatus = SyncStatus.OFFLINE
        binding.toolbarRetryButton.visibility = View.VISIBLE
        updateToolbarTitle()
    }

    private fun hideSyncStatus() {
        syncStatus = null
        binding.toolbarRetryButton.visibility = View.GONE
        updateToolbarTitle()
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

    /**
     * Отправляет Firebase токен на сервер при каждом запуске.
     * Это нужно для получения push-уведомлений.
     */
    private fun sendFirebaseToken() {
        viewLifecycleOwner.lifecycleScope.launch {
            FirebaseTokenHelper.getTokenAndSendToServer(requireContext(), grpcManager)
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
