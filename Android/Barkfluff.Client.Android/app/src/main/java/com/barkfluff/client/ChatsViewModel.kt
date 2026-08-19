package com.barkfluff.client

import android.content.Context
import android.util.Log
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDisplay
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.utils.FirebaseTokenHelper
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

enum class ChatsSyncStatus { UPDATING, OFFLINE }

data class ChatsUiState(
    val allChats: List<GrpcManager.ChatData> = emptyList(),
    val folders: List<GrpcManager.ChatFolder> = emptyList(),
    val selectedFolderId: String? = null,
    val totalChatsCount: Int = 0,
    val displayItems: List<ChatAdapter.ChatDisplayItem> = emptyList(),
    val syncStatus: ChatsSyncStatus? = null,
    val isRealtimeReconnecting: Boolean = false,
    val unreadCount: Int = 0,
    /** false, пока нет ни кэша, ни серверного списка — UI показывает скелетон. */
    val contentAvailable: Boolean = false,
    val isLoadingNextPage: Boolean = false,
)

sealed interface ChatsEvent {
    data object NavigateToLogin : ChatsEvent
    /** Аватар/профиль могли подтянуться при первом входе — перерисовать шапку. */
    data object RefreshUserAvatar : ChatsEvent
    data class ChatLoadError(val message: String) : ChatsEvent
}

/**
 * Состояние списка чатов: домен (allChats/folders/пагинация), realtime-зеркалирование,
 * unread-счётчики и разрешение отображаемых имён/аватаров — в StateFlow. Fragment отвечает
 * только за рендер (DiffUtil в ChatAdapter) и жесты.
 */
