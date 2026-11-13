package com.barkfluff.client

import android.content.Context
import android.util.Log
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.shared.Shared
import com.barkfluff.client.adapter.ChatAdapter
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDisplay
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.grpc.TokenValidity
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.domain.gateway.ChatFolderGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.model.ChatFolder
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.domain.model.AccountStatus
import com.barkfluff.client.utils.FirebaseTokenHelper
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Job
import kotlinx.coroutines.CancellationException
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
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.UUID

enum class ChatsSyncStatus { UPDATING, OFFLINE }

data class ChatsUiState(
    val allChats: List<ChatSummary> = emptyList(),
    val folders: List<ChatFolder> = emptyList(),
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
    /** Identity rejected the refresh token: wipe account crypto state before Login. */
    data class SessionRejected(val validity: TokenValidity) : ChatsEvent
    /** Аватар/профиль могли подтянуться при первом входе — перерисовать шапку. */
    data object RefreshUserAvatar : ChatsEvent
    data class ChatLoadError(val message: String) : ChatsEvent
}

internal fun validChatSummaries(chats: List<ChatSummary>): List<ChatSummary> =
    chats.filter { chat -> runCatching { UUID.fromString(chat.id) }.isSuccess }

internal val chatSummaryOrder: Comparator<ChatSummary> =
    compareByDescending<ChatSummary> { it.lastActivityAt }
        .thenByDescending { it.lastMessage?.id ?: 0L }
        .thenBy { it.id }

internal fun sortChatSummaries(chats: Iterable<ChatSummary>): List<ChatSummary> =
    chats.sortedWith(chatSummaryOrder)

internal fun reconcileChatList(
    cachedChats: List<ChatSummary>,
    remoteChats: List<ChatSummary>,
    isCompleteSnapshot: Boolean,
): List<ChatSummary> {
    val validRemoteChats = validChatSummaries(remoteChats)
    val candidates = if (isCompleteSnapshot) {
        validRemoteChats
    } else {
        // Сервер отдаёт страницы по убыванию активности: кешированный чат новее последнего
        // загруженного, но отсутствующий в ответе, скрыт или удалён (DeleteChat).
        val remoteIds = validRemoteChats.mapTo(HashSet()) { it.id }
        val oldestLoaded = validRemoteChats.minOfOrNull { it.lastActivityAt }
        validChatSummaries(cachedChats).filter { cached ->
            cached.id in remoteIds || oldestLoaded == null || cached.lastActivityAt < oldestLoaded
        } + validRemoteChats
    }
    return sortChatSummaries(candidates.associateBy { it.id }.values)
}

internal sealed interface ChatListRealtimeEvent {
    val chatId: String

    data class NewMessage(
        override val chatId: String,
        val senderId: Long,
        val messageId: Long,
        val text: String,
        val sentAtMillis: Long,
    ) : ChatListRealtimeEvent

    data class PrivateMessage(
        override val chatId: String,
        val senderId: Long,
        val messageId: Long,
        val sentAtMillis: Long,
    ) : ChatListRealtimeEvent

    data class Read(override val chatId: String, val throughMessageId: Long = 0L) : ChatListRealtimeEvent
    data class Hidden(override val chatId: String) : ChatListRealtimeEvent
}

internal class ChatListEventJournal(initialEvents: List<ChatListRealtimeEvent> = emptyList()) {
    private val events = initialEvents.toMutableList()

    fun record(event: ChatListRealtimeEvent) {
        events += event
    }

    fun snapshot(): List<ChatListRealtimeEvent> = events.toList()
}

internal fun mergeChatListSnapshot(
    cachedChats: List<ChatSummary>,
    remoteChats: List<ChatSummary>,
    isCompleteSnapshot: Boolean,
    realtimeEvents: List<ChatListRealtimeEvent>,
    currentUserId: Long,
): List<ChatSummary> = applyChatListRealtimeEvents(
    reconcileChatList(cachedChats, remoteChats, isCompleteSnapshot),
    realtimeEvents,
    currentUserId,
)

