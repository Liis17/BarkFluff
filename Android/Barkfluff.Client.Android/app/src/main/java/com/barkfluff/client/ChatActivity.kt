package com.barkfluff.client

import barkfluff.calls.CallsApiOuterClass
import android.Manifest
import android.animation.ValueAnimator
import android.app.Activity
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.ColorStateList
import android.graphics.Typeface
import android.media.MediaRecorder
import android.net.Uri
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.util.Log
import android.util.TypedValue
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.animation.DecelerateInterpolator
import android.view.animation.PathInterpolator
import androidx.core.animation.doOnEnd
import android.widget.TextView
import android.widget.Toast
import androidx.core.content.FileProvider
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.LinearSmoothScroller
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.adapter.ReadStatus
import com.barkfluff.client.calls.CallActivity
import com.barkfluff.client.calls.CallExtras
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.ActivityChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.adapter.StickerPanelAdapter
import com.barkfluff.client.adapter.StickerPanelItem
import com.barkfluff.client.picker.ImagePickerBottomSheet
import com.barkfluff.client.picker.ImagePickerResult
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.FileSaveUtils
import com.barkfluff.client.utils.ImageCompressor
import com.barkfluff.client.utils.KeyboardHeightTracker
import com.barkfluff.client.utils.StickerCache
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.MarkdownRenderer
import com.barkfluff.client.utils.MessageItemAnimator
import com.barkfluff.client.utils.MessageTimeSpacingDecoration
import com.barkfluff.client.utils.OnlineTimeFormatter
import com.barkfluff.client.utils.applySpringPress
import com.barkfluff.client.view.MessageActionsOverlay
import com.google.android.material.color.MaterialColors
import com.yalantis.ucrop.UCrop
import androidx.activity.OnBackPressedCallback
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.core.view.updateLayoutParams
import androidx.core.view.updatePadding
import androidx.recyclerview.widget.GridLayoutManager
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import coil.load
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import javax.inject.Inject

/**
 * Activity для отображения чата и переписки.
 * Поддерживает:
 * - Отображение сообщений с пагинацией (подгрузка по скролу)
 * - Отправку текстовых сообщений и изображений
 * - Отображение статуса онлайна собеседника
 * - Профиль чата по клику на кнопку информации
 */
@AndroidEntryPoint
class ChatActivity : AppCompatActivity() {

    private lateinit var binding: ActivityChatBinding
    private val viewModel: ChatViewModel by viewModels()

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var realtimeService: RealtimeService

    @Inject lateinit var chatRepository: ChatRepository
    private lateinit var messageAdapter: MessageAdapter

    // Кэш рендера из ChatViewModel.uiState: шапка/звонки/меню читают эти значения синхронно.
    private var chatId: String = ""
    private var chatTitle: String = ""
    private var isChatMuted: Boolean = false
    private var chatAvatarFileId: String? = null
    private var isGroupChat: Boolean = false
    private var otherUserId: Long = 0L
    private var currentUserId: Long = 0L
    private var supportsDrafts: Boolean = false
    private var chatBackgroundLoadVersion = 0

    // Кэш информации об участниках группы для рендера аватарок/имён чужих сообщений: senderId -> (имя, URL/fileId аватара)
    private val groupMemberInfoCache = HashMap<Long, Pair<String?, String?>>()

    // Индикатор набора текста ("печатает...")
    private var typingHeartbeatJob: Job? = null
    @Volatile private var lastTypingInputAt = 0L
    private var suppressTypingInput = false
    private val typingUsers = LinkedHashMap<Long, Job>()
    private val pendingTypingNameFetches = mutableSetOf<Long>()
    private var lastStatusText: CharSequence? = null
    private var lastIndicatorVisible = false

    // Сжатие шапки при прокрутке в историю сообщений
    private var headerCompact = false
    private var statusExpandedHeight = 0
    private var headerNameAnimator: ValueAnimator? = null
    private var headerStatusAnimator: ValueAnimator? = null

    // Морфинг кнопки отправки (круг с микрофоном ↔ pill со стрелкой)
    private val sendButtonBackground = android.graphics.drawable.GradientDrawable().apply {
        shape = android.graphics.drawable.GradientDrawable.RECTANGLE
    }
    private var sendButtonMorphAnimator: ValueAnimator? = null
    private var sendButtonWide: Boolean? = null

    // Вставленные из буфера обмена изображения
    private val pendingPastedImages = mutableListOf<Uri>()
    private val pendingStickerUris = mutableListOf<Uri>()
    private val pendingDocumentUris = mutableListOf<Uri>()
    private val pendingCropQueue = ArrayDeque<Uri>()

    // Голосовые сообщения
    private var sendButtonVoiceMode = false
    private var voiceRecorder: MediaRecorder? = null
    private var voiceRecordingFile: File? = null
    private var voiceRecordingStartedAtMs = 0L
    private var voiceDownRawX = 0f
    private var voiceCancelPending = false
    private var voiceTimerJob: Job? = null
    private var voiceDotAnimator: ValueAnimator? = null

    // Программное изменение поля ввода не должно триггерить сохранение черновика
    private var suppressDraftSave = false

    // Inline стикер-панель
    private enum class InputPanelState { NONE, KEYBOARD, STICKER_PANEL }
    private var inputPanelState = InputPanelState.NONE
    private var lastKnownKeyboardHeight = 0
    private var isTransitioningToStickers = false
    private var stickerDataLoaded = false
    private lateinit var stickerPanelAdapter: StickerPanelAdapter

    // Callback назад — включён только когда стикер-панель или оверлей открыты
    private lateinit var backCallback: OnBackPressedCallback

    // Оверлей меню действий над сообщением (Telegram-стиль)
    private lateinit var messageActionsOverlay: MessageActionsOverlay

    // Режим множественного выделения сообщений
    private val selectedMessageIds = mutableSetOf<Long>()