@HiltViewModel
class ChatsViewModel @Inject constructor(
    @ApplicationContext private val appContext: Context,
    private val grpcManager: GrpcManager,
    private val realtimeService: RealtimeService,
    private val chatCacheRepository: ChatCacheRepository,
    private val chatDraftRepository: ChatDraftRepository,
) : ViewModel() {

    companion object {
        private const val TAG = "ChatsViewModel"
        private const val TOKEN_BUFFER_MINUTES = 5
    }

    private val _uiState = MutableStateFlow(ChatsUiState())
    val uiState: StateFlow<ChatsUiState> = _uiState.asStateFlow()

    private val _events = MutableSharedFlow<ChatsEvent>(extraBufferCapacity = 8)
    val events: SharedFlow<ChatsEvent> = _events.asSharedFlow()

    private val globalParam = GlobalParam(appContext)

    private var initialized = false
    private var cacheScope: CacheScope? = null
    private var hasAppliedRemoteChats = false
    private var cachedDisplays: Map<String, CachedChatDisplay> = emptyMap()
    private var localDraftStates: Map<String, Boolean> = emptyMap()
    private var loadChatsJob: Job? = null
    private var refreshDisplayJob: Job? = null
    private var connectionCheckJob: Job? = null

    /** Идемпотентная инициализация: подписки + первая загрузка. */
    fun initialize() {
        if (initialized) return
        initialized = true
        cacheScope = CacheScope.from(globalParam)

        subscribeToRealtimeEvents()

        viewModelScope.launch {
            chatDraftRepository.drafts.collect { drafts ->
                localDraftStates = drafts.mapValues { it.value.isActive }
                applyFolderFilter()
            }
        }
        viewModelScope.launch { chatDraftRepository.loadLocal() }

        hydrateChatsFromCache()
        checkTokenAndLoadChats()
    }

    // ═══════════════════════════════════════════════════════════════
    // Загрузка
    // ═══════════════════════════════════════════════════════════════

    private fun hydrateChatsFromCache() {
        val scope = cacheScope ?: return
        viewModelScope.launch {
            val snapshot = runCatching { chatCacheRepository.readChatList(scope) }.getOrNull()
                ?: return@launch
            if (hasAppliedRemoteChats) return@launch

            _uiState.value = _uiState.value.copy(
                allChats = snapshot.chats,
                folders = snapshot.folders,
                totalChatsCount = snapshot.totalCount,
                contentAvailable = true,
            )
            cachedDisplays = snapshot.displays
            realtimeService.changeOnlineSubscription(snapshot.chats.flatMap { it.memberIds }.distinct())
            applyFolderFilter()
        }
    }

    /** Кнопка «повторить» в шапке: полный проход проверки токена + загрузка. */
    fun retry() {
        checkTokenAndLoadChats()
    }

    private fun checkTokenAndLoadChats() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.UPDATING)
            val hasRefreshToken = globalParam.refreshToken != null
            val hasAccessToken = globalParam.accessToken != null
            val isAccessTokenExpired = isAccessTokenExpired()

            when {
                !hasRefreshToken -> {
                    _events.tryEmit(ChatsEvent.NavigateToLogin)
                    return@launch
                }
                hasRefreshToken && (!hasAccessToken || isAccessTokenExpired) -> {
                    val refreshResult = tryRefreshToken()
                    if (!refreshResult) {
                        _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.OFFLINE)
                        return@launch
                    }
                }
            }

            grpcManager.initAllClients(appContext, globalParam)

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
                    }
                }
            }

            _events.tryEmit(ChatsEvent.RefreshUserAvatar)
            loadChats()
            sendFirebaseToken()
        }
    }

    fun loadChats() {
        loadChatsJob?.cancel()
        loadChatsJob = viewModelScope.launch {
            _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.UPDATING)

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
                val state = _uiState.value
                val merged = (state.allChats + refreshedChats).associateBy { it.id }.values
                    .sortedByDescending { it.lastActivityAt }
                val newFolders = if (foldersResult.isSuccess) foldersResult.getOrNull() ?: emptyList() else {
                    Log.w(TAG, "Не удалось загрузить папки, продолжаем с пустым списком", foldersResult.exceptionOrNull())
                    state.folders
                }
                // Если выбранная папка пропала — сбрасываем на «Все чаты»
                val selectedFolder = if (state.selectedFolderId != null && newFolders.none { it.folderId == state.selectedFolderId }) {
                    null
                } else {
                    state.selectedFolderId
                }
                _uiState.value = state.copy(
                    allChats = merged,
                    folders = newFolders,
                    totalChatsCount = page.totalCount,
                    selectedFolderId = selectedFolder,
                    syncStatus = null,
                )
                Log.d(TAG, "Загружено ${merged.size} из ${page.totalCount} чатов")

                realtimeService.changeOnlineSubscription(merged.flatMap { it.memberIds }.distinct())
                cacheScope?.let { scope ->
                    runCatching {
                        chatCacheRepository.saveChatPage(
                            scope = scope,
                            chats = refreshedChats,
                            totalCount = page.totalCount,
                            folders = foldersResult.getOrNull()
                        )
                    }.onFailure { Log.w(TAG, "Не удалось сохранить кеш чатов", it) }
                }
                applyFolderFilter()
            } else {
                Log.e(TAG, "Ошибка загрузки чатов", chatsResult.exceptionOrNull())
                _events.tryEmit(
                    ChatsEvent.ChatLoadError(chatsResult.exceptionOrNull()?.message.orEmpty())
                )
                _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.OFFLINE)
            }
        }
    }

    fun loadNextPage() {
        val state = _uiState.value
        if (state.isLoadingNextPage || state.allChats.size >= state.totalChatsCount) return
        _uiState.value = state.copy(isLoadingNextPage = true)
        viewModelScope.launch {
            grpcManager.getChatsPage(offset = _uiState.value.allChats.size).onSuccess { page ->
                val merged = (_uiState.value.allChats + page.chats).associateBy { it.id }.values
                    .sortedByDescending { it.lastActivityAt }
                _uiState.value = _uiState.value.copy(
                    allChats = merged,
                    totalChatsCount = page.totalCount,
                )
                cacheScope?.let { scope ->
                    runCatching { chatCacheRepository.saveChatPage(scope, page.chats, page.totalCount) }
                }
                realtimeService.changeOnlineSubscription(merged.flatMap { it.memberIds }.distinct())
                applyFolderFilter()
            }.onFailure {
                Log.w(TAG, "Не удалось загрузить следующую страницу чатов", it)
            }
            _uiState.value = _uiState.value.copy(isLoadingNextPage = false)
        }
    }

    /** Возврат из фона: realtime-события терялись — проверяем токен и перезагружаем список. */
    fun reloadFromBackground() {
        if (grpcManager.messagesClient == null) return
        Log.d(TAG, "reloadFromBackground: checking token and reloading chats")
        viewModelScope.launch {
            val tokenValid = grpcManager.ensureTokenValid(appContext)
            if (!tokenValid) {
                Log.w(TAG, "reloadFromBackground: Token refresh failed, keeping cached chats")
                _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.OFFLINE)
                return@launch
            }
            loadChats()
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Папки и отображаемый список
    // ═══════════════════════════════════════════════════════════════

    fun selectFolder(folderId: String?) {
        val state = _uiState.value
        if (state.selectedFolderId == folderId) return
        _uiState.value = state.copy(selectedFolderId = folderId)
        applyFolderFilter()
    }

    /** Пересчитывает отображаемый список (фильтр папки + черновики + разрешение имён). */
    private fun applyFolderFilter() {
        refreshDisplayJob?.cancel()
        refreshDisplayJob = viewModelScope.launch {
            val state = _uiState.value
            val filtered: List<GrpcManager.ChatData> = if (state.selectedFolderId == null) {
                if (globalParam.excludeFolderChatsFromAll && state.folders.isNotEmpty()) {
                    val inFolders = chatsInUserFolders()
                    state.allChats.filter { it.id !in inFolders }
                } else {
                    state.allChats
                }
            } else {
                val folder = state.folders.firstOrNull { it.folderId == state.selectedFolderId }
                if (folder == null) state.allChats
                else {
                    val ids = folder.chatIds.toSet()
                    state.allChats.filter { it.id in ids }
                }
            }.map { chat ->
                localDraftStates[chat.id]?.let { chat.copy(hasDraft = it) } ?: chat
            }.sortedByDescending { it.lastActivityAt }

            val displayItems = filtered.map { resolveDisplayItem(it) }
            _uiState.value = _uiState.value.copy(
                displayItems = displayItems,
                unreadCount = totalUnread(_uiState.value.allChats),
            )
        }
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
        return ChatAdapter.ChatDisplayItem(
            chatData = chat,
            displayTitle = chat.title.ifBlank { appContext.getString(R.string.chat_title_default) },
            displayAvatarFileId = chat.pictureFileId.ifBlank { null }
        )
    }

    private fun chatsInUserFolders(): Set<String> {
        val folders = _uiState.value.folders
        if (folders.isEmpty()) return emptySet()
        val s = HashSet<String>()
        for (f in folders) s.addAll(f.chatIds)
        return s
    }

    private fun totalUnread(chats: List<GrpcManager.ChatData>): Int =
        chats.fold(0L) { total, chat ->
            (total + chat.countUnread).coerceAtMost(Int.MAX_VALUE.toLong())
        }.toInt()

    // ═══════════════════════════════════════════════════════════════
    // Realtime
    // ═══════════════════════════════════════════════════════════════

    private fun subscribeToRealtimeEvents() {
        viewModelScope.launch {
            chatCacheRepository.clearedEvents.collect {
                hasAppliedRemoteChats = false
                cachedDisplays = emptyMap()
                _uiState.value = ChatsUiState()
            }
        }
        viewModelScope.launch {
            realtimeService.newMessages.collect { event ->
                handleNewMessage(event)
            }
        }
        viewModelScope.launch {
            realtimeService.messagesRead.collect { event ->
                handleMessageRead(event)
            }
        }
        viewModelScope.launch {
            realtimeService.privateMessages.collect { event ->
                handlePrivateMessage(event.chatId, event.message.senderId, event.message.sentAt.seconds * 1000)
            }
        }
        viewModelScope.launch {
            realtimeService.privateMessagesRead.collect { event ->
                if (event.userId == globalParam.userId) {
                    mirrorReadInAllChats(event.chatId)
                    applyFolderFilter()
                }
            }
        }
        // Состояние соединения: «Соединение...» показываем только если не соединились за 1 секунду.
        viewModelScope.launch {
            realtimeService.connectionState.collect { state ->
                when (state) {
                    RealtimeService.ConnectionState.CONNECTED -> {
                        connectionCheckJob?.cancel()
                        connectionCheckJob = null
                        _uiState.value = _uiState.value.copy(isRealtimeReconnecting = false)
                    }
                    RealtimeService.ConnectionState.CONNECTING,
                    RealtimeService.ConnectionState.DISCONNECTED -> {
                        if (connectionCheckJob == null) {
                            connectionCheckJob = launch {
                                delay(1000)
                                _uiState.value = _uiState.value.copy(isRealtimeReconnecting = true)
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
        applyFolderFilter()
        persistChatList()
    }

    private fun handleMessageRead(event: barkfluff.updates.UpdatesApiOuterClass.MessageReadEvent) {
        // Зеркалим обнуление непрочитанных текущего пользователя в allChats.
        if (event.newReadByList.contains(globalParam.userId)) {
            mirrorReadInAllChats(event.chatId)
            applyFolderFilter()
        }
        persistChatList()
    }

    private fun handlePrivateMessage(chatId: String, senderId: Long, sentAtMillis: Long) {
        if (_uiState.value.allChats.none { it.id == chatId }) {
            loadChats()
            return
        }
        updateAllChats(chatId) { existing ->
            existing.copy(
                lastActivityAt = sentAtMillis,
                countUnread = if (senderId == globalParam.userId) existing.countUnread else existing.countUnread + 1
            )
        }
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
        val existing = _uiState.value.allChats
        val idx = existing.indexOfFirst { it.id == chatId }
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
            _uiState.value = _uiState.value.copy(
                allChats = (existing + newChat).sortedByDescending { it.lastActivityAt }
            )
            return
        }
        updateAllChats(chatId) { chat ->
            val isOwn = senderId == globalParam.userId
            chat.copy(
                lastMessage = GrpcManager.LastMessageData(
                    id = messageId,
                    senderId = senderId,
                    text = text,
                    sentAt = sentAtMillis,
                    readBy = listOf(senderId)
                ),
                countUnread = if (isOwn) chat.countUnread else chat.countUnread + 1
            )
        }
    }

    private fun mirrorReadInAllChats(chatId: String) {
        updateAllChats(chatId) { existing ->
            if (existing.countUnread == 0L) existing else existing.copy(countUnread = 0L)
        }
    }

    private fun updateAllChats(chatId: String, transform: (GrpcManager.ChatData) -> GrpcManager.ChatData) {
        val existing = _uiState.value.allChats
        val idx = existing.indexOfFirst { it.id == chatId }
        if (idx < 0) return
        _uiState.value = _uiState.value.copy(
            allChats = existing.toMutableList().also { it[idx] = transform(it[idx]) }
                .sortedByDescending { it.lastActivityAt }
        )
    }

    private fun persistChatList() {
        val scope = cacheScope ?: return
        val state = _uiState.value
        viewModelScope.launch {
            runCatching { chatCacheRepository.saveChatPage(scope, state.allChats, state.totalChatsCount) }
                .onFailure { Log.w(TAG, "Не удалось обновить кеш чатов", it) }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Токен
    // ═══════════════════════════════════════════════════════════════

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

            val createResult = grpcManager.createIdentityClient(identityAddress, appContext)
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

    /**
     * Отправляет Firebase токен на сервер при каждом запуске — нужно для push-уведомлений.
     */
    private fun sendFirebaseToken() {
        viewModelScope.launch {
            FirebaseTokenHelper.getTokenAndSendToServer(appContext, grpcManager)
        }
    }
}