internal fun applyChatListRealtimeEvents(
    initialChats: List<ChatSummary>,
    events: List<ChatListRealtimeEvent>,
    currentUserId: Long,
): List<ChatSummary> {
    val chats = initialChats.associateBy { it.id }.toMutableMap()
    val hiddenChatIds = mutableSetOf<String>()
    val readThroughByChat = mutableMapOf<String, Long>()
    val seenMessageIds = mutableMapOf<String, MutableSet<Long>>()

    events.forEach { event ->
        when (event) {
            is ChatListRealtimeEvent.Hidden -> {
                hiddenChatIds += event.chatId
                chats.remove(event.chatId)
            }

            is ChatListRealtimeEvent.Read -> {
                val chat = chats[event.chatId]
                if (chat == null || event.throughMessageId <= 0L ||
                    chat.firstUnreadMessageId <= 0L || chat.firstUnreadMessageId <= event.throughMessageId
                ) {
                    readThroughByChat[event.chatId] = maxOf(
                        readThroughByChat[event.chatId] ?: 0L,
                        event.throughMessageId,
                    )
                    if (chat != null) {
                        chats[event.chatId] = chat.copy(countUnread = 0L, firstUnreadMessageId = 0L)
                    }
                }
            }

            is ChatListRealtimeEvent.NewMessage -> {
                if (event.chatId in hiddenChatIds) return@forEach

                val chat = chats[event.chatId] ?: ChatSummary(
                    id = event.chatId,
                    title = "",
                    picture = "",
                    isGroupChat = false,
                    lastMessage = null,
                    memberIds = listOf(currentUserId, event.senderId).distinct(),
                    countUnread = 0L,
                    firstUnreadMessageId = 0L,
                )
                val previousMessageId = chat.lastMessage?.id ?: 0L
                val isNewer = event.messageId > previousMessageId || event.sentAtMillis > chat.lastActivityAt
                val messageIsNew = event.messageId > previousMessageId &&
                    seenMessageIds.getOrPut(event.chatId) { mutableSetOf() }.add(event.messageId)
                val isIncoming = event.senderId != currentUserId
                val readThrough = readThroughByChat[event.chatId]
                val isAfterRead = readThrough != null && event.messageId > readThrough
                val unread = if (isIncoming && messageIsNew &&
                    (isAfterRead || (readThrough == null && isNewer))
                ) incrementUnread(chat.countUnread) else chat.countUnread
                val lastMessage = if (isNewer || chat.lastMessage == null) {
                    com.barkfluff.client.domain.model.LastMessageSummary(
                        id = event.messageId,
                        senderId = event.senderId,
                        text = event.text,
                        sentAt = event.sentAtMillis,
                        readBy = listOf(event.senderId),
                    )
                } else {
                    chat.lastMessage
                }
                chats[event.chatId] = chat.copy(
                    lastMessage = lastMessage,
                    lastActivityAt = maxOf(chat.lastActivityAt, event.sentAtMillis),
                    countUnread = unread,
                    firstUnreadMessageId = when {
                        unread == 0L -> 0L
                        chat.countUnread == 0L && isIncoming -> event.messageId
                        else -> chat.firstUnreadMessageId
                    },
                )
            }

            is ChatListRealtimeEvent.PrivateMessage -> {
                if (event.chatId in hiddenChatIds) return@forEach

                val chat = chats[event.chatId] ?: ChatSummary(
                    id = event.chatId,
                    title = "",
                    picture = "",
                    isGroupChat = false,
                    lastMessage = null,
                    memberIds = listOf(currentUserId, event.senderId).distinct(),
                    countUnread = 0L,
                    firstUnreadMessageId = 0L,
                    chatType = Shared.ChatType.CHAT_TYPE_PRIVATE,
                )
                val isIncoming = event.senderId != currentUserId
                val previousMessageId = chat.lastMessage?.id ?: 0L
                val isNewer = event.messageId > previousMessageId || event.sentAtMillis > chat.lastActivityAt
                val messageIsNew = event.messageId > previousMessageId &&
                    seenMessageIds.getOrPut(event.chatId) { mutableSetOf() }.add(event.messageId)
                val readThrough = readThroughByChat[event.chatId]
                val isAfterRead = readThrough != null && event.messageId > readThrough
                val unread = if (isIncoming && messageIsNew && isNewer &&
                    (readThrough == null || isAfterRead)
                ) {
                    incrementUnread(chat.countUnread)
                } else {
                    chat.countUnread
                }
                chats[event.chatId] = chat.copy(
                    lastMessage = if (isNewer || chat.lastMessage == null) {
                        com.barkfluff.client.domain.model.LastMessageSummary(
                            id = event.messageId,
                            senderId = event.senderId,
                            text = "",
                            sentAt = event.sentAtMillis,
                            readBy = listOf(event.senderId),
                        )
                    } else {
                        chat.lastMessage
                    },
                    lastActivityAt = maxOf(chat.lastActivityAt, event.sentAtMillis),
                    countUnread = unread,
                    firstUnreadMessageId = if (unread > 0L && chat.countUnread == 0L && isIncoming && messageIsNew) {
                        event.messageId
                    } else {
                        chat.firstUnreadMessageId
                    },
                )
            }
        }
    }

    return sortChatSummaries(chats.values.filterNot { it.id in hiddenChatIds })
}

