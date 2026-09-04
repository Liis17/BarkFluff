package com.barkfluff.client

import android.content.Context
import android.net.Uri
import android.util.Log
import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.shared.Shared
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageRowProjector
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.adapter.ReadStatus
import com.barkfluff.client.chat.RegularChatSession
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.drafts.ComposerAttachmentStore
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.MessageGateway
import com.barkfluff.client.domain.gateway.PresenceGateway
import com.barkfluff.client.domain.gateway.RealtimeGateway
import com.barkfluff.client.domain.model.PinErrorException
import com.barkfluff.client.send.OutgoingMessageQueue
import com.barkfluff.client.send.OutgoingMessageSnapshot
import com.barkfluff.client.send.AttachmentSpec
import com.barkfluff.client.send.SendJob
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.io.File
import barkfluff.files.FilesApiOuterClass.UploadFileType
import javax.inject.Inject
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.channels.Channel

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

/** Metadata that is stable for the lifetime of a regular chat session. */
data class ChatSessionState(
    val chatId: String = "",
    val title: String = "",
    val avatarFileId: String? = null,
    val isGroupChat: Boolean = false,
    val isChatMuted: Boolean = false,
    val otherUserId: Long = 0L,
    val pinnedCount: Int = 0,
    val pinnedPreview: String = "",
    val firstPinnedMessageId: Long = 0L,
)

/** Timeline rows are immutable projections; pagination cursors stay outside the renderer. */
data class TimelineState(
    val rows: List<MessageItem> = emptyList(),
    val isLoading: Boolean = false,
    val firstVisibleMessageId: Long = 0L,
    val lastVisibleMessageId: Long = 0L,
)

/** Text, reply/edit mode and accepted preview attachments survive Activity recreation. */
data class ComposerState(
    val text: String = "",
    val pendingReply: PendingReply? = null,
    val pendingEdit: PendingEdit? = null,
    val attachmentPaths: List<String> = emptyList(),
    val attachmentKinds: List<String> = emptyList(),
    val draftGeneration: Long? = null,
    val isRestoring: Boolean = false,
)

/** Selection is a value object; the adapter never owns this set. */
data class SelectionState(
    val selectedMessageIds: Set<Long> = emptySet(),
    val isActive: Boolean = false,
) {
    val isEmpty: Boolean get() = selectedMessageIds.isEmpty()
}

/** Presence is ephemeral and is rebuilt from subscriptions after process death. */
data class PresenceState(
    val onlineUserIds: Set<Long> = emptySet(),
    val typingUserIds: Set<Long> = emptySet(),
    val lastSeenEpochMillisByUser: Map<Long, Long> = emptyMap(),
)

/**
 * Single immutable state boundary for a regular chat.
 *
 * The computed properties below preserve the old rendering call sites while callers migrate to
 * the explicit session/timeline/composer/selection/presence components.
 */
data class ChatUiState(
    val session: ChatSessionState = ChatSessionState(),
    val timeline: TimelineState = TimelineState(),
    val composer: ComposerState = ComposerState(),
    val selection: SelectionState = SelectionState(),
    val presence: PresenceState = PresenceState(),
) {
    val chatTitle: String get() = session.title
    val chatAvatarFileId: String? get() = session.avatarFileId
    val isGroupChat: Boolean get() = session.isGroupChat
    val isChatMuted: Boolean get() = session.isChatMuted
    val otherUserId: Long get() = session.otherUserId
    val items: List<MessageItem> get() = timeline.rows
    val isLoading: Boolean get() = timeline.isLoading
    val pendingReply: PendingReply? get() = composer.pendingReply
    val pendingEdit: PendingEdit? get() = composer.pendingEdit
    val pinnedCount: Int get() = session.pinnedCount
    val pinnedPreview: String get() = session.pinnedPreview
    val firstPinnedMessageId: Long get() = session.firstPinnedMessageId
}

/** Intents accepted by the regular-chat state boundary. */
sealed interface ChatIntent {
    data class Initialize(
        val chatId: String,
        val title: String,
        val avatarFileId: String?,
        val isGroupChat: Boolean,
        val otherUserId: Long,
        val supportsDrafts: Boolean,
    ) : ChatIntent
    data object Load : ChatIntent
    data object LoadUp : ChatIntent
    data object LoadDown : ChatIntent
    data object StartCatchUp : ChatIntent
    data object ReachedBottom : ChatIntent
    data class TextChanged(val text: String) : ChatIntent
    data class TypingChanged(val text: String) : ChatIntent
    data object StopTyping : ChatIntent
    data class Send(val text: String, val fileIds: List<String> = emptyList()) : ChatIntent
    data class SendMedia(val job: SendJob) : ChatIntent
    data class StageAttachment(
        val uri: Uri,
        val kind: String,
        val fileName: String? = null,
        val mimeType: String? = null,
    ) : ChatIntent
    data class RemoveAttachment(val attachmentIndex: Int) : ChatIntent
    data class SetReply(val item: MessageItem) : ChatIntent
    data object ClearReply : ChatIntent
    data class SetEdit(val item: MessageItem) : ChatIntent
    data object ClearEdit : ChatIntent
    data class SaveDraft(val text: String, val immediate: Boolean = false) : ChatIntent
    data class ToggleSelection(val messageId: Long) : ChatIntent
    data object ClearSelection : ChatIntent
    data class TogglePin(val item: MessageItem) : ChatIntent
    data class Delete(val messageId: Long) : ChatIntent
    data class RetryOutgoing(val operationId: String) : ChatIntent
    data class CancelOutgoing(val operationId: String) : ChatIntent
    data class SetMuted(val muted: Boolean) : ChatIntent
}

