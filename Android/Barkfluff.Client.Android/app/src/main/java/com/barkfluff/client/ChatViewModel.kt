package com.barkfluff.client

import android.content.Context
import android.util.Log
import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.shared.Shared
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.adapter.ReadStatus
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.send.MediaSendService
import com.barkfluff.client.send.UploadState
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import javax.inject.Inject
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout

/**
 * Активный ответ (reply): messageId оригинала + данные для превью-бара.
 */
data class PendingReply(
    val messageId: Long,
    val senderId: Long,
    val senderName: String?,
    val text: String,
    val attachments: List<Shared.MessageAttachment>,
)

/**
 * Активное редактирование: messageId + исходный текст и file_id существующих вложений
 * (передаются в EditMessage без изменений).
 */
data class PendingEdit(
    val messageId: Long,
    val text: String,
    val fileIds: List<String>,
)

data class ChatUiState(
    val chatTitle: String = "",
    val chatAvatarFileId: String? = null,
    val isGroupChat: Boolean = false,
    val isChatMuted: Boolean = false,
    val otherUserId: Long = 0L,
    val items: List<MessageItem> = emptyList(),
    val isLoading: Boolean = false,
    val pendingReply: PendingReply? = null,
    val pendingEdit: PendingEdit? = null,
    /** Количество закреплённых сообщений (0 = бар скрыт). */
    val pinnedCount: Int = 0,
    /** Текст-превью первого закреплённого сообщения. */
    val pinnedPreview: String = "",
    /** MessageId первого закрепа (для скролла по клику на бар). */
    val firstPinnedMessageId: Long = 0L,
)

/** Одноразовые события для UI (тосты, завершение экрана, восстановление черновика). */
sealed interface ChatEvent {
    data class ToastRes(val resId: Int, val formatArg: String? = null) : ChatEvent
    data class DraftRestored(val text: String) : ChatEvent
    data object FinishActivity : ChatEvent
}

/**
 * Состояние экрана чата, переживающее пересоздание Activity (recreate при смене chatId
 * очищает ViewModelStore, поэтому каждый чат получает свежий VM). Владеет списком сообщений
 * (включая разделители дат/непрочитанных и оптимистичные плейсхолдеры), пагинацией,
 * realtime-событиями, закрепами и режимами reply/edit. Process death восстанавливается
 * через SavedStateHandle (pendingReply/pendingEdit) + Room-кэш истории + черновики.
 */