private fun incrementUnread(count: Long): Long =
    if (count == Long.MAX_VALUE) count else count + 1L

internal fun resolveOtherUserId(
    memberIds: List<Long>,
    currentUserId: Long,
    cachedUserId: Long = 0L,
): Long? {
    if (currentUserId <= 0L) return null
    return memberIds.firstOrNull { it > 0L && it != currentUserId }
        ?: cachedUserId.takeIf { it > 0L && it != currentUserId }
}

internal class ChatDisplayResolver(
    private val loadUser: suspend (Long) -> Result<UserProfile>,
    private val saveDisplay: suspend (String, CachedChatDisplay) -> Unit,
    private val saveDisplays: suspend (Map<String, CachedChatDisplay>) -> Unit = { updates ->
        updates.forEach { (chatId, display) -> saveDisplay(chatId, display) }
    },
) {
    private val displays = linkedMapOf<String, CachedChatDisplay>()
    private val resolvedProfiles = mutableSetOf<String>()
    private val failedProfileLoads = mutableSetOf<String>()
    private val profilesInFlight = mutableSetOf<String>()

    fun loadCachedDisplays(cached: Map<String, CachedChatDisplay>, currentUserId: Long) {
        displays.clear()
        resolvedProfiles.clear()
        failedProfileLoads.clear()
        profilesInFlight.clear()
        cached.forEach { (chatId, display) ->
            displays[chatId] = display.copy(
                otherUserId = resolveOtherUserId(emptyList(), currentUserId, display.otherUserId) ?: 0L,
            )
        }
    }

    /** Ошибки профиля повторяются после следующего явного обновления списка. */
    fun beginChatListRefresh() {
        resolvedProfiles.clear()
        failedProfileLoads.clear()
    }

    suspend fun cacheChatList(chats: List<ChatSummary>, currentUserId: Long) {
        val updates = linkedMapOf<String, CachedChatDisplay>()
        chats.filterNot { it.isGroupChat }.forEach { chat ->
            val peerId = resolveOtherUserId(
                chat.memberIds,
                currentUserId,
                displays[chat.id]?.otherUserId ?: 0L,
            ) ?: 0L
            remember(
                chat.id,
                CachedChatDisplay(
                    title = chat.title,
                    avatarFileId = chatAvatarFileId(chat),
                    otherUserId = peerId,
                    accountStatus = displays[chat.id]?.accountStatus ?: AccountStatus.ACTIVE,
                ),
                currentUserId,
            )?.let { updates[chat.id] = it }
        }
        if (updates.isNotEmpty()) saveDisplays(updates)
    }

    fun resolveCached(
        chat: ChatSummary,
        currentUserId: Long,
        defaultTitle: String,
        blockedUserTitle: String,
        deletedUserTitle: String,
    ): ChatAdapter.ChatDisplayItem {
        if (chat.isGroupChat) {
            return ChatAdapter.ChatDisplayItem(
                chatData = chat,
                displayTitle = chat.title.ifBlank { defaultTitle },
                displayAvatarFileId = chatAvatarFileId(chat),
            )
        }

        val cached = displays[chat.id]
        val otherUserId = resolveOtherUserId(chat.memberIds, currentUserId, cached?.otherUserId ?: 0L)
        val restricted = cached?.accountStatus?.let { it != AccountStatus.ACTIVE } == true
        val title = when {
            restricted && cached?.accountStatus in setOf(AccountStatus.PENDING_DELETION, AccountStatus.DELETED) -> deletedUserTitle
            restricted -> blockedUserTitle
            else -> chat.title.ifBlank { cached?.title.orEmpty() }
        }
        val avatarFileId = if (restricted) null else chatAvatarFileId(chat).ifBlank { cached?.avatarFileId.orEmpty() }
        return ChatAdapter.ChatDisplayItem(
            chatData = chat,
            displayTitle = title.ifBlank { defaultTitle },
            displayAvatarFileId = avatarFileId?.ifBlank { null },
            otherUserId = otherUserId ?: 0L,
            accountStatus = cached?.accountStatus ?: AccountStatus.ACTIVE,
        )
    }

    fun resolveCachedChats(
        chats: List<ChatSummary>,
        currentUserId: Long,
        defaultTitle: String,
        blockedUserTitle: String,
        deletedUserTitle: String,
    ): List<ChatAdapter.ChatDisplayItem> = chats.map {
        resolveCached(it, currentUserId, defaultTitle, blockedUserTitle, deletedUserTitle)
    }

    suspend fun resolveMissingProfiles(chats: List<ChatSummary>, currentUserId: Long) {
        for (chat in chats) {
            if (chat.isGroupChat) continue
            val cached = displays[chat.id]

            val otherUserId = resolveOtherUserId(chat.memberIds, currentUserId, cached?.otherUserId ?: 0L)
                ?: continue
            if (chat.id in resolvedProfiles ||
                chat.id in failedProfileLoads || !profilesInFlight.add(chat.id)
            ) continue

            try {
                val result = try {
                    loadUser(otherUserId)
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (failure: Exception) {
                    Result.failure(failure)
                }
                val user = result.getOrNull()
                if (user == null) {
                    result.exceptionOrNull()?.let { if (it is CancellationException) throw it }
                    // Ошибка не становится отрицательным кешем: следующая загрузка списка повторит запрос.
                    failedProfileLoads += chat.id
                    continue
                }

                resolvedProfiles += chat.id
                val restricted = user.accountStatus != AccountStatus.ACTIVE
                val avatarFileId = if (restricted) null else {
                    user.profilePicturePreviewFileId.ifBlank { user.profilePictureFileId }.ifBlank { null }
                }
                val title = if (restricted) "" else "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
                remember(
                    chat.id,
                    CachedChatDisplay(title, avatarFileId, otherUserId, user.accountStatus),
                    currentUserId,
                )?.let { saveDisplay(chat.id, it) }
            } finally {
                profilesInFlight -= chat.id
            }
        }
    }

    private fun remember(chatId: String, candidate: CachedChatDisplay, currentUserId: Long): CachedChatDisplay? {
        val existing = displays[chatId]
        val restricted = candidate.accountStatus != AccountStatus.ACTIVE
        val display = CachedChatDisplay(
            title = if (restricted) "" else candidate.title.ifBlank { existing?.title.orEmpty() },
            avatarFileId = if (restricted) null else candidate.avatarFileId?.takeIf { it.isNotBlank() } ?: existing?.avatarFileId,
            otherUserId = resolveOtherUserId(emptyList(), currentUserId, candidate.otherUserId)
                ?: resolveOtherUserId(emptyList(), currentUserId, existing?.otherUserId ?: 0L)
                ?: 0L,
            accountStatus = candidate.accountStatus,
        )
        if (display != existing &&
            (display.title.isNotBlank() || display.avatarFileId != null || display.otherUserId > 0L)
        ) {
            displays[chatId] = display
            return display
        }
        return null
    }

    private fun chatAvatarFileId(chat: ChatSummary): String =
        chat.picturePreviewFileId.ifBlank { chat.pictureFileId }
}

/**
 * Состояние списка чатов: домен (allChats/folders/пагинация), realtime-зеркалирование,
 * unread-счётчики и разрешение отображаемых имён/аватаров — в StateFlow. Fragment отвечает
 * только за рендер (DiffUtil в ChatAdapter) и жесты.
 */
@HiltViewModel
class ChatsViewModel @Inject constructor(
    @ApplicationContext private val appContext: Context,
    private val authGateway: AuthGateway,
    private val userProfileGateway: UserProfileGateway,
    private val chatDirectoryGateway: ChatDirectoryGateway,
    private val chatFolderGateway: ChatFolderGateway,
    private val realtimeService: RealtimeService,
    private val chatCacheRepository: ChatCacheRepository,
    private val chatDraftRepository: ChatDraftRepository,
) : ViewModel() {

    companion object {
        private const val TAG = "ChatsViewModel"
    }

    private val _uiState = MutableStateFlow(ChatsUiState())
    val uiState: StateFlow<ChatsUiState> = _uiState.asStateFlow()

    private val _events = MutableSharedFlow<ChatsEvent>(extraBufferCapacity = 8)
    val events: SharedFlow<ChatsEvent> = _events.asSharedFlow()

    private val globalParam = GlobalParam(appContext)

    private var initialized = false
    private var cacheScope: CacheScope? = null
    private var hasAppliedRemoteChats = false
    private val displayResolver = ChatDisplayResolver(
        loadUser = userProfileGateway::user,
        saveDisplay = { chatId, display ->
            cacheScope?.let { scope ->
                runCatching { chatCacheRepository.saveDisplay(scope, chatId, display) }
                    .onFailure { Log.w(TAG, "Не удалось сохранить отображение чата", it) }
            }
        },
        saveDisplays = { displays ->
            cacheScope?.let { scope ->
                runCatching { chatCacheRepository.saveDisplays(scope, displays) }
                    .onFailure { Log.w(TAG, "Не удалось сохранить отображения чатов", it) }
            }
        },
    )
    private var localDraftStates: Map<String, Boolean> = emptyMap()
    private var loadChatsJob: Job? = null
    private var chatCacheHydrationJob: Job? = null
    private var refreshDisplayJob: Job? = null
    private var connectionCheckJob: Job? = null
    private var unknownChatRefreshJob: Job? = null
    private var activeChatListJournal: ChatListEventJournal? = null
    private val pendingChatListEvents = mutableListOf<ChatListRealtimeEvent>()
    private var pendingEventsBaseChats: List<ChatSummary>? = null
    private var hasSeenRealtimeConnection = false
    private var realtimeConnectionInterrupted = false
    private var pendingDisplayRefresh = false
    private val chatListCacheMutex = Mutex()

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
        chatCacheHydrationJob = viewModelScope.launch {
            val snapshot = runCatching { chatCacheRepository.readChatList(scope) }.getOrNull()
                ?: return@launch
            if (hasAppliedRemoteChats) return@launch

            val cachedChats = validChatSummaries(snapshot.chats)
            pendingEventsBaseChats = cachedChats
            val hydratedChats = applyChatListRealtimeEvents(
                cachedChats,
                pendingChatListEvents,
                globalParam.userId,
            )
            val hiddenCachedIds = pendingChatListEvents
                .filterIsInstance<ChatListRealtimeEvent.Hidden>()
                .mapTo(HashSet()) { it.chatId }
                .intersect(cachedChats.mapTo(HashSet()) { it.id })
            _uiState.value = _uiState.value.copy(
                allChats = hydratedChats,
                folders = snapshot.folders,
                totalChatsCount = (snapshot.totalCount - hiddenCachedIds.size).coerceAtLeast(0),
                contentAvailable = true,
            )
            displayResolver.loadCachedDisplays(snapshot.displays, globalParam.userId)
            realtimeService.changeOnlineSubscription(hydratedChats.flatMap { it.memberIds }.distinct())
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
            if (globalParam.refreshToken == null) {
                _events.tryEmit(ChatsEvent.NavigateToLogin)
                return@launch
            }
            if (!applyTokenValidity()) return@launch

            if (globalParam.pictureFileId.isBlank() && globalParam.picturePreviewFileId.isBlank()) {
                Log.d(TAG, "checkTokenAndLoadChats: Аватар не загружен, загружаем данные пользователя")
                val userDataResult = userProfileGateway.currentUser()
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
        val requestStartState = _uiState.value
        val requestBaseChats = pendingEventsBaseChats ?: requestStartState.allChats
        val journal = ChatListEventJournal(pendingChatListEvents)
        activeChatListJournal = journal
        displayResolver.beginChatListRefresh()
        loadChatsJob = viewModelScope.launch {
            chatCacheHydrationJob?.join()
            _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.UPDATING)
            try {
                // Параллельно: чаты и папки. Чаты критичны, папки — нет.
                val (chatsResult, foldersResult) = coroutineScope {
                    val chatsDeferred = async { chatDirectoryGateway.chats() }
                    val foldersDeferred = async { chatFolderGateway.folders() }
                    chatsDeferred.await() to foldersDeferred.await()
                }

                if (chatsResult.isSuccess) {
                    val page = chatsResult.getOrNull()!!
                    val refreshedChats = page.chats.toMutableList()
                    var nextOffset = refreshedChats.size
                    for (pageIndex in 1 until 3) {
                        if (nextOffset >= page.totalCount) break
                        val nextPage = chatDirectoryGateway.chats(offset = nextOffset).getOrNull() ?: break
                        refreshedChats += nextPage.chats
                        nextOffset += nextPage.chats.size
                        if (nextPage.chats.isEmpty()) break
                    }

                    val events = journal.snapshot()
                    if (activeChatListJournal !== journal) return@launch
                    val isCompleteSnapshot = refreshedChats.size >= page.totalCount
                    val remoteChats = validChatSummaries(refreshedChats)
                    val mergeBaseChats = pendingEventsBaseChats ?: requestBaseChats
                    val merged = mergeChatListSnapshot(
                        cachedChats = mergeBaseChats,
                        remoteChats = remoteChats,
                        isCompleteSnapshot = isCompleteSnapshot,
                        realtimeEvents = events,
                        currentUserId = globalParam.userId,
                    )
                    val state = _uiState.value
                    val newFolders = if (foldersResult.isSuccess) foldersResult.getOrNull() ?: emptyList() else {
                        Log.w(TAG, "Не удалось загрузить папки, продолжаем с прежним списком", foldersResult.exceptionOrNull())
                        state.folders
                    }
                    val selectedFolder = if (
                        state.selectedFolderId != null && newFolders.none { it.folderId == state.selectedFolderId }
                    ) null else state.selectedFolderId
                    val remoteIds = remoteChats.mapTo(HashSet()) { it.id }
                    val hiddenIds = events.filterIsInstance<ChatListRealtimeEvent.Hidden>().mapTo(HashSet()) { it.chatId }
                    val hiddenIdsStillInResponse = hiddenIds.intersect(remoteIds)
                    val totalCount = (page.totalCount - hiddenIdsStillInResponse.size).coerceAtLeast(0)
                    val removedChatIds = (mergeBaseChats.mapTo(HashSet()) { it.id } -
                        merged.mapTo(HashSet()) { it.id }) + hiddenIds

                    hasAppliedRemoteChats = true
                    _uiState.value = state.copy(
                        allChats = merged,
                        folders = newFolders,
                        totalChatsCount = totalCount,
                        selectedFolderId = selectedFolder,
                        contentAvailable = true,
                        syncStatus = null,
                    )
                    Log.d(TAG, "Загружено ${merged.size} из $totalCount чатов")

                    pendingChatListEvents.clear()
                    pendingEventsBaseChats = merged
                    activeChatListJournal = null
                    realtimeService.changeOnlineSubscription(merged.flatMap { it.memberIds }.distinct())
                    cacheScope?.let { scope ->
                        runCatching {
                            chatListCacheMutex.withLock {
                                val latest = _uiState.value
                                chatCacheRepository.saveChatPage(
                                    scope = scope,
                                    chats = latest.allChats,
                                    totalCount = latest.totalChatsCount,
                                    folders = latest.folders,
                                    replaceExisting = isCompleteSnapshot,
                                    removedChatIds = removedChatIds,
                                )
                            }
                        }.onFailure { Log.w(TAG, "Не удалось сохранить объединённый список чатов", it) }
                    }
                    displayResolver.cacheChatList(merged, globalParam.userId)
                    applyFolderFilter()
                } else {
                    Log.e(TAG, "Ошибка загрузки чатов", chatsResult.exceptionOrNull())
                    _events.tryEmit(
                        ChatsEvent.ChatLoadError(chatsResult.exceptionOrNull()?.message.orEmpty())
                    )
                    _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.OFFLINE)
                }
            } finally {
                if (activeChatListJournal === journal) activeChatListJournal = null
            }
        }
    }

    fun loadNextPage() {
        val state = _uiState.value
        if (state.isLoadingNextPage || state.allChats.size >= state.totalChatsCount) return
        _uiState.value = state.copy(isLoadingNextPage = true)
        viewModelScope.launch {
            chatDirectoryGateway.chats(offset = _uiState.value.allChats.size).onSuccess { page ->
                val currentChats = _uiState.value.allChats
                val currentIds = currentChats.mapTo(HashSet()) { it.id }
                val merged = sortChatSummaries(currentChats + page.chats.filterNot { it.id in currentIds })
                _uiState.value = _uiState.value.copy(
                    allChats = merged,
                    totalChatsCount = page.totalCount,
                )
                cacheScope?.let { scope ->
                    runCatching {
                        chatListCacheMutex.withLock {
                            val latest = _uiState.value
                            chatCacheRepository.saveChatPage(scope, latest.allChats, latest.totalChatsCount)
                        }
                    }.onFailure { Log.w(TAG, "Не удалось сохранить следующую страницу чатов", it) }
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
        if (globalParam.socketMessages.isBlank()) return
        Log.d(TAG, "reloadFromBackground: checking token and reloading chats")
        viewModelScope.launch {
            if (applyTokenValidity()) loadChats()
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
        val state = _uiState.value
        val filtered = filteredChats(state)
        _uiState.value = state.copy(
            displayItems = resolveCachedDisplayItems(filtered),
            unreadCount = totalUnread(state.allChats),
        )

        if (refreshDisplayJob?.isActive == true) {
            pendingDisplayRefresh = true
            return
        }

        refreshDisplayJob = viewModelScope.launch {
            do {
                pendingDisplayRefresh = false
                displayResolver.resolveMissingProfiles(filteredChats(_uiState.value), globalParam.userId)
                val latest = _uiState.value
                val latestFiltered = filteredChats(latest)
                _uiState.value = latest.copy(
                    displayItems = resolveCachedDisplayItems(latestFiltered),
                    unreadCount = totalUnread(latest.allChats),
                )
            } while (pendingDisplayRefresh)
        }
    }

    private fun filteredChats(state: ChatsUiState): List<ChatSummary> {
        val filtered = if (state.selectedFolderId == null) {
            if (globalParam.excludeFolderChatsFromAll && state.folders.isNotEmpty()) {
                val inFolders = state.folders.flatMapTo(HashSet()) { it.chatIds }
                state.allChats.filter { it.id !in inFolders }
            } else {
                state.allChats
            }
        } else {
            val folder = state.folders.firstOrNull { it.folderId == state.selectedFolderId }
            if (folder == null) state.allChats else {
                val ids = folder.chatIds.toSet()
                state.allChats.filter { it.id in ids }
            }
        }
        return sortChatSummaries(filtered.map { chat ->
            localDraftStates[chat.id]?.let { chat.copy(hasDraft = it) } ?: chat
        })
    }

    private fun resolveCachedDisplayItems(chats: List<ChatSummary>): List<ChatAdapter.ChatDisplayItem> =
        displayResolver.resolveCachedChats(
            chats,
            globalParam.userId,
            appContext.getString(R.string.chat_title_default),
            appContext.getString(R.string.account_blocked_user),
            appContext.getString(R.string.account_deleted_user),
        )

    private fun totalUnread(chats: List<ChatSummary>): Int =
        chats.fold(0L) { total, chat ->
            (total + chat.countUnread).coerceAtMost(Int.MAX_VALUE.toLong())
        }.toInt()

    // ═══════════════════════════════════════════════════════════════
    // Realtime
    // ═══════════════════════════════════════════════════════════════

    private fun subscribeToRealtimeEvents() {
        viewModelScope.launch {
            chatCacheRepository.clearedEvents.collect {
                loadChatsJob?.cancel()
                chatCacheHydrationJob?.cancel()
                chatCacheHydrationJob = null
                activeChatListJournal = null
                pendingChatListEvents.clear()
                pendingEventsBaseChats = null
                hasAppliedRemoteChats = false
                displayResolver.loadCachedDisplays(emptyMap(), globalParam.userId)
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
                handlePrivateMessage(
                    event.chatId,
                    event.message.senderId,
                    event.message.id,
                    event.message.sentAt.seconds * 1000,
                )
            }
        }
        viewModelScope.launch {
            realtimeService.chatHidden.collect { event ->
                handleChatHidden(event.chatId, event.contentWiped)
            }
        }
        // Папки изменились (в том числе на другом устройстве): перечитываем только папки.
        // collect последователен, а поток сигналов схлопывает повторы — пока идёт обновление,
        // в очереди остаётся не больше одного сигнала, и последнее изменение не теряется.
        viewModelScope.launch {
            realtimeService.chatFoldersChanged.collect { refreshFolders() }
        }
        viewModelScope.launch {
            realtimeService.privateMessagesRead.collect { event ->
                if (event.userId == globalParam.userId) {
                    recordAndApplyRealtimeEvent(
                        ChatListRealtimeEvent.Read(event.chatId, event.lastReadMessageId)
                    )
                    applyFolderFilter()
                    persistChatList()
                }
            }
        }
        // Состояние соединения: «Соединение...» показываем только если не соединились за 1 секунду.
        viewModelScope.launch {
            realtimeService.connectionState.collect { state ->
                when (state) {
                    RealtimeService.ConnectionState.CONNECTED -> {
                        val shouldReloadChats = hasSeenRealtimeConnection && realtimeConnectionInterrupted
                        hasSeenRealtimeConnection = true
                        realtimeConnectionInterrupted = false
                        connectionCheckJob?.cancel()
                        connectionCheckJob = null
                        _uiState.value = _uiState.value.copy(isRealtimeReconnecting = false)
                        if (shouldReloadChats) loadChats()
                    }
                    RealtimeService.ConnectionState.CONNECTING,
                    RealtimeService.ConnectionState.DISCONNECTED -> {
                        if (hasSeenRealtimeConnection) realtimeConnectionInterrupted = true
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
        val wasListed = _uiState.value.allChats.any { it.id == event.chatId }
        recordAndApplyRealtimeEvent(
            ChatListRealtimeEvent.NewMessage(
                chatId = event.chatId,
                senderId = msg.senderId,
                messageId = msg.id,
                text = msg.content?.text ?: "",
                sentAtMillis = msg.sentAt.seconds * 1000,
            )
        )
        if (!wasListed) scheduleChatListRefresh()
        applyFolderFilter()
        persistChatList()
    }

    private fun handleMessageRead(event: barkfluff.updates.UpdatesApiOuterClass.MessageReadEvent) {
        // Зеркалим обнуление непрочитанных текущего пользователя в allChats.
        if (event.newReadByList.contains(globalParam.userId)) {
            recordAndApplyRealtimeEvent(
                ChatListRealtimeEvent.Read(event.chatId, event.messageId)
            )
            applyFolderFilter()
        }
        persistChatList()
    }

    private fun handlePrivateMessage(chatId: String, senderId: Long, messageId: Long, sentAtMillis: Long) {
        val wasListed = _uiState.value.allChats.any { it.id == chatId }
        recordAndApplyRealtimeEvent(ChatListRealtimeEvent.PrivateMessage(chatId, senderId, messageId, sentAtMillis))
        if (!wasListed) scheduleChatListRefresh()
        applyFolderFilter()
        persistChatList()
    }

    /** DeleteChat на другом устройстве: чат исчезает из списка и кеша, как в веб-клиенте. */
    private suspend fun refreshFolders() {
        // loadChats тоже читает папки: даём ему завершиться, чтобы его более ранний
        // снимок не перезаписал свежий результат этого обновления.
        while (loadChatsJob?.isActive == true) loadChatsJob?.join()
        val folders = chatFolderGateway.folders().getOrElse {
            Log.w(TAG, "Не удалось обновить папки после сигнала об изменении", it)
            return
        }
        val state = _uiState.value
        if (folders == state.folders) return
        val selectedFolderId = state.selectedFolderId?.takeIf { id -> folders.any { it.folderId == id } }
        _uiState.value = state.copy(folders = folders, selectedFolderId = selectedFolderId)
        applyFolderFilter()
        persistChatList()
    }

    private fun handleChatHidden(chatId: String, contentWiped: Boolean) {
        val state = _uiState.value
        val wasListed = state.allChats.any { it.id == chatId }
        recordAndApplyRealtimeEvent(ChatListRealtimeEvent.Hidden(chatId))
        val totalCount = _uiState.value.totalChatsCount
        if (wasListed) applyFolderFilter()
        val scope = cacheScope ?: return
        viewModelScope.launch {
            runCatching {
                chatListCacheMutex.withLock {
                    chatCacheRepository.removeChat(scope, chatId, totalCount, contentWiped)
                }
            }
                .onFailure { Log.w(TAG, "Не удалось удалить чат из кеша", it) }
        }
    }

    private fun recordAndApplyRealtimeEvent(event: ChatListRealtimeEvent) {
        val state = _uiState.value
        if (pendingChatListEvents.isEmpty()) pendingEventsBaseChats = state.allChats
        pendingChatListEvents += event
        activeChatListJournal?.record(event)

        val wasListed = state.allChats.any { it.id == event.chatId }
        val updatedChats = applyChatListRealtimeEvents(state.allChats, listOf(event), globalParam.userId)
        val totalCount = if (event is ChatListRealtimeEvent.Hidden && wasListed) {
            (state.totalChatsCount - 1).coerceAtLeast(0)
        } else {
            state.totalChatsCount
        }
        _uiState.value = state.copy(allChats = updatedChats, totalChatsCount = totalCount)
    }

    private fun scheduleChatListRefresh() {
        if (unknownChatRefreshJob?.isActive == true) return
        unknownChatRefreshJob = viewModelScope.launch {
            delay(250)
            while (loadChatsJob?.isActive == true) loadChatsJob?.join()
            loadChats()
        }
    }

    private fun persistChatList() {
        val scope = cacheScope ?: return
        viewModelScope.launch {
            runCatching {
                chatListCacheMutex.withLock {
                    val state = _uiState.value
                    chatCacheRepository.saveChatPage(
                        scope = scope,
                        chats = state.allChats,
                        totalCount = state.totalChatsCount,
                        folders = state.folders,
                        preserveCachedDisplayWhenEmpty = true,
                    )
                }
            }
                .onFailure { Log.w(TAG, "Не удалось обновить кеш чатов", it) }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Токен
    // ═══════════════════════════════════════════════════════════════

    /**
     * Обновляет токен через общий TokenCoordinator. Отвергнутая Identity сессия (refresh токен
     * истёк или устройство отвязано) ведёт на логин — иначе список вечно висит на кеше,
     * а стримы бесконечно получают 401. Недоступность Identity — только статус offline.
     */
    private suspend fun applyTokenValidity(): Boolean {
        val validity = authGateway.tokenValidity()
        return when (validity) {
            TokenValidity.VALID -> true
            TokenValidity.REJECTED,
            TokenValidity.ACCOUNT_BLOCKED,
            TokenValidity.ACCOUNT_PENDING_DELETION -> {
                Log.w(TAG, "Refresh token rejected, navigating to login")
                _events.tryEmit(ChatsEvent.SessionRejected(validity))
                false
            }
            TokenValidity.UNAVAILABLE -> {
                _uiState.value = _uiState.value.copy(syncStatus = ChatsSyncStatus.OFFLINE)
                false
            }
        }
    }

    /**
     * Отправляет Firebase токен на сервер при каждом запуске — нужно для push-уведомлений.
     */
    private fun sendFirebaseToken() {
        viewModelScope.launch {
            FirebaseTokenHelper.getTokenAndSendToServer(appContext, userProfileGateway)
        }
    }
}