    private val pasteUCropLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK && result.data != null) {
            val croppedUri = UCrop.getOutput(result.data!!)
            if (croppedUri != null) {
                pendingPastedImages.add(croppedUri)
                updateAttachmentPreview()
            }
        }
        processNextCropFromQueue()
    }

    private val recordAudioPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        val messageRes = if (granted) {
            R.string.voice_record_permission_granted
        } else {
            R.string.voice_record_permission_denied
        }
        Toast.makeText(this, messageRes, Toast.LENGTH_SHORT).show()
    }

    companion object {
        private const val TAG = "ChatActivity"
        private const val EXTRA_CHAT_ID = "chat_id"
        private const val EXTRA_CHAT_TITLE = "chat_title"
        private const val EXTRA_CHAT_AVATAR_FILE_ID = "chat_avatar_file_id"
        private const val EXTRA_IS_GROUP_CHAT = "is_group_chat"
        private const val EXTRA_OTHER_USER_ID = "other_user_id"
        private const val EXTRA_CHAT_KIND = "chat_kind"
        private const val EXTRA_INVITE_STATE = "invite_state"
        private const val EXTRA_INVITER_USER_ID = "inviter_user_id"
        private const val EXTRA_INITIAL_MESSAGE = "initial_message"
        /** Метка ClipData — техническая, пользователю не показывается. */
        private const val CLIP_LABEL = "BarkFluff message"
        private const val CLIP_LABEL_MULTIPLE = "BarkFluff messages"
        private const val LOAD_MESSAGES_DELAY_MS = 500L
        private const val MIN_VOICE_RECORDING_MS = 500L
        private const val VOICE_BAR_FADE_DURATION_MS = 160L
        private const val VOICE_DOT_BLINK_DURATION_MS = 700L
        private const val VOICE_TIMER_TICK_MS = 200L
        /** Доля смещения пальца, на которую уезжает подсказка отмены. */
        private const val VOICE_HINT_DRAG_RATIO = 0.35f
        private const val HEADER_MORPH_DURATION_MS = 280L
        private const val SEND_BUTTON_MORPH_DURATION_MS = 300L
        private const val SEND_BUTTON_NARROW_DP = 52f
        private const val SEND_BUTTON_WIDE_DP = 68f
        private const val SEND_BUTTON_NARROW_CORNER_DP = 26f
        private const val SEND_BUTTON_WIDE_CORNER_DP = 18f
        private const val HEADER_SHADOW_DURATION_MS = 250L
        private const val HEADER_SHADOW_ELEVATION_DP = 4f
        /** Минимальный шаг прокрутки, меняющий состояние шапки. */
        private const val HEADER_SCROLL_THRESHOLD_DP = 6

        // Тип чата, отображаемого в этом Activity.
        const val KIND_REGULAR = 0
        const val KIND_PRIVATE = 1
        const val KIND_SECRET = 2

        /** Intent для приватного E2E-чата в общем ChatActivity. inviteState<0 = определить из ListChats. */
        fun privateChatIntent(
            context: Context,
            chatId: String,
            title: String,
            inviteState: Int = -1,
            inviterUserId: Long = 0L
        ): Intent = Intent(context, ChatActivity::class.java).apply {
            putExtra(EXTRA_CHAT_KIND, KIND_PRIVATE)
            putExtra(EXTRA_CHAT_ID, chatId)
            putExtra(EXTRA_CHAT_TITLE, title)
            putExtra(EXTRA_INVITE_STATE, inviteState)
            putExtra(EXTRA_INVITER_USER_ID, inviterUserId)
        }

        /** Intent для секретного E2E-чата в общем ChatActivity. */
        fun secretChatIntent(
            context: Context,
            secretChatId: String,
            initialMessage: String? = null
        ): Intent = Intent(context, ChatActivity::class.java).apply {
            putExtra(EXTRA_CHAT_KIND, KIND_SECRET)
            putExtra(EXTRA_CHAT_ID, secretChatId)
            putExtra(EXTRA_INITIAL_MESSAGE, initialMessage)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        val newChatId = intent.getStringExtra(EXTRA_CHAT_ID)
        if (newChatId != null && newChatId != chatId) {
            // Другой чат — пересоздаём activity с новым intent. ViewModelStore переживает
            // recreate(), поэтому очищаем его явно: иначе новый чат получит VM со состоянием
            // и подписками старого (realtime-коллекторы, оптимистика, пагинация).
            setIntent(intent)
            viewModelStore.clear()
            recreate()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityChatBinding.inflate(layoutInflater)
        setContentView(binding.root)
        messageActionsOverlay = MessageActionsOverlay(binding.chatRootLayout)

        val app = application as BarkFluffApplication
        globalParam = GlobalParam(this)
        grpcManager = app.grpcManager
        realtimeService = app.realtimeService

        // Получаем данные из intent
        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run {
            finish()
            return
        }
        chatTitle = intent.getStringExtra(EXTRA_CHAT_TITLE) ?: ""
        chatAvatarFileId = intent.getStringExtra(EXTRA_CHAT_AVATAR_FILE_ID)
        isGroupChat = intent.getBooleanExtra(EXTRA_IS_GROUP_CHAT, false)
        otherUserId = intent.getLongExtra(EXTRA_OTHER_USER_ID, 0L)
        currentUserId = globalParam.userId

        // E2E-чаты (приватный/секретный) рендерятся в облегчённом shell того же Activity.
        val chatKind = intent.getIntExtra(EXTRA_CHAT_KIND, KIND_REGULAR)
        if (chatKind != KIND_REGULAR) {
            setupE2eShell(chatKind)
            return
        }
        supportsDrafts = true

        Log.d(TAG, "ChatActivity created: chatId=$chatId, title=$chatTitle, isGroupChat=$isGroupChat, otherUserId=$otherUserId")

        setupWindowInsets()
        setupToolbar()
        setupMessagesRecyclerView()
        setupMessageInput()
        setupStickerPanel()
        setupKeyboardTracking()
        setupChatBackground()
        setupPinnedBar()
        viewModel.initialize(
            chatId = chatId,
            title = chatTitle,
            avatarFileId = chatAvatarFileId,
            isGroupChat = isGroupChat,
            otherUserId = otherUserId,
            supportsDrafts = supportsDrafts,
        )
        observeViewModel()

        // Устанавливаем этот чат как открытый
        OpenChatManager.setOpenChat(chatId)

        // Убираем уведомление этого чата из шторки если оно висит
        NotificationHelper.dismissForChat(applicationContext, chatId)

        // Подписка на индикатор набора текста в этом чате (online-статус и typing — UI-слой)
        realtimeService.changeTypingSubscription(listOf(chatId))
        subscribeToTypingEvents()
        if (!isGroupChat && otherUserId > 0) {
            realtimeService.changeOnlineSubscription(listOf(otherUserId))
            loadOnlineStatus(otherUserId)
        }
    }

    /** Индикатор «печатает…»: realtime typing-события → анимация шапки (UI-слой). */
    private fun subscribeToTypingEvents() {
        lifecycleScope.launch {
            realtimeService.typingEvents.collect { event ->
                if (!event.chatId.equals(chatId, ignoreCase = true)) return@collect
                if (event.userId == currentUserId) return@collect
                if (event.action == barkfluff.onliner.OnlinerApiOuterClass.TypingAction.TYPING_ACTION_CANCELLED) {
                    typingUsers.remove(event.userId)?.cancel()
                } else {
                    typingUsers.remove(event.userId)?.cancel()
                    typingUsers[event.userId] = lifecycleScope.launch {
                        delay(6_000)
                        typingUsers.remove(event.userId)
                        renderTypingIndicator()
                    }
                }
                renderTypingIndicator()
            }
        }
    }

    /**
     * Рендер состояния ChatViewModel: список сообщений, шапка, закрепы, режимы reply/edit,
     * прогресс загрузки. Список подаётся в MessageAdapter через DiffUtil; появление
     * разделителя непрочитанных или рост хвоста своими сообщениями управляет прокруткой.
     */
    private fun observeViewModel() {
        lifecycleScope.launch {
            var previousItems: List<MessageItem> = emptyList()
            var previousState: ChatUiState? = null
            var previousTitle: String? = null
            var previousAvatarFileId: String? = null
            var previousPinnedCount = -1
            var previousPinnedPreview: String? = null
            var observedOnlineUserId = 0L
            viewModel.uiState.collect { state ->
                // Кэш значений для синхронных читателей (шапка, звонки, меню, forward)
                chatTitle = state.chatTitle
                chatAvatarFileId = state.chatAvatarFileId
                isGroupChat = state.isGroupChat
                isChatMuted = state.isChatMuted
                otherUserId = state.otherUserId

                if (previousTitle != state.chatTitle) {
                    previousTitle = state.chatTitle
                    binding.chatNameTextView.text = state.chatTitle.ifBlank { getString(R.string.chat_default_title) }
                }
                if (previousAvatarFileId != state.chatAvatarFileId) {
                    previousAvatarFileId = state.chatAvatarFileId
                    loadChatAvatar()
                }

                // otherUserId выяснился из chatInfo — подписываемся на онлайн-статус
                if (!state.isGroupChat && state.otherUserId > 0 && state.otherUserId != observedOnlineUserId) {
                    observedOnlineUserId = state.otherUserId
                    realtimeService.changeOnlineSubscription(listOf(state.otherUserId))
                    loadOnlineStatus(state.otherUserId)
                    binding.onlineStatusTextView.visibility = View.VISIBLE
                }

                // Сообщения
                val wasAtBottom = isRecyclerViewAtBottom()
                val unreadAppeared = state.items.any { it.type == MessageType.UNREAD_SEPARATOR } &&
                    previousItems.none { it.type == MessageType.UNREAD_SEPARATOR }
                val grewAtTail = state.items.size > previousItems.size
                val lastMessage = state.items.lastOrNull { it.type == MessageType.MESSAGE }
                val ownAtTail = lastMessage?.senderId == currentUserId

                if (state.items != previousItems) {
                    messageAdapter.submitList(state.items) {
                        if (unreadAppeared) {
                            val idx = messageAdapter.currentList.indexOfFirst { it.type == MessageType.UNREAD_SEPARATOR }
                            if (idx >= 0) {
                                (binding.messagesRecyclerView.layoutManager as LinearLayoutManager)
                                    .scrollToPositionWithOffset(idx, 0)
                            }
                        } else if (grewAtTail && (ownAtTail || wasAtBottom)) {
                            val lastIdx = messageAdapter.itemCount - 1
                            if (lastIdx >= 0) binding.messagesRecyclerView.scrollToPosition(lastIdx)
                        }
                        updateScrollToBottomButton()
                    }
                    previousItems = state.items
                } else {
                    updateScrollToBottomButton()
                }

                binding.loadingProgress.visibility = if (state.isLoading) View.VISIBLE else View.GONE

                // Reply / Edit
                if (previousState?.pendingReply != state.pendingReply) {
                    renderPendingReply(state.pendingReply)
                }
                if (previousState?.pendingEdit != state.pendingEdit) {
                    renderPendingEdit(previousState?.pendingEdit, state.pendingEdit)
                }

                // Закрепы
                if (previousPinnedCount != state.pinnedCount || previousPinnedPreview != state.pinnedPreview) {
                    previousPinnedCount = state.pinnedCount
                    previousPinnedPreview = state.pinnedPreview
                    updatePinnedBar(state.pinnedCount, state.pinnedPreview)
                }

                previousState = state
            }
        }

        lifecycleScope.launch {
            viewModel.events.collect { event ->
                when (event) {
                    is ChatEvent.ToastRes -> {
                        val text = if (event.formatArg != null) {
                            getString(event.resId, event.formatArg)
                        } else {
                            getString(event.resId)
                        }
                        Toast.makeText(this@ChatActivity, text, Toast.LENGTH_SHORT).show()
                    }
                    is ChatEvent.DraftRestored -> {
                        suppressDraftSave = true
                        suppressTypingInput = true
                        binding.messageEditText.setText(event.text)
                        suppressTypingInput = false
                        suppressDraftSave = false
                        updateSendButtonMode()
                    }
                    ChatEvent.FinishActivity -> finish()
                }
            }
        }
    }

    private fun renderPendingReply(reply: PendingReply?) {
        if (reply == null) {
            binding.replyPreviewBar.visibility = View.GONE
        } else {
            val author = reply.senderName?.takeIf { it.isNotBlank() }
                ?: if (reply.senderId == currentUserId) getString(R.string.current_user) else getString(R.string.message_placeholder)
            val preview = if (reply.text.isNotBlank()) {
                reply.text
            } else {
                buildAttachmentSummary(reply.attachments)
            }
            binding.replyPreviewAuthorText.text = getString(R.string.reply_to_author, author)
            binding.replyPreviewContentText.text = preview
            binding.replyPreviewBar.visibility = View.VISIBLE
        }
        updateSendButtonMode()
    }

    private fun renderPendingEdit(previous: PendingEdit?, current: PendingEdit?) {
        if (current != null) {
            binding.editPreviewContentText.text =
                if (current.text.isNotBlank()) current.text else buildAttachmentSummaryFromIds(current)
            binding.editPreviewBar.visibility = View.VISIBLE
            // Синхронизируем поле ввода с редактируемым текстом (и при restore после process death)
            suppressDraftSave = true
            suppressTypingInput = true
            binding.messageEditText.setText(current.text)
            suppressTypingInput = false
            suppressDraftSave = false
            binding.messageEditText.setSelection(binding.messageEditText.text?.length ?: 0)
            binding.messageEditText.requestFocus()
            WindowInsetsControllerCompat(window, binding.chatRootLayout).show(WindowInsetsCompat.Type.ime())
        } else {
            binding.editPreviewBar.visibility = View.GONE
            if (previous != null) {
                // Правка отправлена — очищаем поле ввода (паритет со старым sendEdit)
                suppressDraftSave = true
                binding.messageEditText.text?.clear()
                suppressDraftSave = false
            }
        }
        updateSendButtonMode()
    }

    /** Краткое описание вложений для edit-превью, когда fileIds есть, а attachments нет. */
    private fun buildAttachmentSummaryFromIds(edit: PendingEdit): String {
        return if (edit.fileIds.isEmpty()) "" else getString(R.string.attachment_preview)
    }

    /**
     * Облегчённый setup для E2E-чатов (приватный/секретный) в общем shell ChatActivity.
     * Переиспользует layout/шапку/MessageAdapter обычного чата, но только текст: прячет
     * вложения/стикеры/голос/меню/звонок/закреплённые и не поднимает подписки обычного чата.
     * Логика загрузки/отправки/realtime делегируется контроллеру по типу чата.
     */
    private fun setupE2eShell(chatKind: Int) {
        setupWindowInsets()

        // Шапка: только «назад» + имя/аватар-плейсхолдер.
        binding.btnBack.setOnClickListener { finish() }
        binding.btnMore.visibility = View.GONE
        binding.btnAudioCall.visibility = View.GONE
        binding.pinnedMessageBar.visibility = View.GONE
        binding.onlineStatusTextView.visibility = View.GONE
        binding.chatInfoCard.isClickable = false
        binding.chatNameTextView.text = chatTitle
        binding.chatAvatarPlaceholder.text = chatTitle.trim().firstOrNull()?.uppercase() ?: "?"

        // Ввод: только текст.
        binding.attachButton.visibility = View.GONE
        binding.stickerButton.visibility = View.GONE
        binding.stickerPanelContainer.visibility = View.GONE

        // Адаптер без вложений и меню действий (все callback'и — дефолтные no-op).
        val e2eAdapter = MessageAdapter(
            currentUserId = currentUserId,
            isGroupChat = false,
            messageCornerRadiusDp = globalParam.chatMessageCornerRadius,
            stickerSizeDp = globalParam.chatStickerSizeDp
        )
        messageAdapter = e2eAdapter
        binding.messagesRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@ChatActivity).apply { stackFromEnd = true }
            adapter = e2eAdapter
            itemAnimator = MessageItemAnimator()
        }

        val app = application as BarkFluffApplication
        when (chatKind) {
            KIND_PRIVATE -> {
                val inviteState = intent.getIntExtra(EXTRA_INVITE_STATE, -1)
                val inviterUserId = intent.getLongExtra(EXTRA_INVITER_USER_ID, 0L)
                PrivateChatController(this, binding, e2eAdapter, app, globalParam, chatId)
                    .start(inviteState, inviterUserId)
            }
            KIND_SECRET -> {
                val chat = app.secretChatRepository.getChat(chatId)
                if (chat == null) {
                    Toast.makeText(this, R.string.secret_chat_not_found, Toast.LENGTH_LONG).show()
                    finish()
                    return
                }
                binding.chatNameTextView.text = getString(R.string.secret_chat_title, chat.peerUserId)
                binding.chatAvatarPlaceholder.text = "🔒"
                SecretChatController(this, binding, e2eAdapter, app, globalParam, chat)
                    .start(intent.getStringExtra(EXTRA_INITIAL_MESSAGE))
            }
        }

        OpenChatManager.setOpenChat(chatId)
        NotificationHelper.dismissForChat(applicationContext, chatId)
    }

    // targetSdk 35+ форсирует edge-to-edge, fitsSystemWindows="true" в XML больше не работает —
    // системные бары вручную резервируем через insets, чтобы верхняя/нижняя панели и
    // recyclerview не оказывались под статус-баром / жестовой навигацией.
    private fun setupWindowInsets() {
        WindowCompat.setDecorFitsSystemWindows(window, false)

        val headerBasePaddingTop = binding.chatHeaderBar.paddingTop
        val selectionBasePaddingTop = binding.selectionToolbar.paddingTop

        val sendBaseMargin = (binding.sendButton.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        val inputBaseMargin = (binding.inputBar.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        val recyclerBasePaddingTop = binding.messagesRecyclerView.paddingTop
        val recyclerBasePaddingBottom = binding.messagesRecyclerView.paddingBottom
        // Базовая высота полосы ввода. При многострочном тексте inputBar становится выше,
        // поэтому нижний отступ ленты пересчитывается по фактической высоте контейнера.
        val inputRowBandBasePx = binding.inputRowBottom.layoutParams.height
        var lastBottomInset = 0

        fun updateRecyclerBottomPadding() {
            val inputContentHeight = maxOf(
                binding.inputBar.height + inputBaseMargin,
                binding.sendButton.height + sendBaseMargin
            )
            val inputRowBandPx = maxOf(inputRowBandBasePx, inputContentHeight)
            binding.messagesRecyclerView.updatePadding(
                bottom = recyclerBasePaddingBottom + inputRowBandPx + lastBottomInset
            )
        }

        ViewCompat.setOnApplyWindowInsetsListener(binding.chatRootLayout) { _, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            val bottomInset = maxOf(bars.bottom, ime.bottom)
            lastBottomInset = bottomInset

            // Статус-бар резервирует сама шапка — лента начинается уже под ней.
            binding.chatHeaderBar.updatePadding(top = headerBasePaddingTop + bars.top)
            // Панель режима выделения подменяет шапку и должна так же не залезать под статус-бар.
            binding.selectionToolbar.updatePadding(top = selectionBasePaddingTop + bars.top)
            // Recyclerview остаётся edge-to-edge снизу — фон/обои ленты уходят под панель
            // ввода и жестовую навигацию, а контент просто не долистывается ниже paddingBottom.
            binding.messagesRecyclerView.updatePadding(top = recyclerBasePaddingTop)
            updateRecyclerBottomPadding()

            binding.sendButton.updateLayoutParams<ViewGroup.MarginLayoutParams> { bottomMargin = sendBaseMargin + bottomInset }
            binding.inputBar.updateLayoutParams<ViewGroup.MarginLayoutParams> { bottomMargin = inputBaseMargin + bottomInset }

            insets
        }

        binding.inputBar.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
            updateRecyclerBottomPadding()
        }

        // Лента edge-to-edge уходит под шапку и статус-бар: верхний отступ ленты
        // и область блюр-копии обоев следуют за текущей высотой шапки
        // (инсеты статус-бара, сжатие шапки в компакт-режиме при прокрутке).
        binding.chatHeaderBar.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
            val headerBottom = binding.chatHeaderBar.bottom
            binding.messagesRecyclerView.updatePadding(top = recyclerBasePaddingTop + headerBottom)
            binding.chatHeaderBlurImage.clipBounds =
                android.graphics.Rect(0, 0, binding.chatHeaderBlurImage.width, headerBottom)
        }
    }

    /**
     * В отличие от списка чатов, шапка чата сжимается при прокрутке **вверх** (в историю):
     * имя уменьшается, строка статуса схлопывается, под шапкой появляется тень.
     */
    private fun updateHeaderCompact(dy: Int) {
        val threshold = HEADER_SCROLL_THRESHOLD_DP * resources.displayMetrics.density
        when {
            dy < -threshold -> setHeaderCompact(true)
            dy > threshold -> setHeaderCompact(false)
        }
    }

    private fun setHeaderCompact(compact: Boolean) {
        if (headerCompact == compact) return
        headerCompact = compact
        val density = resources.displayMetrics.density
        val easing = PathInterpolator(0.2f, 0f, 0f, 1f)

        val nameView = binding.chatNameTextView
        val nameFrom = nameView.textSize / resources.displayMetrics.scaledDensity
        headerNameAnimator?.cancel()
        headerNameAnimator = ValueAnimator.ofFloat(nameFrom, if (compact) 17f else 22f).apply {
            duration = HEADER_MORPH_DURATION_MS
            interpolator = easing
            addUpdateListener {
                nameView.setTextSize(TypedValue.COMPLEX_UNIT_SP, it.animatedValue as Float)
            }
            start()
        }
        nameView.typeface = Typeface.create(Typeface.SANS_SERIF, if (compact) 500 else 600, false)

        val statusContainer = binding.chatStatusContainer
        if (statusExpandedHeight == 0) statusExpandedHeight = statusContainer.height
        if (statusExpandedHeight > 0) {
            headerStatusAnimator?.cancel()
            headerStatusAnimator = ValueAnimator.ofInt(
                statusContainer.height,
                if (compact) 0 else statusExpandedHeight
            ).apply {
                duration = HEADER_MORPH_DURATION_MS
                interpolator = easing
                addUpdateListener { animator ->
                    val value = animator.animatedValue as Int
                    statusContainer.updateLayoutParams { height = value }
                    statusContainer.alpha = value.toFloat() / statusExpandedHeight
                }
                doOnEnd {
                    if (compact) return@doOnEnd
                    statusContainer.updateLayoutParams { height = ViewGroup.LayoutParams.WRAP_CONTENT }
                }
                start()
            }
        }

        binding.chatHeaderBar.animate()
            .translationZ(if (compact) HEADER_SHADOW_ELEVATION_DP * density else 0f)
            .setDuration(HEADER_SHADOW_DURATION_MS)
            .start()
    }

    private fun setupToolbar() {
        binding.chatNameTextView.text = chatTitle.ifBlank { getString(R.string.chat_default_title) }

        // Показываем статус онлайна только для личных чатов
        if (!isGroupChat && otherUserId > 0) {
            binding.onlineStatusTextView.text = getString(R.string.chat_status_loading)
            // Загрузка статуса онлайна будет в loadChatInfo()
        } else {
            binding.onlineStatusTextView.visibility = View.GONE
        }

        // Загрузка аватара чата
        loadChatAvatar()

        // Кнопка назад
        binding.btnBack.setOnClickListener {
            finish()
        }

        // Кнопка меню (три точки) — контекстное меню чата
        binding.btnMore.setOnClickListener { showChatMenu(it) }

        binding.btnAudioCall.applySpringPress()
        binding.btnAudioCall.setOnClickListener { startCall(video = false) }

        // Панель режима выделения сообщений
        binding.btnExitSelection.setOnClickListener { exitSelectionMode() }
        binding.btnSelectionCopy.setOnClickListener { copySelectedMessages() }
        binding.btnSelectionForward.setOnClickListener { forwardSelectedMessages() }
        binding.btnSelectionDelete.setOnClickListener { confirmAndDeleteSelected() }

        // Клик на карточку с информацией о чате (аватар + имя):
        // для групп — управление группой, для ЛС — профиль пользователя.
        binding.chatInfoCard.setOnClickListener {
            if (isGroupChat) {
                startActivity(
                    GroupInfoActivity.createIntent(
                        this,
                        chatId = chatId,
                        chatTitle = chatTitle,
                        chatAvatarFileId = chatAvatarFileId
                    )
                )
            } else {
                startActivity(
                    UserProfileActivity.createIntent(
                        this,
                        chatId = chatId,
                        otherUserId = otherUserId,
                        isGroupChat = isGroupChat,
                        chatTitle = chatTitle,
                        chatAvatarFileId = chatAvatarFileId
                    )
                )
            }
        }
    }


    private fun startCall(video: Boolean) {
        lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch

            val app = application as BarkFluffApplication
            val mediaType = if (video) {
                CallsApiOuterClass.CallMediaType.CALL_MEDIA_VIDEO
            } else {
                CallsApiOuterClass.CallMediaType.CALL_MEDIA_AUDIO
            }

            val result = if (isGroupChat) {
                app.callRepository.initiateGroup(chatId, mediaType)
            } else {
                if (otherUserId <= 0L) {
                    Toast.makeText(this@ChatActivity, R.string.chat_call_user_missing, Toast.LENGTH_SHORT).show()
                    return@launch
                }
                app.callRepository.initiateDirect(otherUserId, mediaType)
            }

            result.onSuccess { response ->
                startActivity(Intent(this@ChatActivity, CallActivity::class.java).apply {
                    putExtra(CallExtras.EXTRA_CALL_ID, response.callId)
                    putExtra(CallExtras.EXTRA_CALLER_NAME, chatTitle)
                    putExtra(CallExtras.EXTRA_CHAT_ID, chatId)
                    putExtra(CallExtras.EXTRA_MEDIA_TYPE, if (video) "video" else "audio")
                    putExtra(CallExtras.EXTRA_LIVEKIT_URL, response.livekitUrl.ifBlank { globalParam.livekitUrl })
                    putExtra(CallExtras.EXTRA_ACCESS_TOKEN, response.accessToken)
                })
            }.onFailure { error ->
                Log.e(TAG, "Failed to start call", error)
                Toast.makeText(this@ChatActivity, R.string.call_start_failed, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) {
            Toast.makeText(this, R.string.call_server_not_configured, Toast.LENGTH_SHORT).show()
            return false
        }

        val result = app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true)
        if (result.isFailure) {
            Toast.makeText(this, R.string.call_server_connection_failed, Toast.LENGTH_SHORT).show()
        }
        return result.isSuccess
    }

    private fun loadChatAvatar() {
        if (!chatAvatarFileId.isNullOrBlank()) {
            val fileId = chatAvatarFileId!!
            AvatarLoader.loadByFileId(
                imageView = binding.chatAvatar,
                placeholderView = binding.chatAvatarPlaceholder,
                fileId = fileId,
                displayName = chatTitle,
                userId = chatId.hashCode().toLong(),
                size = 80
            ) {
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            }
        } else {
            AvatarLoader.showPlaceholder(binding.chatAvatarPlaceholder, chatTitle, chatId.hashCode().toLong())
            binding.chatAvatar.visibility = View.GONE
        }
    }

    /**
     * Загружает и отображает фон чата из кэша или по URL.
     * На API 31+ применяет RenderEffect blur, на старых — ScriptIntrinsicBlur (RenderScript).
     */
    private fun applyDimOverlay() {
        val pct = globalParam.chatBackgroundDim
        if (pct == 0) {
            binding.chatDimOverlay.visibility = View.GONE
        } else {
            val alpha = (pct / 100f * 255).toInt().coerceIn(0, 255)
            // Используем цвет фона окна из темы (светлый/тёмный в зависимости от темы)
            val typedValue = android.util.TypedValue()
            theme.resolveAttribute(android.R.attr.colorBackground, typedValue, true)
            val bgColor = typedValue.data
            val dimColor = android.graphics.Color.argb(
                alpha,
                android.graphics.Color.red(bgColor),
                android.graphics.Color.green(bgColor),
                android.graphics.Color.blue(bgColor)
            )
            binding.chatDimOverlay.setBackgroundColor(dimColor)
            binding.chatDimOverlay.visibility = View.VISIBLE
        }
    }

    /** Скруглённый чип-фон для блоков шапки (back / инфо / действия) — радиус больше половины
     *  высоты любого блока, поэтому GradientDrawable сам клэмпит его до pill/circle формы. */
    private fun headerChipDrawable(color: Int): android.graphics.drawable.GradientDrawable =
        android.graphics.drawable.GradientDrawable().apply {
            shape = android.graphics.drawable.GradientDrawable.RECTANGLE
            cornerRadius = 999f * resources.displayMetrics.density
            setColor(color)
        }

    private fun setupChatBackground() {
        val loadVersion = ++chatBackgroundLoadVersion
        val fileId = globalParam.chatBackgroundFileIdFor(chatId)
        applyDimOverlay()
        if (fileId.isBlank()) {
            binding.chatBackgroundImage.visibility = View.GONE
            binding.chatHeaderBlurImage.visibility = View.GONE
            binding.chatHeaderBar.setBackgroundColor(
                com.google.android.material.color.MaterialColors.getColor(
                    binding.chatHeaderBar, com.google.android.material.R.attr.colorSurface
                )
            )
            binding.btnBack.background = null
            binding.chatInfoCard.background = null
            binding.headerActionsCluster.background = null
            return
        }

        binding.chatBackgroundImage.visibility = View.VISIBLE
        // Обои просвечивают через блюр-копию (chatHeaderBlurImage) во всех зазорах шапки —
        // сама шапка прозрачна, а имя/время читаются за счёт отдельных чипов-подложек
        // на каждом блоке (back / инфо / действия), как в Telegram.
        binding.chatHeaderBar.background = null
        val surfaceColor = com.google.android.material.color.MaterialColors.getColor(
            binding.chatHeaderBar, com.google.android.material.R.attr.colorSurface
        )
        val chipColor = android.graphics.Color.argb(
            (255 * 0.7f).toInt(),
            android.graphics.Color.red(surfaceColor),
            android.graphics.Color.green(surfaceColor),
            android.graphics.Color.blue(surfaceColor)
        )
        binding.btnBack.background = headerChipDrawable(chipColor)
        binding.chatInfoCard.background = headerChipDrawable(chipColor)
        binding.headerActionsCluster.background = headerChipDrawable(chipColor)
        binding.chatHeaderBlurImage.visibility = View.VISIBLE
        val applyBlur = globalParam.chatBackgroundBlur

        lifecycleScope.launch {
            // Сначала пробуем из дискового кэша
            val cachedFile = withContext(Dispatchers.IO) { FileCache.getFile(fileId) }
            if (cachedFile != null && cachedFile.exists()) {
                if (loadVersion == chatBackgroundLoadVersion) {
                    applyBackgroundFromFile(cachedFile, applyBlur, loadVersion)
                    applyHeaderBlur(cachedFile, loadVersion)
                }
                return@launch
            }
            // Иначе скачиваем через Files API
            val url = withContext(Dispatchers.IO) {
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            } ?: return@launch

            if (loadVersion != chatBackgroundLoadVersion) return@launch

            applyHeaderBlur(url, loadVersion)

            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
                // API 31+: загружаем через Coil, blur через RenderEffect
                binding.chatBackgroundImage.load(url, AvatarLoader.getImageLoader(this@ChatActivity)) {
                    crossfade(true)
                    listener(onSuccess = { _, _ ->
                        if (loadVersion == chatBackgroundLoadVersion) {
                            applyRenderEffectBlur(applyBlur)
                        }
                    })
                }
            } else {
                // API 26–30: загружаем Bitmap, blur через ScriptIntrinsicBlur
                val bitmap = withContext(Dispatchers.IO) {
                    loadBitmapFromUrl(url)
                }
                if (bitmap != null && loadVersion == chatBackgroundLoadVersion) {
                    val finalBitmap = if (applyBlur) blurBitmapLegacy(bitmap) else bitmap
                    binding.chatBackgroundImage.setImageBitmap(finalBitmap)
                }
            }

            // Кешируем в дисковый кэш приложения
            withContext(Dispatchers.IO) {
                try {
                    val connection = java.net.URL(url)
                        .openConnection() as java.net.HttpURLConnection
                    grpcManager.configureHttpConnection(connection)
                    connection.connect()
                    val bytes = connection.inputStream.readBytes()
                    connection.disconnect()
                    FileCache.saveFile(fileId, bytes)
                } catch (_: Exception) { }
            }
        }
    }

    private fun applyBackgroundFromFile(file: java.io.File, applyBlur: Boolean, loadVersion: Int) {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
            binding.chatBackgroundImage.load(file, AvatarLoader.getImageLoader(this)) {
                crossfade(false)
                listener(onSuccess = { _, _ ->
                    if (loadVersion == chatBackgroundLoadVersion) {
                        applyRenderEffectBlur(applyBlur)
                    }
                })
            }
        } else {
            lifecycleScope.launch {
                val bitmap = withContext(Dispatchers.IO) {
                    android.graphics.BitmapFactory.decodeFile(file.absolutePath)
                } ?: return@launch
                if (loadVersion != chatBackgroundLoadVersion) return@launch
                val finalBitmap = if (applyBlur) blurBitmapLegacy(bitmap) else bitmap
                binding.chatBackgroundImage.setImageBitmap(finalBitmap)
            }
        }
    }

    @androidx.annotation.RequiresApi(android.os.Build.VERSION_CODES.S)
    private fun applyRenderEffectBlur(applyBlur: Boolean) {
        binding.chatBackgroundImage.setRenderEffect(
            if (applyBlur) android.graphics.RenderEffect.createBlurEffect(
                20f, 20f, android.graphics.Shader.TileMode.CLAMP
            ) else null
        )
    }

    /**
     * Блюр-копия обоев для подложки шапки (chatHeaderBlurImage), обрезаемая
     * clipBounds'ом до высоты шапки. Блюрится всегда — независимо от настройки
     * chatBackgroundBlur основного фона: на API 31+ через RenderEffect,
     * на старых устройствах через ScriptIntrinsicBlur по bitmap.
     */
    private fun applyHeaderBlur(source: Any, loadVersion: Int) {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
            binding.chatHeaderBlurImage.load(source, AvatarLoader.getImageLoader(this)) {
                listener(onSuccess = { _, _ ->
                    if (loadVersion == chatBackgroundLoadVersion) {
                        binding.chatHeaderBlurImage.setRenderEffect(
                            android.graphics.RenderEffect.createBlurEffect(
                                20f, 20f, android.graphics.Shader.TileMode.CLAMP
                            )
                        )
                    }
                })
            }
        } else {
            lifecycleScope.launch {
                val bitmap = withContext(Dispatchers.IO) {
                    when (source) {
                        is java.io.File -> android.graphics.BitmapFactory.decodeFile(source.absolutePath)
                        is String -> loadBitmapFromUrl(source)
                        else -> null
                    }
                }
                if (bitmap != null && loadVersion == chatBackgroundLoadVersion) {
                    binding.chatHeaderBlurImage.setImageBitmap(blurBitmapLegacy(bitmap))
                }
            }
        }
    }

    private fun loadBitmapFromUrl(url: String): android.graphics.Bitmap? {
        return try {
            val conn = java.net.URL(url).openConnection() as java.net.HttpURLConnection
            grpcManager.configureHttpConnection(conn)
            conn.connect()
            val bmp = android.graphics.BitmapFactory.decodeStream(conn.inputStream)
            conn.disconnect()
            bmp
        } catch (_: Exception) { null }
    }

    @Suppress("DEPRECATION")
    private fun blurBitmapLegacy(src: android.graphics.Bitmap): android.graphics.Bitmap {
        return try {
            val rs = android.renderscript.RenderScript.create(this)
            val input = android.renderscript.Allocation.createFromBitmap(rs, src)
            val output = android.renderscript.Allocation.createTyped(rs, input.type)
            val script = android.renderscript.ScriptIntrinsicBlur.create(rs, input.element)
            script.setRadius(20f)
            script.setInput(input)
            script.forEach(output)
            val blurred = src.copy(src.config ?: android.graphics.Bitmap.Config.ARGB_8888, true)
            output.copyTo(blurred)
            rs.destroy()
            blurred
        } catch (_: Exception) { src }
    }

    private fun setupMessagesRecyclerView() {
        val scope = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.Main)
        messageAdapter = MessageAdapter(
            currentUserId = currentUserId,
            isGroupChat = isGroupChat,
            getFileUrl = { fileId ->
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            },
            downloadToCache = { fileId, onProgress ->
                chatRepository.downloadFile(fileId, onProgress)
            },
            scope = scope,
            messageCornerRadiusDp = globalParam.chatMessageCornerRadius,
            stickerSizeDp = globalParam.chatStickerSizeDp,
            onMessageActionRequested = { bubble, item ->
                showMessageActionMenu(bubble, item)
            },
            onReplyQuoteClick = { originalMessageId ->
                scrollToAndHighlightMessage(originalMessageId)
            },
            senderInfoProvider = { senderId -> groupMemberInfoCache[senderId] },
            onSelectionToggle = { messageId -> toggleSelection(messageId) }
        )

        if (isGroupChat) {
            loadGroupMemberInfo()
        }

        binding.messagesRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@ChatActivity).apply {
                stackFromEnd = true // Прокрутка к концу списка
            }
            adapter = messageAdapter
            itemAnimator = MessageItemAnimator()
            addItemDecoration(
                MessageTimeSpacingDecoration(
                    smallSpacingPx = (2 * resources.displayMetrics.density).toInt(),  // 2dp
                    largeSpacingPx = (10 * resources.displayMetrics.density).toInt()  // 10dp
                )
            )

            // Свайп влево по сообщению — триггер reply
            val swipeCallback = com.barkfluff.client.adapter.ReplySwipeCallback(this@ChatActivity) { position ->
                val item = messageAdapter.getMessageAt(position) ?: return@ReplySwipeCallback
                setPendingReply(item)
            }
            addOnItemTouchListener(com.barkfluff.client.adapter.ReplySwipeTableTouchGate(swipeCallback))
            androidx.recyclerview.widget.ItemTouchHelper(swipeCallback).attachToRecyclerView(this)

            // Обработчик скролла для пагинации и кнопки "вниз"
            addOnScrollListener(object : RecyclerView.OnScrollListener() {
                override fun onScrolled(recyclerView: RecyclerView, dx: Int, dy: Int) {
                    super.onScrolled(recyclerView, dx, dy)

                    val layoutManager = layoutManager as LinearLayoutManager
                    val firstVisibleItem = layoutManager.findFirstVisibleItemPosition()
                    val lastVisibleItem = layoutManager.findLastVisibleItemPosition()
                    val totalItemCount = layoutManager.itemCount

                    // Подгрузка вверх (история)
                    if (firstVisibleItem < 10) {
                        viewModel.loadMessagesUp()
                    }

                    // Подгрузка вниз (новые сообщения)
                    if (lastVisibleItem >= totalItemCount - 10) {
                        viewModel.loadMessagesDown()
                    }

                    // Показ/скрытие кнопки прокрутки вниз
                    updateScrollToBottomButton()

                    // Шапка сжимается при уходе в историю сообщений
                    updateHeaderCompact(dy)

                    // Safety-net: долистали до самого низа и подгружать больше нечего —
                    // помечаем прочитанными все загруженные чужие сообщения (страхует от
                    // случаев, когда прогрессивная пометка при пагинации что-то не зацепила).
                    viewModel.onReachedBottom()
                }
            })
        }

        // Обработчик клика на кнопку прокрутки вниз
        binding.scrollToBottomButton.setOnClickListener {
            scrollToLatestMessages()
        }
    }

    /**
     * Загружает имена и аватары участников группы в кэш, после чего обновляет список
     * сообщений, чтобы у чужих сообщений отрисовались мини-аватарки.
     */
    private fun loadGroupMemberInfo() {
        lifecycleScope.launch {
            val members = grpcManager.listChatMembers(chatId).getOrNull() ?: return@launch

            for (member in members) {
                if (member.userId == currentUserId) continue
                val name = "${member.firstName} ${member.lastName}".trim().ifBlank {
                    getString(R.string.group_member_id, member.userId)
                }
                val avatarSource = grpcManager.getUserData(member.userId).getOrNull()?.let { user ->
                    avatarSourceFor(user)
                }
                groupMemberInfoCache[member.userId] = name to avatarSource
            }

            // Изменились только имя и мини-аватар отправителя — полный ребинд не нужен.
            messageAdapter.notifyItemRangeChanged(
                0,
                messageAdapter.itemCount,
                MessageAdapter.PAYLOAD_SENDER_INFO
            )
        }
    }

    /**
     * Перерисовывает индикатор "печатает..." в onlineStatusTextView поверх онлайн-статуса.
     * Если никто не печатает — восстанавливает предыдущее содержимое (статус онлайна / скрытие для группы).
     */
    private fun renderTypingIndicator() {
        if (typingUsers.isEmpty()) {
            if (isGroupChat) {
                binding.onlineStatusTextView.visibility = View.GONE
            } else {
                binding.onlineStatusTextView.text = lastStatusText ?: ""
            }
            return
        }

        if (!isGroupChat) {
            binding.onlineStatusTextView.text = getString(R.string.typing_indicator)
            return
        }

        binding.onlineStatusTextView.visibility = View.VISIBLE
        val names = mutableListOf<String>()
        for (userId in typingUsers.keys) {
            val fullName = groupMemberInfoCache[userId]?.first
            if (fullName != null) {
                names.add(fullName.substringBefore(' '))
            } else {
                loadMissingTypingMemberName(userId)
            }
        }

        binding.onlineStatusTextView.text = if (names.isEmpty()) {
            getString(R.string.typing_indicator)
        } else {
            resources.getQuantityString(
                R.plurals.typing_indicator_named,
                typingUsers.size,
                names.take(3).joinToString(", ")
            )
        }
    }

    /**
     * Асинхронно подгружает имя/аватар участника группы (для индикатора набора текста),
     * тем же способом, что и loadGroupMemberInfo — через getUserData.
     */
    private fun loadMissingTypingMemberName(userId: Long) {
        if (!pendingTypingNameFetches.add(userId)) return
        lifecycleScope.launch {
            try {
                val user = grpcManager.getUserData(userId).getOrNull()
                if (user != null) {
                    val name = "${user.firstName} ${user.lastName}".trim().ifBlank {
                        getString(R.string.group_member_id, userId)
                    }
                    groupMemberInfoCache[userId] = name to avatarSourceFor(user)
                }
            } finally {
                pendingTypingNameFetches.remove(userId)
            }
            renderTypingIndicator()
        }
    }

    private fun avatarSourceFor(user: GrpcManager.UserData): String? {
        return user.profilePicturePreviewUrl
            .ifBlank { user.profilePictureUrl }
            .ifBlank { user.profilePicturePreviewFileId }
            .ifBlank { user.profilePictureFileId }
            .ifBlank { null }
    }

    /**
     * Запускает быстрый плавный скролл вниз для визуальной динамики, параллельно делает запрос
     * на сервер. Через 300 мс — рывок к самому низу (с подгрузкой новых сообщений если нужно).
     * Если расстояние до конца > 500px: рывок на 120dp выше финала → плавное торможение по кривой.
     */
    private fun scrollToLatestMessages() {
        if (viewModel.uiState.value.isLoading) return
        lifecycleScope.launch {
            val lm = binding.messagesRecyclerView.layoutManager as? LinearLayoutManager

            // Сразу запускаем быстрый плавный скролл (скорость в 3x быстрее стандартной)
            if (lm != null) {
                val fastScroller = object : LinearSmoothScroller(this@ChatActivity) {
                    override fun calculateSpeedPerPixel(displayMetrics: android.util.DisplayMetrics): Float {
                        return 25f / 3f / displayMetrics.densityDpi
                    }
                }
                fastScroller.targetPosition = messageAdapter.itemCount - 1
                lm.startSmoothScroll(fastScroller)
            }

            // Ждём 300 мс — за это время список успевает «полететь» вниз,
            // параллельно VM подтягивает актуальное состояние с сервера.
            delay(300)

            try {
                viewModel.refreshLatestMessages()

                // Рывок в конец с эффектом плавного торможения если расстояние большое
                snapToBottom()
            } catch (e: Exception) {
                Log.e(TAG, "Error scrolling to latest messages", e)
                binding.messagesRecyclerView.scrollToPosition(messageAdapter.itemCount - 1)
            }
        }
    }

    /**
     * Рывок в самый низ списка.
     * Если расстояние до конца > 500px: моментально перепрыгивает на 120dp выше финала,
     * затем доезжает последние 120dp с плавным торможением по кривой DecelerateInterpolator.
     * Иначе — просто мгновенный scrollToPosition.
     */
    private fun snapToBottom() {
        val rv = binding.messagesRecyclerView
        val lm = rv.layoutManager as? LinearLayoutManager ?: run {
            rv.scrollToPosition(messageAdapter.itemCount - 1)
            return
        }

        // Считаем текущее расстояние до конца контента через публичные методы RecyclerView
        val totalHeight = rv.computeVerticalScrollRange()
        val currentOffset = rv.computeVerticalScrollOffset()
        val visibleHeight = rv.height
        val distanceToEnd = totalHeight - currentOffset - visibleHeight

        val density = resources.displayMetrics.density
        val overshootPx = (120 * density).toInt() // 120dp → px

        if (distanceToEnd > 500) {
            // Мгновенно прыгаем на 120dp выше финала, потом плавно тормозим до конца
            val jumpTarget = (totalHeight - visibleHeight - overshootPx).coerceAtLeast(currentOffset)
            rv.stopScroll()
            rv.scrollBy(0, jumpTarget - currentOffset)

            // Вычисляем скорость в момент рывка (та же что и у fastScroller: 25/3 ms/px)
            // Длина оставшегося пути = overshootPx, начальная скорость из этой же формулы
            val durationMs = (overshootPx * (25f / 3f)).toLong().coerceAtLeast(80L)

            ValueAnimator.ofInt(0, overshootPx).apply {
                duration = durationMs
                interpolator = DecelerateInterpolator(2f)
                var lastValue = 0
                addUpdateListener { anim ->
                    val value = anim.animatedValue as Int
                    val delta = value - lastValue
                    lastValue = value
                    rv.scrollBy(0, delta)
                }
                start()
            }
        } else {
            // Расстояние маленькое — просто мгновенный переход
            rv.stopScroll()
            rv.scrollToPosition(messageAdapter.itemCount - 1)
        }
    }

    private fun updateScrollToBottomButton() {
        val adapter = binding.messagesRecyclerView.adapter ?: return

        if (adapter.itemCount == 0) {
            binding.scrollToBottomButton.visibility = View.GONE
            return
        }

        // Показываем кнопку если можно прокрутить вниз (не в самом низу)
        val canScrollDown = binding.messagesRecyclerView.canScrollVertically(1)
        binding.scrollToBottomButton.visibility = if (canScrollDown) View.VISIBLE else View.GONE
    }

    private fun setupMessageInput() {
        binding.sendButton.background = sendButtonBackground
        applySendButtonShape(canSend = false, animate = false)
        binding.sendButton.applySpringPress()
        binding.sendButton.setOnClickListener {
            when {
                pendingDocumentUris.isNotEmpty() || pendingPastedImages.isNotEmpty() || pendingStickerUris.isNotEmpty() ->
                    sendMessageWithPendingAttachments()
                else ->
                    sendMessage()
            }
        }
        binding.sendButton.setOnTouchListener { _, event ->
            handleVoiceButtonTouch(event)
        }

        binding.messageEditText.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                updateSendButtonMode()
                onTypingInput(s)
                scheduleDraftSave()
            }
            override fun afterTextChanged(s: Editable?) = Unit
        })

        binding.messageEditText.setOnEditorActionListener { _, _, _ ->
            when {
                pendingDocumentUris.isNotEmpty() || pendingPastedImages.isNotEmpty() || pendingStickerUris.isNotEmpty() ->
                    sendMessageWithPendingAttachments()
                else ->
                    sendMessage()
            }
            true
        }

        binding.attachButton.setOnClickListener {
            pickImages()
        }

        binding.stickerButton.setOnClickListener {
            showStickerPicker()
        }

        // При клике на поле ввода — закрыть стикер-панель и показать клавиатуру
        binding.messageEditText.setOnClickListener {
            if (inputPanelState == InputPanelState.STICKER_PANEL) {
                hideStickerPanel()
                WindowInsetsControllerCompat(window, binding.chatRootLayout).show(WindowInsetsCompat.Type.ime())
            }
        }

        binding.clearAttachmentsButton.setOnClickListener {
            pendingPastedImages.clear()
            pendingStickerUris.clear()
            pendingDocumentUris.clear()
            updateAttachmentPreview()
        }

        binding.clearReplyButton.setOnClickListener {
            clearPendingReply()
        }

        binding.clearEditButton.setOnClickListener {
            clearPendingEdit()
        }

        updateSendButtonMode()

        // Обработка вставки изображений из буфера обмена
        ViewCompat.setOnReceiveContentListener(
            binding.messageEditText,
            arrayOf("image/*")
        ) { _, payload ->
            val split = payload.partition { it.uri != null }
            val uriContent = split.first
            if (uriContent != null) {
                val clip = uriContent.clip
                val desc = clip.description
                Log.d(TAG, "onReceiveContent: itemCount=${clip.itemCount}, mimeTypeCount=${desc?.mimeTypeCount}")
                if (desc != null) {
                    for (m in 0 until desc.mimeTypeCount) {
                        Log.d(TAG, "onReceiveContent: clipMime[$m]=${desc.getMimeType(m)}")
                    }
                }
                for (i in 0 until clip.itemCount) {
                    clip.getItemAt(i).uri?.let { uri ->
                        val resolverMime = contentResolver.getType(uri)
                        Log.d(TAG, "onReceiveContent: uriScheme=${uri.scheme}, resolverMime=$resolverMime")
                        if (isStickerContent(uri, desc, i)) {
                            Log.d(TAG, "onReceiveContent: detected sticker → skip cropper")
                            pendingStickerUris.add(uri)
                            updateAttachmentPreview()
                        } else {
                            Log.d(TAG, "onReceiveContent: not WebP → cropper")
                            pendingCropQueue.addLast(uri)
                        }
                    }
                }
                processNextCropFromQueue()
            }
            split.second
        }
    }

    private fun hasPendingAttachments(): Boolean =
        pendingDocumentUris.isNotEmpty() || pendingPastedImages.isNotEmpty() || pendingStickerUris.isNotEmpty()

    private fun shouldShowVoiceButton(): Boolean {
        val text = binding.messageEditText.text?.toString().orEmpty()
        val state = viewModel.uiState.value
        return text.isBlank() &&
            !hasPendingAttachments() &&
            state.pendingReply == null &&
            state.pendingEdit == null
    }

    private fun updateSendButtonMode() {
        if (voiceRecorder != null) return

        sendButtonVoiceMode = shouldShowVoiceButton()
        binding.sendButton.setImageResource(
            if (sendButtonVoiceMode) R.drawable.ic_mic else R.drawable.ic_send_filled
        )
        binding.sendButton.contentDescription = getString(
            if (sendButtonVoiceMode) R.string.cd_record_voice else R.string.cd_send
        )
        applySendButtonShape(canSend = !sendButtonVoiceMode)
        tintSendButton(
            if (sendButtonVoiceMode) {
                com.google.android.material.R.attr.colorOnPrimaryContainer
            } else {
                com.google.android.material.R.attr.colorOnPrimary
            }
        )
    }

    /**
     * Морфинг кнопки отправки: пустой ввод — круг 52dp в тоне primaryContainer,
     * есть что отправить — компактная pill 68dp в primary.
     */
    private fun applySendButtonShape(canSend: Boolean, animate: Boolean = true) {
        // Форма меняется только на переходе — иначе анимация перезапускалась бы на каждый символ
        if (sendButtonWide == canSend) return
        sendButtonWide = canSend
        val density = resources.displayMetrics.density
        val targetWidth = ((if (canSend) SEND_BUTTON_WIDE_DP else SEND_BUTTON_NARROW_DP) * density).toInt()
        val targetRadius = (if (canSend) SEND_BUTTON_WIDE_CORNER_DP else SEND_BUTTON_NARROW_CORNER_DP) * density
        val targetColor = MaterialColors.getColor(
            binding.sendButton,
            if (canSend) {
                androidx.appcompat.R.attr.colorPrimary
            } else {
                com.google.android.material.R.attr.colorPrimaryContainer
            }
        )

        sendButtonBackground.setColor(targetColor)
        if (!animate) {
            sendButtonBackground.cornerRadius = targetRadius
            binding.sendButton.updateLayoutParams { width = targetWidth }
            return
        }

        sendButtonMorphAnimator?.cancel()
        val startWidth = binding.sendButton.width.takeIf { it > 0 } ?: targetWidth
        val startRadius = sendButtonBackground.cornerRadius
        sendButtonMorphAnimator = ValueAnimator.ofFloat(0f, 1f).apply {
            duration = SEND_BUTTON_MORPH_DURATION_MS
            interpolator = PathInterpolator(0.2f, 0f, 0f, 1f)
            addUpdateListener { animator ->
                val fraction = animator.animatedFraction
                sendButtonBackground.cornerRadius = startRadius + (targetRadius - startRadius) * fraction
                binding.sendButton.updateLayoutParams {
                    width = (startWidth + (targetWidth - startWidth) * fraction).toInt()
                }
            }
            start()
        }
    }

    private fun tintSendButton(attr: Int) {
        val color = MaterialColors.getColor(binding.sendButton, attr)
        binding.sendButton.imageTintList = ColorStateList.valueOf(color)
    }

    /**
     * Реагирует на ввод текста — запускает/останавливает heartbeat отправки статуса набора текста.
     */
    private fun onTypingInput(s: CharSequence?) {
        if (suppressTypingInput) return
        if (s.isNullOrBlank()) {
            stopTypingHeartbeat(sendCancel = true)
            return
        }
        lastTypingInputAt = System.currentTimeMillis()
        if (typingHeartbeatJob == null) {
            typingHeartbeatJob = lifecycleScope.launch {
                while (isActive) {
                    realtimeService.sendTypingStatus(chatId, typing = true)
                    delay(4_000)
                    if (System.currentTimeMillis() - lastTypingInputAt >= 5_000) break
                }
                typingHeartbeatJob = null
            }
        }
    }

    private fun stopTypingHeartbeat(sendCancel: Boolean) {
        val job = typingHeartbeatJob
        if (job != null) {
            job.cancel()
            typingHeartbeatJob = null
            if (sendCancel) {
                realtimeService.sendTypingStatus(chatId, typing = false)
            }
        }
    }

    private fun handleVoiceButtonTouch(event: MotionEvent): Boolean {
        if (!sendButtonVoiceMode && voiceRecorder == null) return false

        return when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                if (!shouldShowVoiceButton()) {
                    updateSendButtonMode()
                    false
                } else {
                    if (!hasRecordAudioPermission()) {
                        recordAudioPermissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
                        return true
                    }

                    voiceDownRawX = event.rawX
                    voiceCancelPending = false
                    if (startVoiceRecording()) {
                        binding.sendButton.animate()
                            .scaleX(0.95f)
                            .scaleY(0.95f)
                            .setDuration(80L)
                            .start()
                    }
                    true
                }
            }
            MotionEvent.ACTION_MOVE -> {
                if (voiceRecorder == null) return true

                val cancelDistancePx = resources.displayMetrics.widthPixels * 0.5f
                val dx = (event.rawX - voiceDownRawX).coerceAtMost(0f).coerceAtLeast(-cancelDistancePx)
                binding.sendButton.translationX = dx
                binding.voiceRecordHint.translationX = dx * VOICE_HINT_DRAG_RATIO

                val cancelNow = -dx >= cancelDistancePx
                if (cancelNow != voiceCancelPending) {
                    voiceCancelPending = cancelNow
                    updateVoiceRecordHint(cancelNow)
                    tintSendButton(
                        if (cancelNow) androidx.appcompat.R.attr.colorError
                        else com.google.android.material.R.attr.colorOnPrimaryContainer
                    )
                }
                true
            }
            MotionEvent.ACTION_UP -> {
                finishVoiceRecording(shouldSend = !voiceCancelPending)
                true
            }
            MotionEvent.ACTION_CANCEL -> {
                finishVoiceRecording(shouldSend = false)
                true
            }
            else -> true
        }
    }

    private fun hasRecordAudioPermission(): Boolean =
        ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

    private fun startVoiceRecording(): Boolean {
        if (voiceRecorder != null) return true

        val file = File.createTempFile("voice_${System.currentTimeMillis()}_", ".ogg", cacheDir)
        return try {
            val recorder = MediaRecorder(this).apply {
                setAudioSource(MediaRecorder.AudioSource.MIC)
                setOutputFormat(MediaRecorder.OutputFormat.OGG)
                setAudioEncoder(MediaRecorder.AudioEncoder.OPUS)
                setAudioChannels(1)
                setAudioSamplingRate(48_000)
                setAudioEncodingBitRate(24_000)
                setOutputFile(file.absolutePath)
                prepare()
                start()
            }

            voiceRecorder = recorder
            voiceRecordingFile = file
            voiceRecordingStartedAtMs = System.currentTimeMillis()
            tintSendButton(com.google.android.material.R.attr.colorOnPrimaryContainer)
            showVoiceRecordingBar()
            true
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start voice recording", e)
            runCatching { voiceRecorder?.release() }
            voiceRecorder = null
            voiceRecordingFile = null
            runCatching { file.delete() }
            resetVoiceButtonDrag()
            Toast.makeText(this, R.string.voice_record_start_failed, Toast.LENGTH_SHORT).show()
            false
        }
    }

    private fun finishVoiceRecording(shouldSend: Boolean) {
        val recorder = voiceRecorder ?: return
        val file = voiceRecordingFile
        val elapsedMs = System.currentTimeMillis() - voiceRecordingStartedAtMs
        val wasCancelledByDrag = !shouldSend && voiceCancelPending
        var send = shouldSend

        try {
            recorder.stop()
        } catch (e: Exception) {
            Log.w(TAG, "Failed to stop voice recording cleanly", e)
            send = false
        } finally {
            runCatching { recorder.release() }
            voiceRecorder = null
            voiceRecordingFile = null
            voiceRecordingStartedAtMs = 0L
            hideVoiceRecordingBar()
            resetVoiceButtonDrag()
        }

        if (!send) {
            runCatching { file?.delete() }
            if (wasCancelledByDrag) {
                Toast.makeText(this, R.string.voice_record_cancelled, Toast.LENGTH_SHORT).show()
            }
            return
        }

        if (file == null || elapsedMs < MIN_VOICE_RECORDING_MS || !file.exists() || file.length() == 0L) {
            runCatching { file?.delete() }
            Toast.makeText(this, R.string.voice_record_too_short, Toast.LENGTH_SHORT).show()
            return
        }

        sendVoiceMessage(file)
    }

    /**
     * На время записи прячет грядку ввода и показывает поверх неё индикатор:
     * мигающая точка, счётчик длительности и подсказка отмены.
     */
    private fun showVoiceRecordingBar() {
        updateVoiceRecordTimer()
        updateVoiceRecordHint(cancelPending = false)
        binding.voiceRecordHint.translationX = 0f
        binding.voiceRecordDot.alpha = 1f

        binding.voiceRecordBar.alpha = 0f
        binding.voiceRecordBar.visibility = View.VISIBLE
        binding.voiceRecordBar.animate()
            .alpha(1f)
            .setDuration(VOICE_BAR_FADE_DURATION_MS)
            .start()
        binding.inputBar.animate()
            .alpha(0f)
            .setDuration(VOICE_BAR_FADE_DURATION_MS)
            .withEndAction { binding.inputBar.visibility = View.INVISIBLE }
            .start()

        voiceDotAnimator?.cancel()
        voiceDotAnimator = ValueAnimator.ofFloat(1f, 0.25f).apply {
            duration = VOICE_DOT_BLINK_DURATION_MS
            repeatMode = ValueAnimator.REVERSE
            repeatCount = ValueAnimator.INFINITE
            addUpdateListener { binding.voiceRecordDot.alpha = it.animatedValue as Float }
            start()
        }

        voiceTimerJob?.cancel()
        voiceTimerJob = lifecycleScope.launch {
            while (isActive) {
                updateVoiceRecordTimer()
                delay(VOICE_TIMER_TICK_MS)
            }
        }
    }

    private fun hideVoiceRecordingBar() {
        voiceTimerJob?.cancel()
        voiceTimerJob = null
        voiceDotAnimator?.cancel()
        voiceDotAnimator = null

        if (binding.voiceRecordBar.visibility != View.VISIBLE) return

        binding.voiceRecordBar.animate()
            .alpha(0f)
            .setDuration(VOICE_BAR_FADE_DURATION_MS)
            .withEndAction {
                binding.voiceRecordBar.visibility = View.GONE
                binding.voiceRecordHint.translationX = 0f
            }
            .start()
        binding.inputBar.visibility = View.VISIBLE
        binding.inputBar.animate()
            .alpha(1f)
            .setDuration(VOICE_BAR_FADE_DURATION_MS)
            .start()
    }

    private fun updateVoiceRecordTimer() {
        val elapsedSec = ((System.currentTimeMillis() - voiceRecordingStartedAtMs) / 1000L)
            .coerceAtLeast(0L)
        binding.voiceRecordTimer.text = getString(
            R.string.voice_record_timer_format,
            elapsedSec / 60,
            elapsedSec % 60
        )
    }

    private fun updateVoiceRecordHint(cancelPending: Boolean) {
        binding.voiceRecordHint.setText(
            if (cancelPending) R.string.voice_record_release_to_cancel
            else R.string.voice_record_slide_to_cancel
        )
        binding.voiceRecordHint.setTextColor(
            MaterialColors.getColor(
                binding.voiceRecordHint,
                if (cancelPending) androidx.appcompat.R.attr.colorError
                else com.google.android.material.R.attr.colorOnSurfaceVariant
            )
        )
    }

    private fun resetVoiceButtonDrag() {
        binding.sendButton.animate()
            .translationX(0f)
            .scaleX(1f)
            .scaleY(1f)
            .setDuration(120L)
            .start()
        voiceCancelPending = false
        updateSendButtonMode()
    }

    private fun sendVoiceMessage(file: File) {
        val localId = java.util.UUID.randomUUID().toString()
        viewModel.addOptimisticMessage(
            MessageItem(
                messageId = -System.nanoTime(),
                senderId = currentUserId,
                text = "",
                timestamp = System.currentTimeMillis(),
                attachments = emptyList(),
                readStatus = ReadStatus.SENDING,
                type = MessageType.MESSAGE,
                localId = localId,
                uploadProgress = 0
            )
        )

        val job = com.barkfluff.client.send.SendJob(
            chatId = chatId,
            chatTitle = chatTitle,
            text = "",
            attachments = listOf(com.barkfluff.client.send.AttachmentSpec.Voice(file)),
            localIds = listOf(localId)
        )
        com.barkfluff.client.send.MediaSendService.enqueue(applicationContext, job)
    }

    private fun pickImages() {
        val imagePicker = ImagePickerBottomSheet.newInstance { result ->
            handleSelectedImages(result)
        }
        imagePicker.show(supportFragmentManager, "ImagePickerBottomSheet")
    }

    private fun setupKeyboardTracking() {
        lastKnownKeyboardHeight = KeyboardHeightTracker.getLastKnownHeight(this)

        KeyboardHeightTracker(binding.chatRootLayout, this) { height, isVisible ->
            if (isVisible) {
                lastKnownKeyboardHeight = height
                if (isTransitioningToStickers) {
                    // Клавиатура ещё не закрылась, ждём
                    return@KeyboardHeightTracker
                }
                if (inputPanelState == InputPanelState.STICKER_PANEL) {
                    hideStickerPanel()
                }
                inputPanelState = InputPanelState.KEYBOARD
            } else {
                if (isTransitioningToStickers) {
                    isTransitioningToStickers = false
                    showStickerPanelView()
                } else if (inputPanelState == InputPanelState.KEYBOARD) {
                    inputPanelState = InputPanelState.NONE
                    backCallback.isEnabled = false
                }
            }
        }
    }

    private fun setupStickerPanel() {
        stickerPanelAdapter = StickerPanelAdapter(
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onStickerClick = { sticker ->
                sendStickerMessage(sticker)
            },
            onStickerLongPress = { sticker ->
                showStickerPreview(sticker)
            }
        )

        binding.stickerPreviewOverlay.setOnClickListener {
            hideStickerPreview()
        }

        val gridLayoutManager = GridLayoutManager(this, 4)
        gridLayoutManager.spanSizeLookup = object : GridLayoutManager.SpanSizeLookup() {
            override fun getSpanSize(position: Int): Int {
                return when (stickerPanelAdapter.getItemViewType(position)) {
                    StickerPanelAdapter.VIEW_TYPE_STICKER -> 1
                    else -> 4 // PackHeader, Loading, Empty — full width
                }
            }
        }

        binding.stickerPanelRecyclerView.apply {
            layoutManager = gridLayoutManager
            adapter = stickerPanelAdapter
            addItemDecoration(object : RecyclerView.ItemDecoration() {
                override fun getItemOffsets(outRect: android.graphics.Rect, view: View, parent: RecyclerView, state: RecyclerView.State) {
                    val position = parent.getChildAdapterPosition(view)
                    if (position <= 0) return
                    val item = stickerPanelAdapter.currentList.getOrNull(position) ?: return
                    if (item is StickerPanelItem.PackHeader) {
                        outRect.top = (15 * resources.displayMetrics.density).toInt()
                    }
                }
            })
        }

        // Back press закрывает стикер-панель или оверлей предпросмотра.
        // Callback включён только когда есть что закрывать — иначе система
        // обрабатывает жест сама и показывает predictive back анимацию.
        backCallback = object : OnBackPressedCallback(false) {
            override fun handleOnBackPressed() {
                when {
                    messageAdapter.selectionMode -> exitSelectionMode()
                    messageActionsOverlay.isShowing -> messageActionsOverlay.dismiss()
                    binding.stickerPreviewOverlay.visibility == View.VISIBLE -> hideStickerPreview()
                    inputPanelState == InputPanelState.STICKER_PANEL -> hideStickerPanel()
                }
            }
        }
        onBackPressedDispatcher.addCallback(this, backCallback)
    }

    private fun showStickerPicker() {
        if (inputPanelState == InputPanelState.STICKER_PANEL) {
            hideStickerPanel()
            return
        }

        if (inputPanelState == InputPanelState.KEYBOARD) {
            isTransitioningToStickers = true
            WindowInsetsControllerCompat(window, binding.chatRootLayout).hide(WindowInsetsCompat.Type.ime())
        } else {
            showStickerPanelView()
        }
    }

    private fun showStickerPanelView() {
        val panelHeight = if (lastKnownKeyboardHeight > 0) lastKnownKeyboardHeight
            else (resources.displayMetrics.heightPixels * 0.4).toInt()

        binding.stickerPanelContainer.layoutParams.height = panelHeight
        binding.stickerPanelContainer.visibility = View.VISIBLE
        binding.stickerPanelContainer.requestLayout()
        inputPanelState = InputPanelState.STICKER_PANEL
        backCallback.isEnabled = true

        if (!stickerDataLoaded) {
            loadStickerPanelData()
        }
    }

    private fun hideStickerPanel() {
        binding.stickerPanelContainer.visibility = View.GONE
        inputPanelState = InputPanelState.NONE
        backCallback.isEnabled = binding.stickerPreviewOverlay.visibility == View.VISIBLE
    }

    private fun showStickerPreview(sticker: barkfluff.files.FilesApiOuterClass.StickerInfo) {
        val fileId = sticker.fileId
        if (fileId.isBlank()) return
        binding.stickerPreviewOverlay.visibility = View.VISIBLE
        backCallback.isEnabled = true
        val imageView = binding.stickerPreviewImage
        imageView.setImageDrawable(null)
        lifecycleScope.launch {
            val url = try {
                withContext(Dispatchers.IO) {
                    chatRepository.getFileDownloadUrl(fileId).getOrNull()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading sticker preview url", e)
                null
            }
            if (url.isNullOrBlank()) return@launch
            val imageLoader = AvatarLoader.getImageLoader(this@ChatActivity)
            imageView.load(url, imageLoader) {
                memoryCacheKey(fileId)
                diskCacheKey(fileId)
                crossfade(200)
            }
        }
    }

    private fun hideStickerPreview() {
        binding.stickerPreviewOverlay.visibility = View.GONE
        binding.stickerPreviewImage.setImageDrawable(null)
        backCallback.isEnabled = inputPanelState == InputPanelState.STICKER_PANEL
    }

    private fun loadStickerPanelData() {
        val cached = StickerCache.loadPanelData()
        if (cached != null) {
            stickerPanelAdapter.submitList(cached)
            stickerDataLoaded = true
            refreshStickerDataFromServer()
            return
        }
        stickerPanelAdapter.submitList(listOf(StickerPanelItem.Loading))
        refreshStickerDataFromServer()
    }

    private fun refreshStickerDataFromServer() {
        lifecycleScope.launch {
            try {
                val packs = withContext(Dispatchers.IO) { grpcManager.listStickerPacks() }
                if (packs.isNullOrEmpty()) {
                    if (!stickerDataLoaded) stickerPanelAdapter.submitList(listOf(StickerPanelItem.Empty))
                    return@launch
                }

                val allItems = mutableListOf<StickerPanelItem>()
                for (pack in packs) {
                    allItems.add(StickerPanelItem.PackHeader(
                        packId = pack.id,
                        packName = pack.name,
                        stickerCount = pack.stickerCount,
                        coverStickerId = pack.coverStickerId
                    ))
                    val stickers = withContext(Dispatchers.IO) { grpcManager.getStickerPack(pack.id) }
                    if (stickers != null) {
                        allItems.addAll(stickers.map { StickerPanelItem.Sticker(it, pack.id) })
                    }
                }
                stickerPanelAdapter.submitList(allItems)
                stickerDataLoaded = true
                StickerCache.savePanelData(allItems)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading sticker panel data", e)
                if (!stickerDataLoaded) stickerPanelAdapter.submitList(listOf(StickerPanelItem.Empty))
            }
        }
    }

    private fun sendStickerMessage(sticker: barkfluff.files.FilesApiOuterClass.StickerInfo) {
        lifecycleScope.launch {
            try {
                val fileId = sticker.fileId
                if (fileId.isBlank()) {
                    Toast.makeText(this@ChatActivity, R.string.sticker_file_missing, Toast.LENGTH_SHORT).show()
                    return@launch
                }
                sendMessage(text = "", fileIds = listOf(fileId))
            } catch (e: Exception) {
                Log.e(TAG, "Error sending sticker", e)
                Toast.makeText(this@ChatActivity, R.string.sticker_send_failed, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun handleSelectedImages(result: ImagePickerResult) {
        Log.d(TAG, "handleSelectedImages: uris.size=${result.uris.size}, sendAsFile=${result.sendAsFile}, sendSeparately=${result.sendSeparately}, captionLength=${result.captionText.length}, isDocuments=${result.isDocuments}, fromCamera=${result.fromCamera}")

        val uris = result.uris
        if (uris.isEmpty()) return

        // Если выбраны документы — добавить в pendingDocumentUris и показать превью
        if (result.isDocuments) {
            pendingDocumentUris.addAll(uris)
            updateAttachmentPreview()
            return
        }

        // Если фото с камеры — добавить в pendingPastedImages и показать превью
        if (result.fromCamera) {
            pendingPastedImages.addAll(uris)
            updateAttachmentPreview()
            return
        }

        // Обычный выбор фото/видео — формируем SendJob и кидаем в foreground-сервис
        val selectedUris = uris.take(ImagePickerBottomSheet.MAX_SELECTION)

        val attachments = mutableListOf<com.barkfluff.client.send.AttachmentSpec>()
        for (uri in selectedUris) {
            val mimeType = contentResolver.getType(uri)
            val isVideo = mimeType?.startsWith("video/") == true

            attachments += when {
                isVideo -> {
                    val spec = com.barkfluff.client.editor.VideoEditCache.get(uri)
                        ?: com.barkfluff.client.editor.EditedVideoSpec(uri = uri)
                    com.barkfluff.client.send.AttachmentSpec.Video(spec)
                }
                com.barkfluff.client.editor.MediaEditCache.has(uri) -> {
                    val edited = com.barkfluff.client.editor.MediaEditCache.get(uri)!!
                    val key = com.barkfluff.client.send.SendPayloadCache.put(edited.bytes)
                    com.barkfluff.client.send.AttachmentSpec.EditedImage(cacheKey = key, originalUri = uri)
                }
                else -> com.barkfluff.client.send.AttachmentSpec.RawImage(uri)
            }
        }

        // URI для локального превью оптимистичного сообщения (картинки/видео показываются сразу,
        // ещё до загрузки на сервер). Документы/стикеры превью не имеют.
        val previewUris: List<Uri?> = attachments.map { spec ->
            when (spec) {
                is com.barkfluff.client.send.AttachmentSpec.RawImage -> spec.uri
                is com.barkfluff.client.send.AttachmentSpec.EditedImage -> spec.originalUri
                is com.barkfluff.client.send.AttachmentSpec.Video -> spec.spec.uri
                else -> null
            }
        }

        // Генерим localId на каждое будущее сообщение и добавляем оптимистичные items в чат —
        // пользователь сразу видит карточки с прогрессом аплоада (M3 Expressive inline feedback).
        val localIds: List<String> = if (result.sendSeparately) {
            attachments.map { java.util.UUID.randomUUID().toString() }
        } else {
            listOf(java.util.UUID.randomUUID().toString())
        }

        if (result.sendSeparately) {
            attachments.forEachIndexed { idx, _ ->
                val captionForFirst = if (idx == 0) result.captionText else ""
                viewModel.addOptimisticMessage(
                    MessageItem(
                        messageId = -(System.nanoTime() + idx),
                        senderId = currentUserId,
                        text = captionForFirst,
                        timestamp = System.currentTimeMillis(),
                        attachments = emptyList(),
                        readStatus = ReadStatus.SENDING,
                        type = MessageType.MESSAGE,
                        localId = localIds[idx],
                        uploadProgress = 0,
                        localPreviewUris = listOfNotNull(previewUris.getOrNull(idx))
                    )
                )
            }
        } else {
            viewModel.addOptimisticMessage(
                MessageItem(
                    messageId = -System.nanoTime(),
                    senderId = currentUserId,
                    text = result.captionText,
                    timestamp = System.currentTimeMillis(),
                    attachments = emptyList(),
                    readStatus = ReadStatus.SENDING,
                    type = MessageType.MESSAGE,
                    localId = localIds[0],
                    uploadProgress = 0,
                    localPreviewUris = previewUris.filterNotNull()
                )
            )
        }

        val job = com.barkfluff.client.send.SendJob(
            chatId = chatId,
            chatTitle = chatTitle,
            text = result.captionText,
            attachments = attachments,
            replyId = viewModel.uiState.value.pendingReply?.messageId ?: 0L,
            sendSeparately = result.sendSeparately,
            sendAsFile = result.sendAsFile,
            localIds = localIds
        )
        com.barkfluff.client.send.MediaSendService.enqueue(applicationContext, job)
        clearPendingReply()
    }

    private fun isStickerContent(uri: Uri, clipDescription: android.content.ClipDescription?, index: Int): Boolean {
        // 1. MIME-тип image/webp — однозначно стикер
        val resolverMime = contentResolver.getType(uri)
        if (resolverMime == "image/webp") return true

        if (clipDescription != null && index < clipDescription.mimeTypeCount) {
            val clipMime = clipDescription.getMimeType(index)
            if (clipMime == "image/webp") return true
        }

        val path = uri.path ?: uri.toString()
        if (path.endsWith(".webp", ignoreCase = true)) return true

        // 2. Стикер от клавиатуры (Gboard и др.) — URI содержит /sticker/ в пути
        //    и приходит от inputmethod file provider
        val authority = uri.authority ?: ""
        if (authority.contains("inputmethod") && path.contains("/sticker/", ignoreCase = true)) return true

        return false
    }

    private suspend fun convertToWebp(uri: Uri): ByteArray? = withContext(Dispatchers.IO) {
        try {
            val mimeType = contentResolver.getType(uri)
            // Уже WebP — просто читаем байты
            if (mimeType == "image/webp") {
                return@withContext contentResolver.openInputStream(uri)?.use { it.readBytes() }
            }
            // Декодируем и конвертируем в WebP (с прозрачностью)
            val bitmap = contentResolver.openInputStream(uri)?.use {
                android.graphics.BitmapFactory.decodeStream(it)
            } ?: return@withContext null
            val outputStream = java.io.ByteArrayOutputStream()
            bitmap.compress(android.graphics.Bitmap.CompressFormat.WEBP_LOSSLESS, 100, outputStream)
            bitmap.recycle()
            outputStream.toByteArray()
        } catch (e: Exception) {
            Log.e(TAG, "Error converting to WebP", e)
            null
        }
    }

    private suspend fun readBytesFromUri(uri: Uri): ByteArray? = withContext(Dispatchers.IO) {
        try {
            contentResolver.openInputStream(uri)?.use { inputStream ->
                inputStream.readBytes()
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error reading bytes from uri", e)
            null
        }
    }

    /**
     * Возвращает (displayName, mimeType) для документа по URI.
     * displayName ищется через OpenableColumns.DISPLAY_NAME, mimeType — через ContentResolver.getType().
     * Если ничего не нашлось — возвращается (null, null).
     */
    private fun getDocumentInfo(uri: Uri): Pair<String?, String?> {
        var name: String? = null
        try {
            contentResolver.query(
                uri,
                arrayOf(android.provider.OpenableColumns.DISPLAY_NAME),
                null, null, null
            )?.use { cursor ->
                val nameIndex = cursor.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                if (nameIndex >= 0 && cursor.moveToFirst()) {
                    name = cursor.getString(nameIndex)
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error querying document info", e)
        }
        if (name.isNullOrBlank()) {
            // fallback на последний сегмент пути
            name = uri.lastPathSegment?.substringAfterLast('/')
        }
        val mime = contentResolver.getType(uri)
        return name to mime
    }

    private fun processNextCropFromQueue() {
        val next = pendingCropQueue.removeFirstOrNull() ?: return
        openPasteCropper(next)
    }

    private fun openPasteCropper(sourceUri: Uri) {
        val destinationUri = Uri.fromFile(
            File(cacheDir, "paste_crop_${System.currentTimeMillis()}.jpg")
        )
        val options = UCrop.Options().apply {
            setCompressionFormat(android.graphics.Bitmap.CompressFormat.JPEG)
            setCompressionQuality(95)
            setFreeStyleCropEnabled(true)
            setToolbarColor(getColor(android.R.color.white))
            setStatusBarColor(getColor(android.R.color.black))
            setActiveControlsWidgetColor(getColor(android.R.color.black))
        }
        pasteUCropLauncher.launch(
            UCrop.of(sourceUri, destinationUri).withOptions(options).getIntent(this)
        )
    }

    private fun updateAttachmentPreview() {
        val photosCount = pendingPastedImages.size + pendingStickerUris.size
        val filesCount = pendingDocumentUris.size

        if (photosCount == 0 && filesCount == 0) {
            binding.attachmentPreviewBar.visibility = View.GONE
        } else {
            binding.attachmentPreviewBar.visibility = View.VISIBLE
            binding.attachmentCountText.text = when {
                photosCount > 0 && filesCount > 0 ->
                    getString(R.string.attached_mixed_count, photosCount, filesCount)
                filesCount > 0 ->
                    getString(R.string.attached_files_count, filesCount)
                else ->
                    getString(R.string.attached_photos_count, photosCount)
            }
        }
        updateSendButtonMode()
    }

    private fun sendMessageWithPendingAttachments() {
        val text = binding.messageEditText.text.toString().trim()
        val photos = pendingPastedImages.toList()
        val stickers = pendingStickerUris.toList()
        val documents = pendingDocumentUris.toList()
        pendingPastedImages.clear()
        pendingStickerUris.clear()
        pendingDocumentUris.clear()
        updateAttachmentPreview()
        suppressDraftSave = true
        binding.messageEditText.text?.clear()
        suppressDraftSave = false

        lifecycleScope.launch {
            val fileIds = mutableListOf<String>()

            // Загружаем стикеры (конвертируем в WebP)
            for ((index, uri) in stickers.withIndex()) {
                try {
                    val bytes = convertToWebp(uri) ?: continue
                    val uploadResult = chatRepository.uploadFile(
                        bytes,
                        barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_STICKER
                    )
                    if (uploadResult.isSuccess) {
                        fileIds.add(uploadResult.getOrNull()!!)
                    } else {
                        Log.e(TAG, "Sticker ${index + 1}/${stickers.size} upload failed: ${uploadResult.exceptionOrNull()?.message}")
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error processing sticker ${index + 1}/${stickers.size}", e)
                }
            }

            // Загружаем фото (со сжатием)
            for ((index, uri) in photos.withIndex()) {
                try {
                    val bytes = ImageCompressor.compressImage(uri, this@ChatActivity).getOrNull()
                        ?: continue

                    val uploadType = barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE

                    val uploadResult = chatRepository.uploadFile(bytes, uploadType)
                    if (uploadResult.isSuccess) {
                        fileIds.add(uploadResult.getOrNull()!!)
                    } else {
                        Log.e(TAG, "Photo ${index + 1}/${photos.size} upload failed: ${uploadResult.exceptionOrNull()?.message}")
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error processing photo ${index + 1}/${photos.size}", e)
                }
            }

            // Загружаем документы (без сжатия)
            for ((index, uri) in documents.withIndex()) {
                try {
                    val bytes = readBytesFromUri(uri) ?: continue
                    val (docName, docMime) = getDocumentInfo(uri)
                    val uploadResult = chatRepository.uploadFile(
                        bytes,
                        barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT,
                        fileName = docName,
                        mimeType = docMime
                    )
                    if (uploadResult.isSuccess) {
                        fileIds.add(uploadResult.getOrNull()!!)
                    } else {
                        Log.e(TAG, "Document ${index + 1}/${documents.size} upload failed: ${uploadResult.exceptionOrNull()?.message}")
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error processing document ${index + 1}/${documents.size}", e)
                }
            }

            if (fileIds.isNotEmpty()) {
                sendMessage(text = text, fileIds = fileIds)
            } else {
                Toast.makeText(this@ChatActivity, R.string.files_upload_failed, Toast.LENGTH_SHORT).show()
                if (text.isNotBlank()) {
                    sendMessage(text = text)
                }
            }
        }
    }

    private fun sendMessage(text: String = binding.messageEditText.text.toString(), fileIds: List<String> = emptyList()) {
        val messageText = text.trim()
        val state = viewModel.uiState.value

        // Если активен режим редактирования — редактируем существующее сообщение.
        // fileIds игнорируем (вложения не меняются), VM использует сохранённые fileIds.
        if (state.pendingEdit != null) {
            viewModel.sendMessage(messageText)
            return
        }

        // Reply без текста и без файлов — отправляем сам факт пересылки
        if (messageText.isBlank() && fileIds.isEmpty() && state.pendingReply == null) return

        // Освобождаем поле ввода и reply-bar моментально — оптимистичный SENDING-item
        // добавит VM, пользователь не ждёт сетевого ответа.
        suppressDraftSave = true
        binding.messageEditText.text?.clear()
        suppressDraftSave = false
        clearPendingReply(saveDraft = false)

        viewModel.sendMessage(messageText, fileIds)
    }

    // ─── Reply / Forward UX ────────────────────────────────────────────────────

    private fun setPendingReply(item: MessageItem) {
        viewModel.setPendingReply(item)
        binding.messageEditText.requestFocus()
        scheduleDraftSave()
    }

    private fun clearPendingReply(saveDraft: Boolean = true) {
        viewModel.clearPendingReply()
        if (saveDraft) scheduleDraftSave()
    }

    private fun scheduleDraftSave(immediate: Boolean = false) {
        if (!supportsDrafts || suppressDraftSave) return
        viewModel.saveDraft(binding.messageEditText.text?.toString().orEmpty(), immediate)
    }

    // ─── Edit / Delete UX ─────────────────────────────────────────────────────

    private fun setPendingEdit(item: MessageItem) {
        // Edit и reply — взаимоисключающие режимы (VM чистит reply сам).
        // Поле ввода синхронизирует renderPendingEdit по смене состояния.
        viewModel.setPendingEdit(item)
    }

    private fun clearPendingEdit() {
        viewModel.clearPendingEdit()
        suppressDraftSave = true
        binding.messageEditText.text?.clear()
        suppressDraftSave = false
        updateSendButtonMode()
    }

    private fun confirmAndDelete(item: MessageItem) {
        com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle(R.string.delete_message_title)
            .setMessage(R.string.delete_message_message)
            .setNegativeButton(R.string.btn_cancel, null)
            .setPositiveButton(R.string.btn_delete) { _, _ ->
                viewModel.deleteMessage(item.messageId)
            }
            .show()
    }

    /** Краткое описание вложений для reply preview (когда текста нет). */
    private fun buildAttachmentSummary(attachments: List<barkfluff.shared.Shared.MessageAttachment>): String {
        if (attachments.isEmpty()) return ""
        val photos = attachments.count {
            it.type == barkfluff.shared.Shared.MessageAttachmentType.IMAGE ||
            it.type == barkfluff.shared.Shared.MessageAttachmentType.GIF
        }
        val videos = attachments.count { it.type == barkfluff.shared.Shared.MessageAttachmentType.VIDEO }
        val docs = attachments.count { it.type == barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT }
        val audios = attachments.count {
            it.type == barkfluff.shared.Shared.MessageAttachmentType.AUDIO ||
            it.type == barkfluff.shared.Shared.MessageAttachmentType.VOICE
        }
        val stickers = attachments.count { it.type == barkfluff.shared.Shared.MessageAttachmentType.STICKER }
        return when {
            photos > 0 -> getString(
                R.string.chat_attachment_photo_summary,
                resources.getQuantityString(R.plurals.photos_count, photos, photos)
            )
            videos > 0 -> getString(
                R.string.chat_attachment_video_summary,
                resources.getQuantityString(R.plurals.videos_count, videos, videos)
            )
            audios > 0 -> getString(R.string.chat_attachment_audio_summary, audios)
            docs > 0 -> getString(
                R.string.chat_attachment_file_summary,
                resources.getQuantityString(R.plurals.files_count, docs, docs)
            )
            stickers > 0 -> getString(R.string.chat_attachment_sticker_summary)
            else -> ""
        }
    }

    /**
     * Скролл к сообщению с указанным ID и кратковременная подсветка bubble.
     * Если сообщение не загружено в адаптер — показываем Toast.
     */
    private fun scrollToAndHighlightMessage(messageId: Long) {
        if (messageId <= 0L) return
        val list = messageAdapter.currentList
        val position = list.indexOfFirst {
            it.type == MessageType.MESSAGE && it.messageId == messageId
        }
        if (position < 0) {
            Toast.makeText(this, R.string.message_not_loaded, Toast.LENGTH_SHORT).show()
            return
        }

        val rv = binding.messagesRecyclerView
        val lm = rv.layoutManager as? LinearLayoutManager ?: return

        val smoothScroller = object : LinearSmoothScroller(this) {
            override fun getVerticalSnapPreference(): Int = SNAP_TO_ANY
        }
        smoothScroller.targetPosition = position
        lm.startSmoothScroll(smoothScroller)

        // Подсветка после прокрутки: ждём один кадр, чтобы ViewHolder стал видимым
        rv.post {
            highlightMessageAt(position)
        }
    }

    private fun highlightMessageAt(position: Int) {
        val rv = binding.messagesRecyclerView
        val holder = rv.findViewHolderForAdapterPosition(position)
        val bubble = holder?.itemView?.findViewById<View>(R.id.messageCard) ?: holder?.itemView ?: return

        val tv = android.util.TypedValue()
        theme.resolveAttribute(androidx.appcompat.R.attr.colorPrimary, tv, true)
        val baseColor = tv.data
        val startAlpha = 90  // ~35%
        val startColor = (baseColor and 0x00FFFFFF) or (startAlpha shl 24)
        val endColor = baseColor and 0x00FFFFFF  // alpha 0

        val originalForeground = bubble.foreground
        val animator = ValueAnimator.ofArgb(startColor, endColor).apply {
            duration = 1500
            addUpdateListener { va ->
                bubble.foreground = android.graphics.drawable.ColorDrawable(va.animatedValue as Int)
            }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: android.animation.Animator) {
                    bubble.foreground = originalForeground
                }
            })
        }
        animator.start()
    }

    /** Идентификаторы пунктов оверлея действий — только для маршрутизации внутри showMessageActionMenu. */
    private object MessageActionId {
        const val REPLY = 1
        const val COPY_TEXT = 2
        const val COPY_IMAGE = 3
        const val SAVE_IMAGES = 4
        const val SAVE_DOCS = 5
        const val EDIT = 6
        const val DELETE = 7
        const val FORWARD = 8
        const val PIN = 9
        const val COPY_MARKDOWN = 10
        const val PROPERTIES = 11
        const val SELECT = 12
    }

    private fun showMessageActionMenu(bubble: View, item: MessageItem) {
        val isOwnMessage = item.senderId == currentUserId
        val imageAtts = item.attachments.filter {
            it.type == barkfluff.shared.Shared.MessageAttachmentType.IMAGE ||
            it.type == barkfluff.shared.Shared.MessageAttachmentType.GIF
        }
        val docAtts = item.attachments.filter {
            it.type == barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT
        }
        val hasText = item.text.isNotBlank()
        val isPinned = viewModel.isMessagePinned(item.messageId)

        val actions = buildList {
            add(MessageActionsOverlay.Action(MessageActionId.REPLY, R.drawable.ic_action_reply, getString(R.string.msg_action_reply)))
            if (hasText) {
                add(MessageActionsOverlay.Action(MessageActionId.COPY_TEXT, R.drawable.ic_action_copy_text, getString(R.string.msg_action_copy_text)))
                add(MessageActionsOverlay.Action(MessageActionId.COPY_MARKDOWN, R.drawable.ic_action_copy_markdown, getString(R.string.msg_action_copy_markdown)))
            }
            if (imageAtts.size == 1) {
                add(MessageActionsOverlay.Action(MessageActionId.COPY_IMAGE, R.drawable.ic_action_copy_image, getString(R.string.msg_action_copy_image)))
            }
            if (imageAtts.isNotEmpty()) {
                add(MessageActionsOverlay.Action(
                    MessageActionId.SAVE_IMAGES, R.drawable.ic_action_save,
                    getString(if (imageAtts.size == 1) R.string.message_save_image else R.string.message_save_images)
                ))
            }
            if (docAtts.isNotEmpty()) {
                add(MessageActionsOverlay.Action(MessageActionId.SAVE_DOCS, R.drawable.ic_action_download, getString(R.string.msg_action_save_to_downloads)))
            }
            if (isOwnMessage) {
                add(MessageActionsOverlay.Action(MessageActionId.EDIT, R.drawable.ic_action_edit, getString(R.string.msg_action_edit)))
                add(MessageActionsOverlay.Action(MessageActionId.DELETE, R.drawable.ic_action_delete, getString(R.string.msg_action_delete), danger = true))
            }
            add(MessageActionsOverlay.Action(MessageActionId.FORWARD, R.drawable.ic_action_forward, getString(R.string.msg_action_forward)))
            add(MessageActionsOverlay.Action(
                MessageActionId.PIN,
                if (isPinned) R.drawable.ic_action_unpin else R.drawable.ic_action_pin,
                getString(if (isPinned) R.string.message_unpin else R.string.message_pin)
            ))
            add(MessageActionsOverlay.Action(MessageActionId.PROPERTIES, R.drawable.ic_action_properties, getString(R.string.msg_action_properties)))
            add(MessageActionsOverlay.Action(MessageActionId.SELECT, R.drawable.ic_action_select, getString(R.string.msg_action_select)))
        }

        messageActionsOverlay.show(
            bubble = bubble,
            actions = actions,
            alignEnd = isOwnMessage,
            onDismiss = {
                // Закрытие анимированное (~180мс) — за это время действие могло уже включить
                // режим выделения (SELECT), который сам управляет backCallback. Не перетираем.
                if (!messageAdapter.selectionMode) {
                    backCallback.isEnabled = binding.stickerPreviewOverlay.visibility == View.VISIBLE ||
                        inputPanelState == InputPanelState.STICKER_PANEL
                }
            }
        ) { actionId ->
            when (actionId) {
                MessageActionId.REPLY -> setPendingReply(item)
                MessageActionId.COPY_TEXT -> copyMessageText(item)
                MessageActionId.COPY_MARKDOWN -> copyMessageMarkdown(item)
                MessageActionId.COPY_IMAGE -> copyMessageImage(imageAtts.first())
                MessageActionId.SAVE_IMAGES -> saveMessageImages(imageAtts)
                MessageActionId.SAVE_DOCS -> saveMessageDocuments(docAtts)
                MessageActionId.EDIT -> setPendingEdit(item)
                MessageActionId.DELETE -> confirmAndDelete(item)
                MessageActionId.FORWARD -> {
                    // Если сообщение само является пересланным — пересылаем оригиналы, а не snapshot.
                    // Пересланных может быть несколько, поэтому берём все, а не первый: иначе
                    // пересылка пачки потеряла бы всё, кроме одного сообщения.
                    val sourceIds = item.attachments
                        .filter { it.type == barkfluff.shared.Shared.MessageAttachmentType.FORWARDED_MESSAGE && it.hasForwardedMessage() }
                        .sortedBy { it.forwardedMessage.order }
                        .map { it.forwardedMessage.originalMessageId }
                        .filter { it > 0 }
                        .ifEmpty { listOf(item.messageId) }
                    com.barkfluff.client.dialog.ForwardChatPickerBottomSheet
                        .newInstance(sourceIds.toLongArray())
                        .show(supportFragmentManager, "forward_picker")
                }
                MessageActionId.PIN -> viewModel.togglePinForMessage(item)
                MessageActionId.PROPERTIES -> showMessageProperties(item)
                MessageActionId.SELECT -> enterSelectionMode(item)
            }
        }
        backCallback.isEnabled = true
    }

    private fun copyMessageText(item: MessageItem) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(CLIP_LABEL, MarkdownRenderer.strip(item.text)))
        Toast.makeText(this, R.string.message_text_copied, Toast.LENGTH_SHORT).show()
    }

    private fun copyMessageMarkdown(item: MessageItem) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(CLIP_LABEL, item.text))
        Toast.makeText(this, R.string.message_text_copied, Toast.LENGTH_SHORT).show()
    }

    private fun showMessageProperties(item: MessageItem) {
        val isOwnMessage = item.senderId == currentUserId
        val senderLabel = if (isOwnMessage) getString(R.string.current_user) else (item.senderName ?: chatTitle)
        val sentAt = SimpleDateFormat("dd.MM.yyyy HH:mm:ss", Locale.getDefault()).format(Date(item.timestamp))

        val lines = buildList {
            add(getString(R.string.msg_properties_id, item.messageId))
            add(getString(R.string.msg_properties_sender, senderLabel))
            add(getString(R.string.msg_properties_sent_at, sentAt))
            add(getString(if (item.isEdited) R.string.msg_properties_edited_yes else R.string.msg_properties_edited_no))
            if (isOwnMessage) {
                add(getString(if (item.readStatus == ReadStatus.READ) R.string.msg_properties_read_yes else R.string.msg_properties_read_no))
            }
            add("")
            if (item.attachments.isEmpty()) {
                add(getString(R.string.msg_properties_no_attachments))
            } else {
                add(getString(R.string.msg_properties_attachments_header))
                item.attachments.forEach { att ->
                    add("• ${att.type.name} — ${att.fileName.ifBlank { att.fileId }}")
                }
            }
        }

        val propertiesText = lines.joinToString("\n")
        com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle(R.string.msg_action_properties)
            .setMessage(propertiesText)
            .setPositiveButton(R.string.btn_close, null)
            .show()
    }

    private fun enterSelectionMode(item: MessageItem) {
        selectedMessageIds.clear()
        selectedMessageIds.add(item.messageId)
        messageAdapter.setSelectionMode(true, selectedMessageIds.toSet())
        updateSelectionToolbar()
        // INVISIBLE, не GONE: pinnedMessageBar и e2eBanner привязаны к нижнему краю
        // chatHeaderBar constraint-цепочкой — GONE обнулил бы её высоту, и они уехали
        // бы под статус-бар. selectionToolbar просто перекрывает её сверху.
        binding.chatHeaderBar.visibility = View.INVISIBLE
        binding.selectionToolbar.visibility = View.VISIBLE
        backCallback.isEnabled = true
    }

    private fun exitSelectionMode() {
        selectedMessageIds.clear()
        messageAdapter.setSelectionMode(false)
        binding.selectionToolbar.visibility = View.GONE
        binding.chatHeaderBar.visibility = View.VISIBLE
        backCallback.isEnabled = messageActionsOverlay.isShowing ||
            binding.stickerPreviewOverlay.visibility == View.VISIBLE ||
            inputPanelState == InputPanelState.STICKER_PANEL
    }

    private fun toggleSelection(messageId: Long) {
        if (!messageAdapter.selectionMode) return
        if (selectedMessageIds.contains(messageId)) {
            selectedMessageIds.remove(messageId)
        } else {
            selectedMessageIds.add(messageId)
        }
        if (selectedMessageIds.isEmpty()) {
            exitSelectionMode()
        } else {
            messageAdapter.setSelected(messageId, selectedMessageIds.toSet())
            updateSelectionToolbar()
        }
    }

    private fun updateSelectionToolbar() {
        binding.selectionCountText.text = getString(R.string.create_group_members_count, selectedMessageIds.size)
    }

    /** Сообщения из выбранных ID в порядке их следования в текущем списке чата. */
    private fun selectedMessagesInOrder(): List<MessageItem> =
        messageAdapter.currentList.filter { it.type == MessageType.MESSAGE && it.messageId in selectedMessageIds }

    private fun copySelectedMessages() {
        val texts = selectedMessagesInOrder().map { MarkdownRenderer.strip(it.text) }.filter { it.isNotBlank() }
        exitSelectionMode()
        if (texts.isEmpty()) return
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(CLIP_LABEL_MULTIPLE, texts.joinToString("\n\n")))
        Toast.makeText(this, R.string.message_text_copied, Toast.LENGTH_SHORT).show()
    }

    private fun forwardSelectedMessages() {
        val ids = selectedMessagesInOrder().map { it.messageId }
        exitSelectionMode()
        if (ids.isEmpty()) return
        com.barkfluff.client.dialog.ForwardChatPickerBottomSheet
            .newInstance(ids.toLongArray())
            .show(supportFragmentManager, "forward_picker")
    }

    private fun confirmAndDeleteSelected() {
        val ownIds = selectedMessagesInOrder().filter { it.senderId == currentUserId }.map { it.messageId }
        exitSelectionMode()
        if (ownIds.isEmpty()) return
        com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle(R.string.delete_message_title)
            .setMessage(R.string.delete_message_message)
            .setNegativeButton(R.string.btn_cancel, null)
            .setPositiveButton(R.string.btn_delete) { _, _ ->
                lifecycleScope.launch {
                    var failed = 0
                    for (id in ownIds) {
                        val result = chatRepository.deleteMessage(id)
                        if (result.isSuccess) viewModel.removeMessageById(id) else failed++
                    }
                    if (failed > 0) {
                        Toast.makeText(
                            this@ChatActivity,
                            getString(R.string.message_delete_batch_failed, failed),
                            Toast.LENGTH_SHORT
                        ).show()
                    }
                }
            }
            .show()
    }

    private fun copyMessageImage(att: barkfluff.shared.Shared.MessageAttachment) {
        lifecycleScope.launch {
            val srcFile = FileCache.getFile(att.fileId) ?: chatRepository.downloadFile(att.fileId)
            if (srcFile == null) {
                Toast.makeText(this@ChatActivity, R.string.message_image_download_failed, Toast.LENGTH_SHORT).show()
                return@launch
            }
            try {
                // FileCache хранит файл без расширения → FileProvider не может определить MIME.
                // Копируем во временный файл с расширением, чтобы ContentResolver вернул image/* MIME.
                val ext = att.fileName.substringAfterLast('.', "").lowercase().ifBlank { "jpg" }
                val resolvedMime = FileSaveUtils.getMimeType("dummy.$ext")
                val mime = if (resolvedMime.startsWith("image/")) resolvedMime else "image/jpeg"
                val tempFile = withContext(Dispatchers.IO) {
                    val tempDir = File(cacheDir, "clipboard").apply { if (!exists()) mkdirs() }
                    val out = File(tempDir, "img_${System.currentTimeMillis()}.$ext")
                    srcFile.inputStream().use { input ->
                        out.outputStream().use { input.copyTo(it) }
                    }
                    out
                }
                val uri = FileProvider.getUriForFile(
                    this@ChatActivity, "${packageName}.fileprovider", tempFile
                )
                val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                val clip = ClipData(
                    ClipDescription("BarkFluff image", arrayOf(mime)),
                    ClipData.Item(uri)
                )
                cm.setPrimaryClip(clip)
                Toast.makeText(this@ChatActivity, R.string.message_image_copied, Toast.LENGTH_SHORT).show()
            } catch (e: Exception) {
                Toast.makeText(this@ChatActivity, R.string.message_copy_failed, Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun saveMessageImages(images: List<barkfluff.shared.Shared.MessageAttachment>) {
        if (images.isEmpty()) return
        Toast.makeText(this, R.string.saving, Toast.LENGTH_SHORT).show()
        lifecycleScope.launch {
            var saved = 0
            for (att in images) {
                val name = att.fileName.ifBlank { "image_${att.fileId.take(8)}.jpg" }
                val file = FileCache.getFile(att.fileId) ?: chatRepository.downloadFile(att.fileId)
                if (file != null) {
                    val ok = withContext(Dispatchers.IO) {
                        FileSaveUtils.saveImageToGallery(this@ChatActivity, file, name)
                    }
                    if (ok) saved++
                }
            }
            Toast.makeText(
                this@ChatActivity,
                if (saved > 0) getString(R.string.saved_to_gallery, saved) else getString(R.string.save_failed),
                Toast.LENGTH_SHORT
            ).show()
        }
    }

    private fun saveMessageDocuments(docs: List<barkfluff.shared.Shared.MessageAttachment>) {
        if (docs.isEmpty()) return
        Toast.makeText(this, R.string.saving, Toast.LENGTH_SHORT).show()
        lifecycleScope.launch {
            var saved = 0
            for (att in docs) {
                val name = att.fileName.ifBlank { "file_${att.fileId.take(8)}" }
                val file = FileCache.getFile(att.fileId) ?: chatRepository.downloadFile(att.fileId)
                if (file != null) {
                    val ok = withContext(Dispatchers.IO) {
                        FileSaveUtils.saveToDownloads(this@ChatActivity, file, name)
                    }
                    if (ok) saved++
                }
            }
            Toast.makeText(
                this@ChatActivity,
                if (saved > 0) getString(R.string.saved_to_downloads, saved) else getString(R.string.save_failed),
                Toast.LENGTH_SHORT
            ).show()
        }
    }

    /** Контекстное меню чата (кнопка «три точки»): переключение уведомлений. */
    private fun showChatMenu(anchor: View) {
        val popup = android.widget.PopupMenu(this, anchor)
        val muteTitle = if (isChatMuted) getString(R.string.chat_menu_unmute) else getString(R.string.chat_menu_mute)
        val muteItem = popup.menu.add(muteTitle)
        popup.setOnMenuItemClickListener { item ->
            if (item === muteItem) {
                toggleChatMute()
                true
            } else {
                false
            }
        }
        popup.show()
    }

    private fun toggleChatMute() {
        val newMuted = !viewModel.uiState.value.isChatMuted
        lifecycleScope.launch {
            val result = grpcManager.setChatMuted(chatId, newMuted)
            if (result.isSuccess) {
                viewModel.setChatMuted(newMuted)
                isChatMuted = newMuted
                globalParam.setChatMutedLocal(chatId, newMuted)
                val msg = if (newMuted) getString(R.string.chat_muted) else getString(R.string.chat_unmuted)
                android.widget.Toast.makeText(this@ChatActivity, msg, android.widget.Toast.LENGTH_SHORT).show()
            } else {
                android.widget.Toast.makeText(this@ChatActivity, getString(R.string.chat_mute_error), android.widget.Toast.LENGTH_SHORT).show()
            }
        }
    }

    private var onlineStatusJob: Job? = null
    private var onlineStatusSubscription: Job? = null

    /**
     * Записывает онлайн-статус, не давая индикатору набора текста быть перезаписанным:
     * пока хотя бы один собеседник печатает — onlineStatusTextView остаётся отведён под typing-текст.
     */
    private fun applyOnlineStatus(text: CharSequence, indicatorVisible: Boolean) {
        lastStatusText = text
        lastIndicatorVisible = indicatorVisible
        binding.onlineIndicator.visibility = if (indicatorVisible) View.VISIBLE else View.GONE
        if (typingUsers.isEmpty()) {
            binding.onlineStatusTextView.text = text
        }
    }

    private fun loadOnlineStatus(userId: Long) {
        // Отменяем предыдущий job если есть
        onlineStatusJob?.cancel()

        onlineStatusJob = lifecycleScope.launch {
            try {
                // Первоначальная загрузка
                fetchAndDisplayOnlineStatus(userId)

                // Периодическое обновление каждые 30 секунд
                while (true) {
                    delay(30_000)
                    fetchAndDisplayOnlineStatus(userId)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading online status", e)
            }
        }

        // Подписка на streaming обновления онлайн-статуса через RealtimeService
        onlineStatusSubscription?.cancel()
        onlineStatusSubscription = lifecycleScope.launch {
            realtimeService.onlineStatuses.collect { status ->
                if (status.userId == userId) {
                    withContext(Dispatchers.Main) {
                        val isOnline = status.status.number == barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE.number
                        if (isOnline) {
                            applyOnlineStatus(getString(R.string.profile_online), true)
                        } else {
                            val lastSeen = OnlineTimeFormatter.formatLastSeen(this@ChatActivity, status.lastSeen.seconds * 1000)
                            applyOnlineStatus(lastSeen, false)
                        }
                    }
                }
            }
        }
    }

    private suspend fun fetchAndDisplayOnlineStatus(userId: Long) {
        try {
            val onlinerClient = grpcManager.onlinerClient
            if (onlinerClient != null) {
                val request = barkfluff.onliner.OnlinerApiOuterClass.GetOnlineStatusRequest.newBuilder()
                    .addUserIds(userId)
                    .build()
                val response = onlinerClient.getOnlineStatus(request)
                val userStatus = response.usersStatusesList.firstOrNull()
                withContext(Dispatchers.Main) {
                    if (userStatus != null) {
                        val isOnline = userStatus.status.getNumber() == barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE.getNumber()
                        if (isOnline) {
                            applyOnlineStatus(getString(R.string.profile_online), true)
                        } else {
                            val lastSeen = OnlineTimeFormatter.formatLastSeen(this@ChatActivity, userStatus.lastSeen.seconds * 1000)
                            applyOnlineStatus(lastSeen, false)
                        }
                    } else {
                        applyOnlineStatus(getString(R.string.status_recently_seen), false)
                    }
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error fetching online status", e)
        }
    }

    private fun getMimeType(fileName: String): String? {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        if (ext.isEmpty()) return null
        return android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext)
    }

    /**
     * Возвращает true если RecyclerView прокручен до самого низа
     * (последнее сообщение полностью видно или мы у нижней границы).
     */
    private fun isRecyclerViewAtBottom(): Boolean {
        val layoutManager = binding.messagesRecyclerView.layoutManager as? LinearLayoutManager ?: return true
        val lastVisible = layoutManager.findLastCompletelyVisibleItemPosition()
        val total = layoutManager.itemCount
        return total == 0 || lastVisible >= total - 1
    }

    // ═══════════════════════════════════════════════════════════════
    // Закреплённые сообщения
    // ═══════════════════════════════════════════════════════════════

    private fun setupPinnedBar() {
        binding.pinnedMessageBar.setOnClickListener {
            val first = viewModel.uiState.value.firstPinnedMessageId
            if (first > 0L) scrollToMessageId(first)
        }
        binding.pinnedListButton.setOnClickListener {
            val intent = Intent(this, PinnedMessagesActivity::class.java)
                .putExtra(PinnedMessagesActivity.EXTRA_CHAT_ID, chatId)
            pinnedListLauncher.launch(intent)
        }
    }

    private val pinnedListLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            val targetId = result.data?.getLongExtra(PinnedMessagesActivity.RESULT_SCROLL_TO_MESSAGE_ID, 0L) ?: 0L
            if (targetId > 0L) scrollToMessageId(targetId)
        }
    }

    private fun updatePinnedBar(pinnedCount: Int, pinnedPreview: String) {
        if (pinnedCount <= 0) {
            binding.pinnedMessageBar.visibility = View.GONE
            return
        }
        binding.pinnedMessageBar.visibility = View.VISIBLE
        binding.pinnedTitle.text = if (pinnedCount > 1) {
            getString(R.string.pinned_message_count, pinnedCount)
        } else {
            getString(R.string.pinned_message)
        }
        binding.pinnedPreview.text = if (pinnedPreview.isBlank()) getString(R.string.attachment_preview) else pinnedPreview
    }

    private fun scrollToMessageId(messageId: Long) {
        val list = messageAdapter.currentList
        val idx = list.indexOfFirst { it.type == MessageType.MESSAGE && it.messageId == messageId }
        if (idx >= 0) {
            binding.messagesRecyclerView.smoothScrollToPosition(idx)
        } else {
            // Сообщение не загружено — VM подгружает окно вокруг него.
            lifecycleScope.launch {
                if (viewModel.ensureMessageLoaded(messageId)) {
                    val newIdx = messageAdapter.currentList.indexOfFirst { it.type == MessageType.MESSAGE && it.messageId == messageId }
                    if (newIdx >= 0) binding.messagesRecyclerView.scrollToPosition(newIdx)
                }
            }
        }
    }

    override fun onStart() {
        super.onStart()
        // При возврате из фона — подгружаем пропущенное и синхронизируем edit/delete
        // (токен, ожидание переподключения стримов и догрузка — в ChatViewModel).
        viewModel.onStartCatchUp()
    }

    override fun onResume() {
        super.onResume()
        setupChatBackground()
        refreshChatBackgroundSettings()
        // Обновляем список, чтобы отразить изменения кэша (например, после удаления видео из кэша)
        if (::messageAdapter.isInitialized) {
            messageAdapter.messageCornerRadiusDp = globalParam.chatMessageCornerRadius
            messageAdapter.stickerSizeDp = globalParam.chatStickerSizeDp
            // notifyItemRangeChanged вместо notifyDataSetChanged: содержимое перерисовывается
            // так же, но структура списка остаётся валидной — сохраняются пул холдеров,
            // позиция скролла и анимации.
            messageAdapter.notifyItemRangeChanged(0, messageAdapter.itemCount)
        }
    }

    private fun refreshChatBackgroundSettings() {
        lifecycleScope.launch {
            grpcManager.getUserSettings().onSuccess { settings ->
                globalParam.applyChatBackgroundSettings(
                    settings.globalChatBackgroundFileId,
                    settings.chatBackgroundFileIds
                )
                setupChatBackground()
            }
        }
    }

    override fun onStop() {
        scheduleDraftSave(immediate = true)
        finishVoiceRecording(shouldSend = false)
        stopTypingHeartbeat(sendCancel = true)
        if (messageActionsOverlay.isShowing) messageActionsOverlay.dismiss(animate = false)
        super.onStop()
    }

    override fun onDestroy() {
        super.onDestroy()
        // Сбрасываем открытый чат
        OpenChatManager.closeChat()
        onlineStatusJob?.cancel()
        onlineStatusSubscription?.cancel()
        stopTypingHeartbeat(sendCancel = true)
        realtimeService.changeTypingSubscription(emptyList())
        headerNameAnimator?.cancel()
        headerStatusAnimator?.cancel()
        sendButtonMorphAnimator?.cancel()
        voiceTimerJob?.cancel()
        voiceDotAnimator?.cancel()
    }
}