@HiltViewModel
class ChatViewModel @Inject constructor(
    @ApplicationContext private val appContext: Context,
    private val grpcManager: GrpcManager,
    private val realtimeService: RealtimeService,
    private val chatRepository: ChatRepository,
    private val chatCacheRepository: ChatCacheRepository,
    private val chatDraftRepository: ChatDraftRepository,
    private val savedStateHandle: SavedStateHandle,
) : ViewModel() {

    companion object {
        private const val TAG = "ChatViewModel"
        private const val PAGE_SIZE = 30
        private const val KEY_PENDING_REPLY = "pending_reply_message_id"
        private const val KEY_PENDING_EDIT = "pending_edit_message_id"
        private const val KEY_CHAT_ID = "chat_id"
    }

    private val _uiState = MutableStateFlow(ChatUiState())
    val uiState: StateFlow<ChatUiState> = _uiState.asStateFlow()

    private val _events = MutableSharedFlow<ChatEvent>(extraBufferCapacity = 16)
    val events: SharedFlow<ChatEvent> = _events.asSharedFlow()

    private val globalParam = GlobalParam(appContext)
    private val currentUserId = globalParam.userId

    private var initialized = false
    private var chatId: String = ""
    private var supportsDrafts = false
    private var cacheScope: CacheScope? = null

    // Пагинация
    private var firstVisibleMessageId = 0L
    private var lastVisibleMessageId = 0L
    private var hasMoreMessagesUp = true
    private var hasMoreMessagesDown = true
    private var isLoadingMessages = false
    private var loadMessagesJob: Job? = null
    private var firstUnreadMessageId = 0L
    private var lastBottomReadTriggerId = -1L

    // Черновик
    private var draftSaveJob: Job? = null
    private var draftRestored = false
    private var isRestoringDraft = false

    // Закреплённые сообщения
    private val pinnedById = mutableMapOf<Long, Shared.PinnedMessageInfo>()
    private val pinnedSorted = mutableListOf<Shared.PinnedMessageInfo>()
    private var pinnedTotalCount = 0

    /**
     * Идемпотентная инициализация под конкретный чат. При recreate() того же чата
     * состояние уже живо и перезагрузка не выполняется.
     */
    fun initialize(
        chatId: String,
        title: String,
        avatarFileId: String?,
        isGroupChat: Boolean,
        otherUserId: Long,
        supportsDrafts: Boolean,
    ) {
        if (initialized) {
            if (this.chatId != chatId) {
                Log.w(TAG, "initialize: VM уже привязан к ${this.chatId}, игнорируем $chatId")
            }
            return
        }
        initialized = true
        this.chatId = chatId
        this.supportsDrafts = supportsDrafts
        cacheScope = CacheScope.from(globalParam)
        savedStateHandle[KEY_CHAT_ID] = chatId

        _uiState.value = ChatUiState(
            chatTitle = title,
            chatAvatarFileId = avatarFileId,
            isGroupChat = isGroupChat,
            otherUserId = otherUserId,
        )

        restoreFromSavedState()
        subscribeToRealtimeEvents()
        loadChatInfoAndMessages()
        loadPinnedMessages()
        restoreDraft()
    }

    // ═══════════════════════════════════════════════════════════════
    // Загрузка сообщений и пагинация
    // ═══════════════════════════════════════════════════════════════

    private fun loadCachedMessages() {
        val scope = cacheScope ?: return
        viewModelScope.launch {
            val messages = runCatching {
                chatCacheRepository.latestMessages(scope, chatId, limit = PAGE_SIZE)
            }.getOrNull().orEmpty()
            if (messages.isEmpty()) return@launch

            displayMessages(messages)
            val sortedMessages = messages.sortedBy { it.sentAt.seconds }
            firstVisibleMessageId = sortedMessages.first().id
            lastVisibleMessageId = sortedMessages.last().id
            hasMoreMessagesUp = messages.size >= PAGE_SIZE
            hasMoreMessagesDown = false
            setLoading(false)
        }
    }

    private fun loadChatInfoAndMessages() {
        loadCachedMessages()
        setLoading(true)

        viewModelScope.launch {
            try {
                val chatInfoResult = chatRepository.getChatInfo(chatId)
                if (chatInfoResult.isSuccess) {
                    val chatInfo = chatInfoResult.getOrNull()!!
                    val state = _uiState.value
                    _uiState.value = state.copy(
                        chatTitle = if (chatInfo.title.isNotBlank()) chatInfo.title else state.chatTitle,
                        chatAvatarFileId = if (chatInfo.pictureFileId.isNotBlank()) chatInfo.pictureFileId else state.chatAvatarFileId,
                        isGroupChat = chatInfo.isGroupChat,
                        isChatMuted = chatInfo.muted,
                    )
                    firstUnreadMessageId = chatInfo.firstUnreadMessageId
                    globalParam.setChatMutedLocal(chatId, chatInfo.muted)

                    // Определяем otherUserId из участников чата (если не был передан через intent)
                    if (!chatInfo.isGroupChat && _uiState.value.otherUserId == 0L && chatInfo.memberIds.isNotEmpty()) {
                        val other = chatInfo.memberIds.firstOrNull { it != currentUserId } ?: 0L
                        if (other > 0) {
                            _uiState.value = _uiState.value.copy(otherUserId = other)
                        }
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading chat info", e)
            }

            loadMessages()
        }
    }

    fun loadMessages(isRetry: Boolean = false) {
        loadMessagesJob = viewModelScope.launch {
            try {
                val result = if (firstUnreadMessageId > 0) {
                    chatRepository.loadMessages(
                        chatId = chatId,
                        fromMessageId = firstUnreadMessageId,
                        offsetBefore = 15,
                        offsetAfter = 30
                    )
                } else {
                    chatRepository.loadMessages(
                        chatId = chatId,
                        fromMessageId = 0L,
                        offsetBefore = 0,
                        offsetAfter = 0,
                        count = PAGE_SIZE
                    )
                }

                if (result.isSuccess) {
                    val messages = result.getOrNull()!!
                    cacheScope?.let { scope ->
                        runCatching { chatCacheRepository.saveMessages(scope, chatId, messages) }
                            .onFailure { Log.w(TAG, "Не удалось сохранить сообщения в кеш", it) }
                    }
                    displayMessages(messages)

                    if (messages.isNotEmpty()) {
                        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
                        firstVisibleMessageId = sortedMessages.first().id
                        lastVisibleMessageId = sortedMessages.last().id
                    }

                    hasMoreMessagesUp = messages.size >= 15
                    hasMoreMessagesDown = true

                    markVisibleMessagesAsRead(messages)
                } else {
                    if (!isRetry) {
                        Log.w(TAG, "Message load failed, retrying after channel refresh...")
                        delay(300)
                        loadMessages(isRetry = true)
                        return@launch
                    }
                    _events.tryEmit(ChatEvent.ToastRes(R.string.messages_load_failed))
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading messages", e)
                if (!isRetry) {
                    Log.w(TAG, "Message load exception, retrying after channel refresh...")
                    delay(300)
                    loadMessages(isRetry = true)
                    return@launch
                }
            } finally {
                setLoading(false)
            }
        }
    }

    fun loadMessagesUp() {
        if (isLoadingMessages || !hasMoreMessagesUp) return

        setLoading(true)
        Log.d(TAG, "Loading messages up from $firstVisibleMessageId")

        loadMessagesJob = viewModelScope.launch {
            try {
                cacheScope?.let { scope ->
                    val cached = chatCacheRepository.messagesBefore(
                        scope,
                        chatId,
                        firstVisibleMessageId,
                        limit = PAGE_SIZE
                    )
                    if (cached.isNotEmpty()) {
                        prependMessages(cached)
                        firstVisibleMessageId = cached.minBy { it.sentAt.seconds }.id
                        hasMoreMessagesUp = cached.size >= PAGE_SIZE
                        return@launch
                    }
                }
                val result = chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = firstVisibleMessageId,
                    offsetBefore = PAGE_SIZE,
                    offsetAfter = 0
                )

                if (result.isSuccess) {
                    val messages = result.getOrNull()!!
                    cacheScope?.let { scope ->
                        runCatching { chatCacheRepository.saveMessages(scope, chatId, messages) }
                    }
                    if (messages.isNotEmpty()) {
                        prependMessages(messages)
                        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
                        firstVisibleMessageId = sortedMessages.first().id
                        hasMoreMessagesUp = messages.size >= PAGE_SIZE
                    } else {
                        hasMoreMessagesUp = false
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading messages up", e)
            } finally {
                setLoading(false)
            }
        }
    }

    fun loadMessagesDown() {
        if (isLoadingMessages || !hasMoreMessagesDown) return

        setLoading(true)
        Log.d(TAG, "Loading messages down from $lastVisibleMessageId")

        loadMessagesJob = viewModelScope.launch {
            try {
                val result = chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = lastVisibleMessageId,
                    offsetBefore = 0,
                    offsetAfter = PAGE_SIZE
                )

                if (result.isSuccess) {
                    val messages = result.getOrNull()!!
                    cacheScope?.let { scope ->
                        runCatching { chatCacheRepository.saveMessages(scope, chatId, messages) }
                    }
                    if (messages.isNotEmpty()) {
                        appendMessages(messages)
                        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
                        lastVisibleMessageId = sortedMessages.last().id
                        hasMoreMessagesDown = messages.size >= PAGE_SIZE
                        markVisibleMessagesAsRead(messages)
                    } else {
                        hasMoreMessagesDown = false
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading messages down", e)
            } finally {
                setLoading(false)
            }
        }
    }

    /** Safety-net со скролл-листенера: долистали до низа — пометить всё загруженное прочитанным. */
    fun markAllLoadedMessagesAsRead() {
        val unreadMessageIds = _uiState.value.items
            .filter { it.type == MessageType.MESSAGE && it.senderId != currentUserId }
            .map { it.messageId }

        if (unreadMessageIds.isNotEmpty()) {
            viewModelScope.launch {
                try {
                    chatRepository.markAsRead(unreadMessageIds)
                    Log.d(TAG, "Marked all ${unreadMessageIds.size} loaded messages as read (reached bottom)")
                } catch (e: Exception) {
                    Log.e(TAG, "Error marking all messages as read on scroll to bottom", e)
                }
            }
        }
    }

    /** Вызывается скролл-листеном при достижении дна: триггер для [markAllLoadedMessagesAsRead]. */
    fun onReachedBottom() {
        if (!hasMoreMessagesDown && lastVisibleMessageId != lastBottomReadTriggerId) {
            lastBottomReadTriggerId = lastVisibleMessageId
            markAllLoadedMessagesAsRead()
        }
    }

    private fun markVisibleMessagesAsRead(messages: List<Shared.Message>) {
        val unreadMessageIds = messages
            .filter { it.senderId != currentUserId && !it.readByList.contains(currentUserId) }
            .map { it.id }

        if (unreadMessageIds.isNotEmpty()) {
            viewModelScope.launch {
                try {
                    chatRepository.markAsRead(unreadMessageIds)
                    Log.d(TAG, "Marked ${unreadMessageIds.size} messages as read")
                } catch (e: Exception) {
                    Log.e(TAG, "Error marking messages as read", e)
                }
            }
        }
    }

    /**
     * При возврате из фона — догружаем пропущенное и синхронизируем edit/delete.
     * Ждёт переподключения RealtimeService (каналы пересоздаются в ProcessLifecycleOwner.onStart).
     */
    fun onStartCatchUp() {
        if (lastVisibleMessageId <= 0L || isLoadingMessages) return
        viewModelScope.launch {
            val tokenValid = grpcManager.ensureTokenValid(appContext)
            if (!tokenValid) {
                Log.w(TAG, "onStartCatchUp: Token refresh failed")
                _events.tryEmit(ChatEvent.FinishActivity)
                return@launch
            }

            waitForConnection()
            if (lastVisibleMessageId > 0L && !isLoadingMessages) {
                Log.d(TAG, "onStartCatchUp: loading missed messages from lastVisibleMessageId=$lastVisibleMessageId")
                hasMoreMessagesDown = true
                loadMessagesDown()
                syncRecentMessages()
            }
        }
    }

    /**
     * Подтягивает свежее состояние уже загруженных сообщений и применяет diff:
     *  — сообщения, которых больше нет на сервере, удаляются (другое устройство удалило);
     *  — сообщения с обновлённым текстом/вложениями/isEdited обновляются.
     */
    private suspend fun syncRecentMessages() {
        val visibleMessages = _uiState.value.items.filter { it.type == MessageType.MESSAGE }
        if (visibleMessages.isEmpty()) return

        val earliestId = visibleMessages.minOf { it.messageId }
        val latestId = visibleMessages.maxOf { it.messageId }

        val result = chatRepository.loadMessages(
            chatId = chatId,
            fromMessageId = earliestId,
            offsetBefore = 0,
            offsetAfter = 50
        )
        if (result.isFailure) {
            Log.w(TAG, "syncRecentMessages: load failed: ${result.exceptionOrNull()?.message}")
            return
        }

        val serverMessages = result.getOrNull().orEmpty()
        val serverById = serverMessages.associateBy { it.id }
        val serverIds = serverById.keys

        val checkedUpperBound = if (serverMessages.size < 50) Long.MAX_VALUE else serverMessages.maxOfOrNull { it.id } ?: earliestId

        val deletedIds = visibleMessages
            .filter { it.messageId in earliestId..checkedUpperBound && it.messageId !in serverIds }
            .map { it.messageId }
        for (id in deletedIds) {
            Log.d(TAG, "syncRecentMessages: removing locally known but server-missing messageId=$id")
            removeMessageById(id)
        }

        for (item in visibleMessages) {
            val server = serverById[item.messageId] ?: continue
            val serverText = server.content?.text ?: ""
            val serverEdited = server.isEdited
            val serverAttachmentIds = server.content?.attachmentsList?.map { it.id }.orEmpty()
            val localAttachmentIds = item.attachments.map { it.id }
            if (item.text != serverText || item.isEdited != serverEdited || localAttachmentIds != serverAttachmentIds) {
                Log.d(TAG, "syncRecentMessages: applying server-side update to messageId=${item.messageId}")
                applyEditedMessage(server)
            }
        }
    }

    private suspend fun waitForConnection() {
        if (realtimeService.connectionState.value == RealtimeService.ConnectionState.CONNECTED) return
        try {
            withTimeout(5000) {
                realtimeService.connectionState.first { it == RealtimeService.ConnectionState.CONNECTED }
            }
        } catch (e: Exception) {
            Log.w(TAG, "waitForConnection timed out, proceeding anyway")
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Отправка и оптимистичные сообщения
    // ═══════════════════════════════════════════════════════════════

    fun sendMessage(text: String, fileIds: List<String> = emptyList()) {
        val messageText = text.trim()

        val edit = _uiState.value.pendingEdit
        if (edit != null) {
            sendEdit(edit.messageId, messageText)
            return
        }

        val replyId = _uiState.value.pendingReply?.messageId ?: 0L
        if (messageText.isBlank() && fileIds.isEmpty() && replyId == 0L) return

        Log.d(TAG, "sendMessage: textLength=${messageText.length}, fileIds=$fileIds, replyId=$replyId")

        val localId = java.util.UUID.randomUUID().toString()
        val optimisticItem = MessageItem(
            messageId = -System.nanoTime(),
            senderId = currentUserId,
            text = messageText,
            timestamp = System.currentTimeMillis(),
            attachments = emptyList(),
            readStatus = ReadStatus.SENDING,
            type = MessageType.MESSAGE,
            localId = localId
        )
        addOptimisticMessage(optimisticItem)

        viewModelScope.launch {
            try {
                val sentDraft = chatDraftRepository.edit(chatId, messageText, replyId)
                val result = chatRepository.sendMessage(
                    chatId = chatId,
                    text = messageText,
                    fileIds = fileIds,
                    replyToMessageId = replyId
                )

                if (result.isSuccess) {
                    val real = result.getOrNull()
                    if (real != null) {
                        replaceOptimisticByLocalId(localId, toMessageItem(real).copy(readStatus = ReadStatus.SENT))
                    } else {
                        updateOptimisticStatus(localId, ReadStatus.SENT)
                    }
                    sentDraft?.let { chatDraftRepository.clearAfterSent(chatId, it.generation) }
                } else {
                    updateOptimisticStatus(localId, ReadStatus.FAILED)
                    _events.tryEmit(
                        ChatEvent.ToastRes(R.string.message_send_error, result.exceptionOrNull()?.message.orEmpty())
                    )
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error sending message", e)
                updateOptimisticStatus(localId, ReadStatus.FAILED)
                _events.tryEmit(ChatEvent.ToastRes(R.string.message_send_error, e.message.orEmpty()))
            }
        }
    }

    private fun sendEdit(messageId: Long, text: String) {
        val fileIds = _uiState.value.pendingEdit?.fileIds ?: emptyList()
        if (text.isBlank() && fileIds.isEmpty()) {
            _events.tryEmit(ChatEvent.ToastRes(R.string.message_empty))
            return
        }

        viewModelScope.launch {
            val result = chatRepository.editMessage(messageId, text, fileIds)
            if (result.isSuccess) {
                clearPendingEdit()
                applyEditedMessage(result.getOrNull()!!)
            } else {
                _events.tryEmit(
                    ChatEvent.ToastRes(R.string.message_edit_error, result.exceptionOrNull()?.message.orEmpty())
                )
            }
        }
    }

    fun deleteMessage(messageId: Long) {
        viewModelScope.launch {
            val result = chatRepository.deleteMessage(messageId)
            if (result.isSuccess) {
                removeMessageById(messageId)
            } else {
                _events.tryEmit(
                    ChatEvent.ToastRes(R.string.message_delete_error, result.exceptionOrNull()?.message.orEmpty())
                )
            }
        }
    }

    /** Добавляет оптимистичный item (со статусом SENDING) в конец списка. Используется и
     *  пайплайном вложений (Activity создаёт MessageItem с localPreviewUris). */
    fun addOptimisticMessage(item: MessageItem) {
        val currentList = _uiState.value.items.toMutableList()
        currentList.removeAll { it.type == MessageType.UNREAD_SEPARATOR }

        val msgDate = startOfDay(item.timestamp)
        val lastItem = currentList.lastOrNull()
        if (lastItem == null || (lastItem.type != MessageType.MESSAGE && lastItem.type != MessageType.SYSTEM) || startOfDay(lastItem.timestamp) != msgDate) {
            currentList.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
        }
        currentList.add(item)
        submitItems(currentList)
    }

    /** Заменяет оптимистичный item (по localId) на серверный с указанным статусом. */
    private fun replaceOptimisticByLocalId(localId: String, replacement: MessageItem) {
        val currentList = _uiState.value.items.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            currentList[idx] = replacement.copy(localId = localId)
            submitItems(currentList)
        }
    }

    /** Обновляет статус оптимистичного item (SENDING→SENT/FAILED), не подменяя сам item. */
    private fun updateOptimisticStatus(localId: String, status: ReadStatus) {
        val currentList = _uiState.value.items.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            currentList[idx] = currentList[idx].copy(readStatus = status)
            submitItems(currentList)
        }
    }

    /** Обновляет inline-прогресс аплоада медиа на оптимистичном сообщении (0..100). */
    private fun updateOptimisticUploadProgress(localId: String, progress: Int) {
        val currentList = _uiState.value.items.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            currentList[idx] = currentList[idx].copy(uploadProgress = progress.coerceIn(0, 100))
            submitItems(currentList)
        }
    }

    /** Сбрасывает uploadProgress. Если serverMessageId != 0 — заменяет messageId оптимистичного item. */
    private fun clearOptimisticUploadProgress(localId: String, serverMessageId: Long) {
        val currentList = _uiState.value.items.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            val item = currentList[idx]
            currentList[idx] = item.copy(
                uploadProgress = null,
                messageId = if (serverMessageId != 0L) serverMessageId else item.messageId,
                readStatus = if (item.readStatus == ReadStatus.SENDING || item.readStatus == ReadStatus.FAILED) ReadStatus.SENT else item.readStatus
            )
            submitItems(currentList)
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Reply / Edit / Черновик
    // ═══════════════════════════════════════════════════════════════

    fun setPendingReply(item: MessageItem) {
        _uiState.value = _uiState.value.copy(
            pendingReply = PendingReply(
                messageId = item.messageId,
                senderId = item.senderId,
                senderName = item.senderName,
                text = item.text,
                attachments = item.attachments,
            )
        )
        savedStateHandle[KEY_PENDING_REPLY] = item.messageId
    }

    fun clearPendingReply() {
        _uiState.value = _uiState.value.copy(pendingReply = null)
        savedStateHandle[KEY_PENDING_REPLY] = 0L
    }

    fun setPendingEdit(item: MessageItem) {
        if (_uiState.value.pendingReply != null) {
            clearPendingReply()
        }
        _uiState.value = _uiState.value.copy(
            pendingEdit = PendingEdit(
                messageId = item.messageId,
                text = item.text,
                fileIds = item.attachments
                    .filter { it.type != Shared.MessageAttachmentType.FORWARDED_MESSAGE }
                    .map { it.fileId }
                    .filter { it.isNotBlank() },
            )
        )
        savedStateHandle[KEY_PENDING_EDIT] = item.messageId
    }

    fun clearPendingEdit() {
        _uiState.value = _uiState.value.copy(pendingEdit = null)
        savedStateHandle[KEY_PENDING_EDIT] = 0L
    }

    /** Сохраняет черновик (текст ввода живёт в Activity, сюда приходит значением). */
    fun saveDraft(text: String, immediate: Boolean = false) {
        if (!supportsDrafts || isRestoringDraft || _uiState.value.pendingEdit != null) return
        draftSaveJob?.cancel()
        draftSaveJob = viewModelScope.launch {
            val replyId = _uiState.value.pendingReply?.messageId ?: 0L
            chatDraftRepository.edit(chatId, text, replyId)
            if (immediate) chatDraftRepository.flush(chatId) else {
                delay(2_000)
                chatDraftRepository.flush(chatId)
            }
        }
    }

    private fun restoreDraft() {
        if (!supportsDrafts || draftRestored) return
        draftRestored = true
        viewModelScope.launch {
            val draft = chatDraftRepository.restore(chatId) ?: return@launch
            isRestoringDraft = true
            _events.tryEmit(ChatEvent.DraftRestored(draft.text))
            if (draft.replyToMessageId == 0L) {
                isRestoringDraft = false
                return@launch
            }

            val item = _uiState.value.items.firstOrNull { it.messageId == draft.replyToMessageId }
                ?: chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = draft.replyToMessageId,
                    offsetBefore = 1,
                    offsetAfter = 1
                ).getOrNull()?.firstOrNull { it.id == draft.replyToMessageId }?.let(::toMessageItem)
            if (item != null) {
                setPendingReply(item)
            } else {
                chatDraftRepository.edit(chatId, draft.text, 0L)
                chatDraftRepository.flush(chatId)
            }
            isRestoringDraft = false
        }
    }

    /** Восстанавливает режимы reply/edit после process death по SavedStateHandle. */
    private fun restoreFromSavedState() {
        val savedReplyId = savedStateHandle.get<Long>(KEY_PENDING_REPLY) ?: 0L
        val savedEditId = savedStateHandle.get<Long>(KEY_PENDING_EDIT) ?: 0L
        if (savedReplyId <= 0L && savedEditId <= 0L) return

        viewModelScope.launch {
            if (savedReplyId > 0L) {
                fetchMessageItem(savedReplyId)?.let { setPendingReply(it) }
            }
            if (savedEditId > 0L) {
                fetchMessageItem(savedEditId)?.let { setPendingEdit(it) }
            }
        }
    }

    private suspend fun fetchMessageItem(messageId: Long): MessageItem? {
        if (messageId <= 0L) return null
        return _uiState.value.items.firstOrNull { it.messageId == messageId }
            ?: chatRepository.loadMessages(
                chatId = chatId,
                fromMessageId = messageId,
                offsetBefore = 1,
                offsetAfter = 1
            ).getOrNull()?.firstOrNull { it.id == messageId }?.let(::toMessageItem)
    }

    // ═══════════════════════════════════════════════════════════════
    // Realtime
    // ═══════════════════════════════════════════════════════════════

    private fun subscribeToRealtimeEvents() {
        viewModelScope.launch {
            realtimeService.newMessages.collect { event ->
                if (event.chatId == chatId) {
                    val msg = event.message
                    cacheScope?.let { scope ->
                        chatCacheRepository.saveMessages(scope, chatId, listOf(msg))
                    }
                    addNewMessage(msg)
                }
            }
        }

        // Прогресс аплоада медиа — обновляем uploadProgress и статус оптимистичных сообщений.
        viewModelScope.launch {
            MediaSendService.uploadEvents.collect { event ->
                if (event.chatId != chatId) return@collect
                when (event.state) {
                    UploadState.PREPARING -> updateOptimisticUploadProgress(event.localId, event.progress)
                    UploadState.UPLOADING -> updateOptimisticUploadProgress(event.localId, event.progress)
                    UploadState.SENDING -> updateOptimisticUploadProgress(event.localId, 100)
                    UploadState.SENT -> clearOptimisticUploadProgress(event.localId, event.serverMessageId)
                    UploadState.FAILED -> {
                        updateOptimisticStatus(event.localId, ReadStatus.FAILED)
                        clearOptimisticUploadProgress(event.localId, 0L)
                    }
                }
            }
        }

        viewModelScope.launch {
            realtimeService.messagesRead.collect { event ->
                if (event.chatId == chatId) {
                    updateMessageReadStatus(event.messageId, event.newReadByList)
                    cacheScope?.let { scope ->
                        chatCacheRepository.updateReadBy(scope, chatId, event.messageId, event.newReadByList)
                    }
                }
            }
        }

        viewModelScope.launch {
            realtimeService.messageEdited.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    applyEditedMessage(event.message)
                    cacheScope?.let { scope ->
                        chatCacheRepository.saveMessages(scope, chatId, listOf(event.message))
                    }
                }
            }
        }

        viewModelScope.launch {
            realtimeService.messageDeleted.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    removeMessageById(event.messageId)
                    cacheScope?.let { scope ->
                        chatCacheRepository.deleteMessage(scope, chatId, event.messageId)
                    }
                }
            }
        }

        viewModelScope.launch {
            realtimeService.messagePinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onMessagePinnedRemote(event.messageId)
                }
            }
        }

        viewModelScope.launch {
            realtimeService.messageUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onMessageUnpinnedRemote(event.messageId)
                }
            }
        }

        viewModelScope.launch {
            realtimeService.allMessagesUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onAllMessagesUnpinnedRemote()
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Список сообщений (разделители, реконсиляция)
    // ═══════════════════════════════════════════════════════════════

    private fun displayMessages(messages: List<Shared.Message>) {
        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
        val messageItems = messagesWithDateSeparators(sortedMessages).toMutableList()

        // Разделитель непрочитанных вставляется здесь; Activity прокручивает к нему
        // в момент появления (детерминированно в commit-callback DiffUtil).
        if (firstUnreadMessageId > 0) {
            val unreadIndex = messageItems.indexOfFirst {
                it.type == MessageType.MESSAGE && it.messageId == firstUnreadMessageId
            }
            if (unreadIndex >= 0) {
                messageItems.add(unreadIndex, MessageItem.createUnreadSeparator(appContext.getString(R.string.unread_messages)))
            }
        }

        submitItems(messageItems)
    }

    private fun messagesWithDateSeparators(messages: List<Shared.Message>): List<MessageItem> {
        val result = mutableListOf<MessageItem>()
        var lastDate: Long = -1

        for (msg in messages) {
            val msgDate = startOfDay(msg.sentAt.seconds * 1000)

            if (msgDate != lastDate) {
                result.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
                lastDate = msgDate
            }

            result.add(toMessageItem(msg))
        }

        return result
    }

    private fun toMessageItem(msg: Shared.Message): MessageItem {
        val isSystem = msg.type == Shared.MessageContentType.SYSTEM
        return MessageItem(
            messageId = msg.id,
            senderId = msg.senderId,
            text = msg.content?.text ?: "",
            timestamp = msg.sentAt.seconds * 1000,
            attachments = msg.content?.attachmentsList ?: emptyList(),
            readStatus = if (!isSystem && msg.senderId == currentUserId) {
                if (msg.readByList.any { it != currentUserId }) ReadStatus.READ else ReadStatus.SENT
            } else {
                ReadStatus.NONE
            },
            type = if (isSystem) MessageType.SYSTEM else MessageType.MESSAGE,
            isEdited = msg.isEdited,
            replyTo = if (msg.hasReplyTo()) msg.replyTo else null
        )
    }

    private fun prependMessages(messages: List<Shared.Message>) {
        val currentList = _uiState.value.items.toMutableList()

        val existingIds = currentList
            .filter { it.type == MessageType.MESSAGE }
            .map { it.messageId }
            .toSet()
        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
            .filter { it.id !in existingIds }

        if (sortedMessages.isEmpty()) {
            hasMoreMessagesUp = false
            return
        }

        if (currentList.isNotEmpty() && currentList.first().type == MessageType.DATE_SEPARATOR) {
            currentList.removeAt(0)
        }

        val newItems = messagesWithDateSeparators(sortedMessages)

        currentList.addAll(0, newItems)
        submitItems(currentList)
    }

    private fun appendMessages(messages: List<Shared.Message>) {
        val currentList = _uiState.value.items.toMutableList()

        val existingIds = currentList
            .filter { it.type == MessageType.MESSAGE }
            .map { it.messageId }
            .toSet()
        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
            .filter { it.id !in existingIds }

        if (sortedMessages.isEmpty()) {
            hasMoreMessagesDown = false
            return
        }

        val lastMsgItem = currentList.lastOrNull { it.type == MessageType.MESSAGE }
        var lastDate = if (lastMsgItem != null) startOfDay(lastMsgItem.timestamp) else -1L

        for (msg in sortedMessages) {
            val msgDate = startOfDay(msg.sentAt.seconds * 1000)
            if (msgDate != lastDate) {
                currentList.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
                lastDate = msgDate
            }
            currentList.add(toMessageItem(msg))
        }

        submitItems(currentList)
    }

    private fun addNewMessage(msg: Shared.Message) {
        val currentList = _uiState.value.items.toMutableList()

        // Реконсиляция своего оптимистичного сообщения. Realtime-эхо и ответ sendMessage
        // (который проставляет messageId через clearOptimisticUploadProgress) могут прийти в
        // любом порядке — поэтому ищем плейсхолдер ДО проверки дубликата:
        //  • уже усыновлённый по messageId (SENT пришёл раньше эха) — иначе дубль-чек ниже
        //    отбросил бы эхо, и плейсхолдер остался бы с пустыми вложениями (пустой bubble);
        //  • либо ещё SENDING с совпадающим контентом (эхо раньше ответа sendMessage).
        // Для медиа вложения у плейсхолдера пустые (есть только localPreviewUris), поэтому
        // размер вложений не сверяем, если идёт upload.
        if (msg.senderId == currentUserId) {
            val optIdx = currentList.indexOfFirst {
                it.type == MessageType.MESSAGE && it.localId != null && (
                    it.messageId == msg.id ||
                        (it.readStatus == ReadStatus.SENDING &&
                            it.text == (msg.content?.text ?: "") &&
                            (it.uploadProgress != null || it.localPreviewUris.isNotEmpty() ||
                                it.attachments.size == (msg.content?.attachmentsList?.size ?: 0)))
                    )
            }
            if (optIdx >= 0) {
                currentList[optIdx] = toMessageItem(msg).copy(localId = currentList[optIdx].localId)
                submitItems(currentList)
                return
            }
        }

        // Проверка дубликата
        if (currentList.any { (it.type == MessageType.MESSAGE || it.type == MessageType.SYSTEM) && it.messageId == msg.id }) {
            return
        }

        val messageItem = toMessageItem(msg)

        currentList.removeAll { it.type == MessageType.UNREAD_SEPARATOR }

        val msgDate = startOfDay(msg.sentAt.seconds * 1000)
        val lastItem = currentList.lastOrNull()
        if (lastItem != null && (lastItem.type == MessageType.MESSAGE || lastItem.type == MessageType.SYSTEM)) {
            val lastMsgDate = startOfDay(lastItem.timestamp)
            if (msgDate != lastMsgDate) {
                currentList.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
            }
        } else if (currentList.isEmpty()) {
            currentList.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
        }

        currentList.add(messageItem)
        submitItems(currentList)

        lastVisibleMessageId = msg.id

        if (msg.senderId != currentUserId) {
            viewModelScope.launch {
                try {
                    chatRepository.markAsRead(listOf(msg.id))
                } catch (e: Exception) {
                    Log.e(TAG, "Error marking new message as read", e)
                }
            }
        }
    }

    private fun updateMessageReadStatus(messageId: Long, newReadBy: List<Long>) {
        val currentList = _uiState.value.items.toMutableList()
        val index = currentList.indexOfFirst { it.messageId == messageId }
        if (index < 0) return

        val item = currentList[index]
        val updatedItem = item.copy(
            readStatus = if (item.senderId == currentUserId && newReadBy.any { it != currentUserId }) {
                ReadStatus.READ
            } else {
                item.readStatus
            }
        )
        currentList[index] = updatedItem
        submitItems(currentList)
    }

    fun applyEditedMessage(msg: Shared.Message) {
        val currentList = _uiState.value.items.toMutableList()
        val index = currentList.indexOfFirst {
            it.type == MessageType.MESSAGE && it.messageId == msg.id
        }
        if (index < 0) return

        val old = currentList[index]
        val updated = old.copy(
            text = msg.content?.text ?: "",
            attachments = msg.content?.attachmentsList ?: emptyList(),
            isEdited = msg.isEdited
        )
        currentList[index] = updated
        submitItems(currentList)
    }

    fun removeMessageById(messageId: Long) {
        val currentList = _uiState.value.items.toMutableList()
        val removed = currentList.removeAll {
            it.type == MessageType.MESSAGE && it.messageId == messageId
        }
        if (removed) {
            submitItems(currentList)
        }
    }

    private fun rebuildMessagesFromList(messages: List<Shared.Message>) {
        submitItems(messages.map { toMessageItem(it) })
    }

    /**
     * Скролл к сообщению (закрепы, search): если не загружено — подгружает окно вокруг.
     * @return true если сообщение доступно в [uiState] после вызова.
     */
    suspend fun ensureMessageLoaded(messageId: Long): Boolean {
        if (_uiState.value.items.any { it.type == MessageType.MESSAGE && it.messageId == messageId }) {
            return true
        }
        val result = chatRepository.loadMessages(chatId, fromMessageId = messageId, offsetBefore = 20, offsetAfter = 20)
        if (result.isSuccess) {
            rebuildMessagesFromList(result.getOrNull() ?: emptyList())
            return _uiState.value.items.any { it.type == MessageType.MESSAGE && it.messageId == messageId }
        }
        return false
    }

    // ═══════════════════════════════════════════════════════════════
    // Закреплённые сообщения
    // ═══════════════════════════════════════════════════════════════

    private fun loadPinnedMessages() {
        viewModelScope.launch {
            val result = grpcManager.listPinnedMessages(chatId)
            if (result.isSuccess) {
                val (list, total) = result.getOrNull() ?: (emptyList<Shared.PinnedMessageInfo>() to 0)
                pinnedById.clear()
                pinnedSorted.clear()
                pinnedById.putAll(list.associateBy { it.message.id })
                pinnedSorted.addAll(list)
                pinnedTotalCount = total
                updatePinnedState()
            }
        }
    }

    private fun onMessagePinnedRemote(messageId: Long) {
        if (pinnedById.containsKey(messageId)) return
        // Чтобы получить полный Message — перезагружаем последнюю страницу закрепов.
        viewModelScope.launch {
            val result = grpcManager.listPinnedMessages(chatId)
            if (result.isSuccess) {
                val (list, total) = result.getOrNull() ?: return@launch
                pinnedById.clear()
                pinnedSorted.clear()
                pinnedById.putAll(list.associateBy { it.message.id })
                pinnedSorted.addAll(list)
                pinnedTotalCount = total
                updatePinnedState()
            }
        }
    }

    private fun onMessageUnpinnedRemote(messageId: Long) {
        val removed = pinnedById.remove(messageId) ?: return
        pinnedSorted.remove(removed)
        pinnedTotalCount = (pinnedTotalCount - 1).coerceAtLeast(0)
        updatePinnedState()
    }

    private fun onAllMessagesUnpinnedRemote() {
        pinnedById.clear()
        pinnedSorted.clear()
        pinnedTotalCount = 0
        updatePinnedState()
    }

    fun isMessagePinned(messageId: Long): Boolean = pinnedById.containsKey(messageId)

    /** Локальное обновление mute-статуса после успешного серверного вызова из Activity. */
    fun setChatMuted(muted: Boolean) {
        _uiState.value = _uiState.value.copy(isChatMuted = muted)
    }

    /**
     * Кнопка «вниз»: подтягивает последние сообщения с сервера, если локальный хвост протух.
     * Вызывается из Activity параллельно с плавным скроллом (UX-тайминг остаётся в Activity).
     */
    suspend fun refreshLatestMessages() {
        if (isLoadingMessages) return
        val serverLastMessageId = chatRepository.getChatInfo(chatId).getOrNull()?.lastMessageId ?: 0L
        if (serverLastMessageId <= 0L || serverLastMessageId == lastVisibleMessageId) return

        setLoading(true)
        hasMoreMessagesDown = false
        val result = chatRepository.loadMessages(
            chatId = chatId,
            fromMessageId = 0L,
            offsetBefore = 0,
            offsetAfter = 0,
            count = PAGE_SIZE
        )
        setLoading(false)

        if (result.isSuccess) {
            val messages = result.getOrNull()!!
            displayMessages(messages)
            if (messages.isNotEmpty()) {
                val sorted = messages.sortedBy { it.sentAt.seconds }
                firstVisibleMessageId = sorted.first().id
                lastVisibleMessageId = sorted.last().id
            }
            hasMoreMessagesUp = messages.size >= 15
            hasMoreMessagesDown = false
            markVisibleMessagesAsRead(messages)
        }
    }

    fun togglePinForMessage(item: MessageItem) {
        val isPinned = pinnedById.containsKey(item.messageId)
        viewModelScope.launch {
            if (isPinned) {
                val result = grpcManager.unpinMessage(chatId, item.messageId)
                if (result.isFailure) {
                    _events.tryEmit(ChatEvent.ToastRes(R.string.message_unpin_failed))
                }
            } else {
                val result = grpcManager.pinMessage(chatId, item.messageId)
                if (result.isFailure) {
                    val cause = result.exceptionOrNull()
                    _events.tryEmit(
                        if (cause is GrpcManager.PinErrorException && cause.isTooManyPinned) {
                            ChatEvent.ToastRes(R.string.pin_limit_reached)
                        } else {
                            ChatEvent.ToastRes(R.string.message_pin_failed)
                        }
                    )
                } else {
                    val pinned = result.getOrNull() ?: return@launch
                    if (!pinnedById.containsKey(pinned.message.id)) {
                        pinnedById[pinned.message.id] = pinned
                        pinnedSorted.add(0, pinned)
                        pinnedTotalCount++
                        updatePinnedState()
                    }
                }
            }
        }
    }

    private fun updatePinnedState() {
        val first = pinnedSorted.firstOrNull()
        _uiState.value = _uiState.value.copy(
            pinnedCount = pinnedTotalCount,
            pinnedPreview = first?.message?.content?.text ?: "",
            firstPinnedMessageId = first?.message?.id ?: 0L,
        )
    }

    // ═══════════════════════════════════════════════════════════════
    // Утилиты
    // ═══════════════════════════════════════════════════════════════

    private fun setLoading(loading: Boolean) {
        isLoadingMessages = loading
        _uiState.value = _uiState.value.copy(isLoading = loading)
    }

    private fun submitItems(items: List<MessageItem>) {
        _uiState.value = _uiState.value.copy(items = items)
    }

    private fun startOfDay(timestampMillis: Long): Long {
        val calendar = Calendar.getInstance().apply {
            timeInMillis = timestampMillis
        }
        calendar.set(Calendar.HOUR_OF_DAY, 0)
        calendar.set(Calendar.MINUTE, 0)
        calendar.set(Calendar.SECOND, 0)
        calendar.set(Calendar.MILLISECOND, 0)
        return calendar.timeInMillis
    }

    private fun formatDateSeparator(timestampMillis: Long): String {
        val today = startOfDay(System.currentTimeMillis())
        val yesterday = today - 24 * 60 * 60 * 1000
        val messageDate = startOfDay(timestampMillis)

        return when {
            messageDate == today -> appContext.getString(R.string.date_today)
            messageDate == yesterday -> appContext.getString(R.string.date_yesterday)
            else -> SimpleDateFormat("dd MMMM yyyy", appContext.resources.configuration.locales[0]).format(Date(timestampMillis))
        }
    }

    override fun onCleared() {
        loadMessagesJob?.cancel()
        draftSaveJob?.cancel()
        super.onCleared()
    }
}