/** One-shot UI effects. Delivery is buffered so a draft/recovery effect is not a lost event. */
sealed interface ChatEffect {
    data class ToastRes(val resId: Int, val formatArg: String? = null) : ChatEffect
    data class DraftRestored(val text: String) : ChatEffect
    data class AttachmentStaged(val source: Uri) : ChatEffect
    data class AttachmentStageFailed(val source: Uri) : ChatEffect
    data class SendAccepted(val text: String) : ChatEffect
    data object SendRejected : ChatEffect
    data object FinishActivity : ChatEffect
}

/** Source compatibility for the pre-migration Activity observer. */
typealias ChatEvent = ChatEffect

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
    private val authGateway: AuthGateway,
    private val messageGateway: MessageGateway,
    private val realtimeService: RealtimeService,
    private val realtimeGateway: RealtimeGateway,
    private val presenceGateway: PresenceGateway,
    private val chatCacheRepository: ChatCacheRepository,
    private val chatDraftRepository: ChatDraftRepository,
    private val composerAttachmentStore: ComposerAttachmentStore,
    private val outgoingMessageQueue: OutgoingMessageQueue,
    private val savedStateHandle: SavedStateHandle,
) : ViewModel() {

    companion object {
        private const val TAG = "ChatViewModel"
        private const val PAGE_SIZE = 30
        private const val KEY_PENDING_REPLY = "pending_reply_message_id"
        private const val KEY_PENDING_EDIT = "pending_edit_message_id"
        private const val KEY_SELECTED_MESSAGE_IDS = "selected_message_ids"
        private const val KEY_CHAT_ID = "chat_id"
    }

    private val _uiState = MutableStateFlow(ChatUiState())
    val uiState: StateFlow<ChatUiState> = _uiState.asStateFlow()
    /** Public state contract; [uiState] remains as a source-compatible alias during migration. */
    val state: StateFlow<ChatUiState> = uiState

    private val _events = MutableSharedFlow<ChatEffect>(extraBufferCapacity = 16)
    val events: SharedFlow<ChatEvent> = _events.asSharedFlow()
    private val effectChannel = Channel<ChatEffect>(Channel.BUFFERED)
    val effects: Flow<ChatEffect> = effectChannel.receiveAsFlow()

    private val selectionReducer = SelectionReducer()
    private val composerReducer = ChatComposer()
    private val rowProjector = MessageRowProjector()
    private val regularChatSession = RegularChatSession(messageGateway, chatCacheRepository)
    private val presenceSession by lazy { ChatPresenceSession(realtimeGateway, viewModelScope) }

    private fun emitEffect(effect: ChatEffect) {
        _events.tryEmit(effect)
        effectChannel.trySend(effect)
    }

    /** Reducer entry point for new callers. Legacy convenience methods delegate to the same path. */
    fun dispatch(intent: ChatIntent) {
        when (intent) {
            is ChatIntent.Initialize -> initialize(
                chatId = intent.chatId,
                title = intent.title,
                avatarFileId = intent.avatarFileId,
                isGroupChat = intent.isGroupChat,
                otherUserId = intent.otherUserId,
                supportsDrafts = intent.supportsDrafts,
            )
            ChatIntent.Load -> loadMessages()
            ChatIntent.LoadUp -> loadMessagesUp()
            ChatIntent.LoadDown -> loadMessagesDown()
            ChatIntent.StartCatchUp -> onStartCatchUp()
            ChatIntent.ReachedBottom -> onReachedBottom()
            is ChatIntent.TextChanged -> updateComposerText(intent.text)
            is ChatIntent.TypingChanged -> presenceSession.textChanged(intent.text)
            ChatIntent.StopTyping -> presenceSession.stopTyping(sendCancel = true)
            is ChatIntent.Send -> sendMessage(intent.text, intent.fileIds)
            is ChatIntent.SendMedia -> enqueueMedia(intent.job)
            is ChatIntent.StageAttachment -> stageAttachment(intent)
            is ChatIntent.RemoveAttachment -> removeComposerAttachment(intent.attachmentIndex)
            is ChatIntent.SetReply -> setPendingReply(intent.item)
            ChatIntent.ClearReply -> clearPendingReply()
            is ChatIntent.SetEdit -> setPendingEdit(intent.item)
            ChatIntent.ClearEdit -> clearPendingEdit()
            is ChatIntent.SaveDraft -> saveDraft(intent.text, intent.immediate)
            is ChatIntent.ToggleSelection -> toggleSelection(intent.messageId)
            ChatIntent.ClearSelection -> clearSelection()
            is ChatIntent.TogglePin -> togglePinForMessage(intent.item)
            is ChatIntent.Delete -> deleteMessage(intent.messageId)
            is ChatIntent.RetryOutgoing -> retryOutgoing(intent.operationId)
            is ChatIntent.CancelOutgoing -> cancelOutgoing(intent.operationId)
            is ChatIntent.SetMuted -> setChatMuted(intent.muted)
        }
    }

    private val globalParam = GlobalParam(appContext)
    private val currentUserId = globalParam.userId
    private val chatPresence = ChatPresence(currentUserId)

    private var initialized = false
    private var chatId: String = ""
    private var supportsDrafts = false
    private var cacheScope: CacheScope? = null
    private var presenceUserIds: List<Long> = emptyList()
    private var presenceStatusJob: Job? = null

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
    /** Prevents a newly-picked preview from being mistaken for the send in flight. */
    @Volatile private var sendInFlight = false
    private var composerGenerationCounter = 0L
    private var observedOutgoingOperationIds = emptySet<String>()
    private val clearedDraftOperations = mutableSetOf<String>()
    private var latestOutgoingSnapshots: List<OutgoingMessageSnapshot> = emptyList()

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
            session = ChatSessionState(
                chatId = chatId,
                title = title,
                avatarFileId = avatarFileId,
                isGroupChat = isGroupChat,
                otherUserId = otherUserId,
            ),
        )

        configurePresence(if (isGroupChat) emptyList() else listOf(otherUserId))
        restoreSelection()
        restoreFromSavedState()
        subscribeToRealtimeEvents()
        subscribeToOutgoingMessages()
        loadChatInfoAndMessages()
        loadPinnedMessages()
        restoreDraft()
        restoreComposerAttachments()
    }

    /** Copies a picked URI before it is exposed as an accepted composer preview. */
    private fun stageAttachment(intent: ChatIntent.StageAttachment) {
        val scope = cacheScope ?: run {
            emitEffect(ChatEffect.AttachmentStageFailed(intent.uri))
            emitEffect(ChatEffect.ToastRes(R.string.message_send_error, "No active chat scope"))
            return
        }
        if (sendInFlight) {
            emitEffect(ChatEffect.AttachmentStageFailed(intent.uri))
            emitEffect(ChatEffect.ToastRes(R.string.message_send_error, "Message is being queued"))
            return
        }
        // Attachment generations are local handoff generations, not just the remote draft
        // revision. Keeping them monotonic lets a later preview survive an older enqueue.
        val generation = nextComposerGeneration()
        viewModelScope.launch {
            runCatching {
                composerAttachmentStore.stageUri(
                    scope = scope,
                    chatId = chatId,
                    uri = intent.uri,
                    generation = generation,
                    kind = intent.kind,
                    fileName = intent.fileName,
                    mimeType = intent.mimeType,
                )
            }.onSuccess { attachment ->
                composerGenerationCounter = maxOf(composerGenerationCounter, attachment.generation)
                val state = _uiState.value
                _uiState.value = state.copy(
                    composer = composerReducer.stagedAttachment(
                        state.composer,
                        attachment.path,
                        attachment.kind,
                    )
                )
                emitEffect(ChatEffect.AttachmentStaged(intent.uri))
            }.onFailure { error ->
                Log.w(TAG, "Composer attachment staging failed", error)
                emitEffect(ChatEffect.AttachmentStageFailed(intent.uri))
                emitEffect(ChatEffect.ToastRes(R.string.message_send_error, error.message.orEmpty()))
            }
        }
    }

    fun removeComposerAttachment(attachmentIndex: Int) {
        val scope = cacheScope ?: return
        viewModelScope.launch {
            composerAttachmentStore.remove(scope, chatId, attachmentIndex)
            val state = _uiState.value
            val path = state.composer.attachmentPaths.getOrNull(attachmentIndex)
            if (path != null) {
                _uiState.value = state.copy(
                    composer = composerReducer.removeAttachment(state.composer, path)
                )
            }
        }
    }

    private fun restoreComposerAttachments() {
        val scope = cacheScope ?: return
        viewModelScope.launch {
            val restored = composerAttachmentStore.restore(scope, chatId)
            if (restored.isEmpty()) return@launch
            composerGenerationCounter = maxOf(
                composerGenerationCounter,
                restored.maxOf { it.generation },
            )

            // A queue row may already own the preview when the old process died between QUEUED
            // and clearAfterEnqueue. Clear only that generation, then render any newer records.
            val handoffGeneration = restored.maxOf { it.generation }
            if (outgoingMessageQueue.hasDurableHandoff(chatId, handoffGeneration)) {
                composerAttachmentStore.clearAfterEnqueue(scope, chatId, handoffGeneration)
                val remaining = composerAttachmentStore.restore(scope, chatId)
                val state = _uiState.value
                _uiState.value = if (remaining.isEmpty()) {
                    state.copy(
                        composer = composerReducer.clearAfterDurableEnqueue(
                            state.composer,
                            handoffGeneration,
                        )
                    )
                } else {
                    composerReducer.clearAfterDurableEnqueue(
                        state.composer,
                        handoffGeneration,
                        remaining,
                    ).let { next -> state.copy(composer = next) }
                }
                return@launch
            }

            val state = _uiState.value
            val ordered = restored.sortedBy { it.attachmentIndex }
            // Merge with a preview that completed while restore was reading Room; never let a
            // stale restore result remove a newly accepted path.
            val currentPaths = state.composer.attachmentPaths
            val currentKinds = state.composer.attachmentKinds
            val currentEntries = currentPaths.mapIndexed { index, path ->
                path to currentKinds.getOrNull(index).orEmpty().ifBlank { OutgoingAttachmentKind.DOCUMENT.name }
            }
            val restoredEntries = ordered.map { it.path to it.kind }
            val restoredPathSet = restoredEntries.mapTo(HashSet()) { it.first }
            val merged = restoredEntries + currentEntries.filterNot { it.first in restoredPathSet }
            _uiState.value = state.copy(
                composer = state.composer.copy(
                    attachmentPaths = merged.map { it.first },
                    attachmentKinds = merged.map { it.second },
                    draftGeneration = state.composer.draftGeneration
                        ?: ordered.maxOfOrNull { it.generation },
                )
            )
        }
    }

    private fun nextComposerGeneration(): Long {
        val next = maxOf(
            composerGenerationCounter,
            _uiState.value.composer.draftGeneration ?: 0L,
        ) + 1L
        composerGenerationCounter = next
        return next
    }

    /** Starts the chat-scoped presence subscriptions and hydrates their initial status. */
    private fun configurePresence(userIds: List<Long>) {
        if (chatId.isBlank()) return
        val normalized = userIds
            .filter { it > 0L && it != currentUserId }
            .distinct()
        if (normalized == presenceUserIds && presenceStatusJob?.isActive == true) return
        presenceUserIds = normalized
        presenceSession.start(chatId, normalized)
        presenceStatusJob?.cancel()
        if (normalized.isEmpty()) return
        presenceStatusJob = viewModelScope.launch {
            presenceGateway.status(normalized).onSuccess { statuses ->
                statuses.forEach { status ->
                    val next = chatPresence.online(
                        state = _uiState.value.presence,
                        userId = status.userId,
                        isOnline = status.isOnline,
                        lastSeenEpochMillis = status.lastSeenEpochMillis,
                    )
                    if (next != _uiState.value.presence) {
                        _uiState.value = _uiState.value.copy(presence = next)
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Загрузка сообщений и пагинация
    // ═══════════════════════════════════════════════════════════════

    private fun loadCachedMessages() {
        val scope = cacheScope ?: return
        viewModelScope.launch {
            val messages = runCatching {
                regularChatSession.cached(scope, chatId, PAGE_SIZE)
            }.getOrNull().orEmpty()
            if (messages.isEmpty()) return@launch

            displayMessages(messages)
            reconcileSelection(messages.map { it.id }.toSet())
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
                val chatInfoResult = messageGateway.chatInfo(chatId)
                if (chatInfoResult.isSuccess) {
                    val chatInfo = chatInfoResult.getOrNull()!!
                    val state = _uiState.value
                    _uiState.value = state.copy(
                        session = state.session.copy(
                            title = if (chatInfo.title.isNotBlank()) chatInfo.title else state.chatTitle,
                            avatarFileId = if (chatInfo.pictureFileId.isNotBlank()) chatInfo.pictureFileId else state.chatAvatarFileId,
                            isGroupChat = chatInfo.isGroupChat,
                            isChatMuted = chatInfo.muted,
                        )
                    )
                    firstUnreadMessageId = chatInfo.firstUnreadMessageId
                    globalParam.setChatMutedLocal(chatId, chatInfo.muted)

                    // Определяем otherUserId из участников чата (если не был передан через intent)
                    if (!chatInfo.isGroupChat && _uiState.value.otherUserId == 0L && chatInfo.memberIds.isNotEmpty()) {
                        val other = chatInfo.memberIds.firstOrNull { it != currentUserId } ?: 0L
                        if (other > 0) {
                            _uiState.value = _uiState.value.copy(
                                session = _uiState.value.session.copy(otherUserId = other)
                            )
                        }
                    }

                    val trackedPresenceUsers = if (chatInfo.isGroupChat) {
                        chatInfo.memberIds
                    } else {
                        listOf(_uiState.value.otherUserId)
                    }
                    configurePresence(trackedPresenceUsers)
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
                    messageGateway.loadMessages(
                        chatId = chatId,
                        fromMessageId = firstUnreadMessageId,
                        offsetBefore = 15,
                        offsetAfter = 30
                    )
                } else {
                    messageGateway.loadMessages(
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
                    reconcileSelection(messages.map { it.id }.toSet())

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
                    emitEffect(ChatEffect.ToastRes(R.string.messages_load_failed))
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
                val page = regularChatSession.before(cacheScope, chatId, firstVisibleMessageId, PAGE_SIZE)
                if (page.isSuccess) {
                    val messages = page.getOrThrow().messages
                    if (messages.isNotEmpty()) {
                        prependMessages(messages)
                        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
                        firstVisibleMessageId = sortedMessages.first().id
                        hasMoreMessagesUp = page.getOrThrow().hasMoreBefore
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
                val page = regularChatSession.after(cacheScope, chatId, lastVisibleMessageId, PAGE_SIZE)
                if (page.isSuccess) {
                    val messages = page.getOrThrow().messages
                    if (messages.isNotEmpty()) {
                        appendMessages(messages)
                        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
                        lastVisibleMessageId = sortedMessages.last().id
                        hasMoreMessagesDown = page.getOrThrow().hasMoreAfter
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
                    messageGateway.markAsRead(unreadMessageIds)
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
                    messageGateway.markAsRead(unreadMessageIds)
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
            val tokenValid = authGateway.ensureValid()
            if (!tokenValid) {
                Log.w(TAG, "onStartCatchUp: Token refresh failed")
                emitEffect(ChatEffect.FinishActivity)
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

        val result = messageGateway.loadMessages(
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
        val hasStagedComposerAttachments = _uiState.value.composer.attachmentPaths.isNotEmpty()
        if (messageText.isBlank() && fileIds.isEmpty() && !hasStagedComposerAttachments && replyId == 0L) return

        if (sendInFlight) return
        sendInFlight = true
        viewModelScope.launch {
            try {
                val sentDraft = chatDraftRepository.edit(chatId, messageText, replyId)
                val draftGeneration = sentDraft?.generation
                val stagedRecords = cacheScope?.let { scope ->
                    if (draftGeneration != null) {
                        composerAttachmentStore.rebindGeneration(scope, chatId, draftGeneration)
                    } else {
                        composerAttachmentStore.restore(scope, chatId)
                    }
                }.orEmpty()
                val stagedAttachments = stagedRecords.mapNotNull(::toAttachmentSpec)
                // When a local draft is unavailable, the staged attachment generation still
                // serves as the durable handoff key. This prevents a successful enqueue from
                // leaving the same preview in the composer for a second send.
                val handoffGeneration = draftGeneration ?: stagedRecords.maxOfOrNull { it.generation }
                outgoingMessageQueue.enqueue(SendJob(
                    chatId = chatId,
                    chatTitle = _uiState.value.chatTitle,
                    text = messageText,
                    attachments = stagedAttachments,
                    replyId = replyId,
                    existingFileIds = fileIds,
                    draftGeneration = handoffGeneration
                ))
                val remaining = cacheScope?.let { scope ->
                    composerAttachmentStore.restore(scope, chatId)
                }.orEmpty()
                val currentState = _uiState.value
                _uiState.value = currentState.copy(
                    composer = if (remaining.isEmpty()) {
                        composerReducer.clearAfterDurableEnqueue(
                            currentState.composer,
                            handoffGeneration,
                        )
                    } else {
                        composerReducer.clearAfterDurableEnqueue(
                            currentState.composer,
                            handoffGeneration,
                            remaining,
                        )
                    }
                )
                emitEffect(ChatEffect.SendAccepted(messageText))
            } catch (e: Exception) {
                Log.e(TAG, "Error sending message", e)
                emitEffect(ChatEffect.SendRejected)
                emitEffect(ChatEffect.ToastRes(R.string.message_send_error, e.message.orEmpty()))
            } finally {
                sendInFlight = false
            }
        }
    }

    private fun toAttachmentSpec(attachment: com.barkfluff.client.cache.ComposerAttachment): AttachmentSpec? {
        val file = File(attachment.path)
        if (!file.isFile) return null
        val kind = runCatching { OutgoingAttachmentKind.valueOf(attachment.kind) }
            .getOrDefault(OutgoingAttachmentKind.DOCUMENT)
        val type = when (kind) {
            OutgoingAttachmentKind.RAW_IMAGE,
            OutgoingAttachmentKind.EDITED_IMAGE -> UploadFileType.MESSAGE_ATTACHMENT_IMAGE
            OutgoingAttachmentKind.VIDEO -> UploadFileType.MESSAGE_ATTACHMENT_VIDEO
            OutgoingAttachmentKind.STICKER -> UploadFileType.MESSAGE_ATTACHMENT_STICKER
            OutgoingAttachmentKind.VOICE -> UploadFileType.MESSAGE_ATTACHMENT_VOICE
            OutgoingAttachmentKind.DOCUMENT -> UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
        }
        return AttachmentSpec.StagedFile(
            file = file,
            kind = kind,
            uploadFileType = type,
            fileName = attachment.fileName,
            mimeType = attachment.mimeType,
            preview = kind != OutgoingAttachmentKind.DOCUMENT && kind != OutgoingAttachmentKind.STICKER,
        )
    }

    fun enqueueMedia(job: SendJob) {
        if (sendInFlight) return
        sendInFlight = true
        viewModelScope.launch {
            try {
                val draft = chatDraftRepository.edit(job.chatId, job.text, job.replyId)
                outgoingMessageQueue.enqueue(job.copy(draftGeneration = draft?.generation))
                _uiState.value = _uiState.value.copy(
                    composer = composerReducer.clearAfterDurableEnqueue(
                        _uiState.value.composer,
                        draft?.generation,
                    )
                )
                emitEffect(ChatEffect.SendAccepted(job.text))
            } catch (e: Exception) {
                Log.e(TAG, "Unable to stage outgoing media", e)
                emitEffect(ChatEffect.SendRejected)
                emitEffect(ChatEffect.ToastRes(R.string.message_send_error, e.message.orEmpty()))
            } finally {
                sendInFlight = false
            }
        }
    }

    fun retryOutgoing(operationId: String) = viewModelScope.launch { outgoingMessageQueue.retry(operationId) }

    fun cancelOutgoing(operationId: String) = viewModelScope.launch { outgoingMessageQueue.cancel(operationId) }

    private fun sendEdit(messageId: Long, text: String) {
        val fileIds = _uiState.value.pendingEdit?.fileIds ?: emptyList()
        if (text.isBlank() && fileIds.isEmpty()) {
            emitEffect(ChatEffect.ToastRes(R.string.message_empty))
            return
        }

        viewModelScope.launch {
            val result = messageGateway.editMessage(messageId, text, fileIds)
            if (result.isSuccess) {
                clearPendingEdit()
                applyEditedMessage(result.getOrNull()!!)
            } else {
                emitEffect(
                    ChatEffect.ToastRes(R.string.message_edit_error, result.exceptionOrNull()?.message.orEmpty())
                )
            }
        }
    }

    fun deleteMessage(messageId: Long) {
        viewModelScope.launch {
            val result = messageGateway.deleteMessage(messageId)
            if (result.isSuccess) {
                removeMessageById(messageId)
            } else {
                emitEffect(
                    ChatEffect.ToastRes(R.string.message_delete_error, result.exceptionOrNull()?.message.orEmpty())
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

    fun updateComposerText(text: String) {
        _uiState.value = _uiState.value.copy(
            composer = composerReducer.textChanged(_uiState.value.composer, text)
        )
    }

    fun setPendingReply(item: MessageItem) {
        val reply = PendingReply(
                    messageId = item.messageId,
                    senderId = item.senderId,
                    senderName = item.senderName,
                    text = item.text,
                    attachments = item.attachments,
                )
        _uiState.value = _uiState.value.copy(
            composer = composerReducer.beginReply(_uiState.value.composer, reply)
        )
        savedStateHandle[KEY_PENDING_REPLY] = item.messageId
    }

    fun clearPendingReply() {
        _uiState.value = _uiState.value.copy(
            composer = composerReducer.clearReply(_uiState.value.composer)
        )
        savedStateHandle[KEY_PENDING_REPLY] = 0L
    }

    fun setPendingEdit(item: MessageItem) {
        if (_uiState.value.pendingReply != null) {
            clearPendingReply()
        }
        val edit = PendingEdit(
                    messageId = item.messageId,
                    text = item.text,
                    fileIds = item.attachments
                        .filter { it.type != Shared.MessageAttachmentType.FORWARDED_MESSAGE }
                        .map { it.fileId }
                        .filter { it.isNotBlank() },
                )
        _uiState.value = _uiState.value.copy(
            composer = composerReducer.beginEdit(_uiState.value.composer, edit)
        )
        savedStateHandle[KEY_PENDING_EDIT] = item.messageId
    }

    fun clearPendingEdit() {
        _uiState.value = _uiState.value.copy(
            composer = composerReducer.clearEdit(_uiState.value.composer)
        )
        savedStateHandle[KEY_PENDING_EDIT] = 0L
    }

    fun toggleSelection(messageId: Long) {
        val state = _uiState.value
        val selection = selectionReducer.toggle(state.selection, messageId)
        _uiState.value = state.copy(selection = selection)
        savedStateHandle[KEY_SELECTED_MESSAGE_IDS] = selection.selectedMessageIds.toLongArray()
        submitItems(state.items)
    }

    fun clearSelection() {
        val state = _uiState.value
        _uiState.value = state.copy(selection = selectionReducer.clear())
        savedStateHandle[KEY_SELECTED_MESSAGE_IDS] = LongArray(0)
        submitItems(state.items)
    }

    private fun restoreSelection() {
        val saved = savedStateHandle.get<LongArray>(KEY_SELECTED_MESSAGE_IDS)
            ?: savedStateHandle.get<ArrayList<Long>>(KEY_SELECTED_MESSAGE_IDS)?.toLongArray()
            ?: return
        val ids = saved.filter { it > 0L }.toSet()
        if (ids.isNotEmpty()) {
            _uiState.value = _uiState.value.copy(
                selection = SelectionState(ids, isActive = true)
            )
        }
    }

    private fun reconcileSelection(availableIds: Set<Long>) {
        val state = _uiState.value
        if (!state.selection.isActive) return
        val selection = selectionReducer.removeMissing(state.selection, availableIds)
        if (selection != state.selection) {
            _uiState.value = state.copy(selection = selection)
            savedStateHandle[KEY_SELECTED_MESSAGE_IDS] = selection.selectedMessageIds.toLongArray()
            submitItems(state.items)
        }
    }

    /** Сохраняет черновик (текст ввода живёт в Activity, сюда приходит значением). */
    fun saveDraft(text: String, immediate: Boolean = false) {
        if (!supportsDrafts || isRestoringDraft || _uiState.value.pendingEdit != null) return
        updateComposerText(text)
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
            _uiState.value = _uiState.value.copy(
                composer = _uiState.value.composer.copy(
                    text = draft.text,
                    draftGeneration = draft.generation,
                    isRestoring = true,
                )
            )
            emitEffect(ChatEffect.DraftRestored(draft.text))
            if (draft.replyToMessageId == 0L) {
                isRestoringDraft = false
                _uiState.value = _uiState.value.copy(
                    composer = _uiState.value.composer.copy(isRestoring = false)
                )
                return@launch
            }

            val item = _uiState.value.items.firstOrNull { it.messageId == draft.replyToMessageId }
                ?: messageGateway.loadMessages(
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
            _uiState.value = _uiState.value.copy(
                composer = _uiState.value.composer.copy(isRestoring = false)
            )
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
            ?: messageGateway.loadMessages(
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
            realtimeGateway.newMessages.collect { event ->
                if (event.chatId == chatId) {
                    val msg = event.message
                    regularChatSession.cacheMessage(cacheScope, chatId, msg)
                    addNewMessage(msg)
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.messagesRead.collect { event ->
                if (event.chatId == chatId) {
                    updateMessageReadStatus(event.messageId, event.newReadByList)
                    cacheScope?.let { scope ->
                        chatCacheRepository.updateReadBy(scope, chatId, event.messageId, event.newReadByList)
                    }
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.messageEdited.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    applyEditedMessage(event.message)
                    regularChatSession.cacheMessage(cacheScope, chatId, event.message)
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.messageDeleted.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    removeMessageById(event.messageId)
                    cacheScope?.let { scope ->
                        chatCacheRepository.deleteMessage(scope, chatId, event.messageId)
                    }
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.messagePinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onMessagePinnedRemote(event.messageId)
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.messageUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onMessageUnpinnedRemote(event.messageId)
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.allMessagesUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onAllMessagesUnpinnedRemote()
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.onlineStatuses.collect { event ->
                if (event.userId !in presenceUserIds) return@collect
                val next = chatPresence.online(
                    _uiState.value.presence,
                    event.userId,
                    event.status == barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE,
                    lastSeenEpochMillis = event.lastSeen.seconds * 1000L + event.lastSeen.nanos / 1_000_000L,
                )
                if (next != _uiState.value.presence) {
                    _uiState.value = _uiState.value.copy(presence = next)
                }
            }
        }

        viewModelScope.launch {
            realtimeGateway.typingEvents.collect { event ->
                val next = chatPresence.typing(
                    state = _uiState.value.presence,
                    chatId = chatId,
                    eventChatId = event.chatId,
                    userId = event.userId,
                    isTyping = event.action != barkfluff.onliner.OnlinerApiOuterClass.TypingAction.TYPING_ACTION_CANCELLED,
                    nowMillis = System.currentTimeMillis(),
                )
                if (next != _uiState.value.presence) {
                    _uiState.value = _uiState.value.copy(presence = next)
                }
            }
        }

        viewModelScope.launch {
            while (kotlin.coroutines.coroutineContext.isActive) {
                delay(1_000)
                val next = chatPresence.expire(_uiState.value.presence, System.currentTimeMillis())
                if (next != _uiState.value.presence) {
                    _uiState.value = _uiState.value.copy(presence = next)
                }
            }
        }
    }

    private fun subscribeToOutgoingMessages() {
        viewModelScope.launch {
            outgoingMessageQueue.observeChat(chatId).collect { snapshots ->
                latestOutgoingSnapshots = snapshots
                val active = snapshots.filter {
                    it.state != OutgoingMessageState.STAGING &&
                        it.state != OutgoingMessageState.CANCEL_REQUESTED &&
                        it.state != OutgoingMessageState.SENT
                }
                val activeIds = active.map { it.operationId }.toSet()
                val current = _uiState.value.items.toMutableList()
                current.removeAll { item ->
                    item.type == MessageType.MESSAGE && item.localId != null &&
                        item.localId in observedOutgoingOperationIds && item.localId !in activeIds
                }
                active.forEach { snapshot -> upsertOutgoingBubble(current, snapshot) }
                observedOutgoingOperationIds = activeIds
                submitItems(current)

                snapshots.filter { it.state == OutgoingMessageState.SENT && it.draftGeneration != null }
                    .filter { clearedDraftOperations.add(it.operationId) }
                    .forEach { sent ->
                        viewModelScope.launch {
                            chatDraftRepository.clearAfterSent(chatId, sent.draftGeneration!!)
                        }
                    }
                snapshots.filter { it.state == OutgoingMessageState.SENT && it.serverMessageId > 0L }
                    .forEach { sent ->
                        val scope = cacheScope ?: return@forEach
                        viewModelScope.launch {
                            chatCacheRepository.cachedMessage(scope, chatId, sent.serverMessageId)?.let(::addNewMessage)
                        }
                    }
            }
        }
    }

    private fun upsertOutgoingBubble(target: MutableList<MessageItem>, snapshot: OutgoingMessageSnapshot) {
        val item = MessageItem(
            messageId = -10_000_000_000L - (snapshot.operationId.hashCode().toLong() and 0x7fff_ffffL),
            senderId = currentUserId,
            text = snapshot.text,
            timestamp = snapshot.createdAtMillis,
            attachments = emptyList(),
            readStatus = if (snapshot.state == OutgoingMessageState.FAILED) ReadStatus.FAILED else ReadStatus.SENDING,
            type = MessageType.MESSAGE,
            localId = snapshot.operationId,
            outgoingState = snapshot.state,
            uploadProgress = snapshot.progress.takeIf { snapshot.state != OutgoingMessageState.FAILED },
            localPreviewUris = snapshot.previewPaths.map { android.net.Uri.fromFile(java.io.File(it)) }
        )
        val existing = target.indexOfFirst { it.type == MessageType.MESSAGE && it.localId == snapshot.operationId }
        if (existing >= 0) target[existing] = item else target.add(item)
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

        latestOutgoingSnapshots.filter {
            it.state != OutgoingMessageState.STAGING &&
                it.state != OutgoingMessageState.CANCEL_REQUESTED &&
                it.state != OutgoingMessageState.SENT
        }
            .forEach { snapshot -> upsertOutgoingBubble(messageItems, snapshot) }
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
            replyTo = if (msg.hasReplyTo()) msg.replyTo else null,
            clientOperationId = msg.clientOperationId.takeIf { it.isNotBlank() }
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
                    it.localId == msg.clientOperationId ||
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
                    messageGateway.markAsRead(listOf(msg.id))
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
        val result = messageGateway.loadMessages(chatId, fromMessageId = messageId, offsetBefore = 20, offsetAfter = 20)
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
            val result = messageGateway.pinnedMessages(chatId)
            if (result.isSuccess) {
                val page = result.getOrNull() ?: return@launch
                val list = page.messages
                val total = page.totalCount
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
            val result = messageGateway.pinnedMessages(chatId)
            if (result.isSuccess) {
                val page = result.getOrNull() ?: return@launch
                val list = page.messages
                val total = page.totalCount
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
        _uiState.value = _uiState.value.copy(
            session = _uiState.value.session.copy(isChatMuted = muted)
        )
    }

    /**
     * Кнопка «вниз»: подтягивает последние сообщения с сервера, если локальный хвост протух.
     * Вызывается из Activity параллельно с плавным скроллом (UX-тайминг остаётся в Activity).
     */
    suspend fun refreshLatestMessages() {
        if (isLoadingMessages) return
        val serverLastMessageId = messageGateway.chatInfo(chatId).getOrNull()?.lastMessageId ?: 0L
        if (serverLastMessageId <= 0L || serverLastMessageId == lastVisibleMessageId) return

        setLoading(true)
        hasMoreMessagesDown = false
        val result = messageGateway.loadMessages(
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
                val result = messageGateway.unpinMessage(chatId, item.messageId)
                if (result.isFailure) {
                    emitEffect(ChatEffect.ToastRes(R.string.message_unpin_failed))
                }
            } else {
                val result = messageGateway.pinMessage(chatId, item.messageId)
                if (result.isFailure) {
                    val cause = result.exceptionOrNull()
                    emitEffect(
                        if (cause is PinErrorException && cause.isTooManyPinned) {
                            ChatEffect.ToastRes(R.string.pin_limit_reached)
                        } else {
                            ChatEffect.ToastRes(R.string.message_pin_failed)
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
            session = _uiState.value.session.copy(
                pinnedCount = pinnedTotalCount,
                pinnedPreview = first?.message?.content?.text ?: "",
                firstPinnedMessageId = first?.message?.id ?: 0L,
            )
        )
    }

    // ═══════════════════════════════════════════════════════════════
    // Утилиты
    // ═══════════════════════════════════════════════════════════════

    private fun setLoading(loading: Boolean) {
        isLoadingMessages = loading
        _uiState.value = _uiState.value.copy(
            timeline = _uiState.value.timeline.copy(isLoading = loading)
        )
    }

    private fun submitItems(items: List<MessageItem>) {
        val state = _uiState.value
        val presentedItems = rowProjector.withSelection(
            rows = items,
            selectedIds = state.selection.selectedMessageIds,
            enabled = state.selection.isActive,
        )
        val messageItems = presentedItems.filter { it.type == MessageType.MESSAGE || it.type == MessageType.SYSTEM }
        _uiState.value = _uiState.value.copy(
            timeline = _uiState.value.timeline.copy(
                rows = presentedItems.toList(),
                firstVisibleMessageId = messageItems.firstOrNull()?.messageId ?: 0L,
                lastVisibleMessageId = messageItems.lastOrNull()?.messageId ?: 0L,
            )
        )
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
        presenceStatusJob?.cancel()
        if (initialized) presenceSession.stop()
        super.onCleared()
    }
}
