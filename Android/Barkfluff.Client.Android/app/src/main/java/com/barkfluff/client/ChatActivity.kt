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
import android.media.MediaRecorder
import android.net.Uri
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.util.Log
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.animation.DecelerateInterpolator
import android.widget.TextView
import android.widget.Toast
import androidx.core.content.FileProvider
import androidx.core.view.isVisible
import androidx.activity.result.contract.ActivityResultContracts
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
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.drafts.ChatDraftRepository
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
import com.barkfluff.client.utils.MessageItemAnimator
import com.barkfluff.client.utils.MessageTimeSpacingDecoration
import com.barkfluff.client.utils.OnlineTimeFormatter
import com.barkfluff.client.utils.applySpringPress
import com.google.android.material.color.MaterialColors
import com.yalantis.ucrop.UCrop
import androidx.activity.OnBackPressedCallback
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.core.view.updateLayoutParams
import androidx.core.view.updatePadding
import androidx.recyclerview.widget.GridLayoutManager
import kotlinx.coroutines.async
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import coil.load
import java.io.File
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

/**
 * Activity для отображения чата и переписки.
 * Поддерживает:
 * - Отображение сообщений с пагинацией (подгрузка по скролу)
 * - Отправку текстовых сообщений и изображений
 * - Отображение статуса онлайна собеседника
 * - Профиль чата по клику на кнопку информации
 */
class ChatActivity : AppCompatActivity() {

    private lateinit var binding: ActivityChatBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var realtimeService: RealtimeService
    private lateinit var chatRepository: ChatRepository
    private lateinit var messageAdapter: MessageAdapter
    private lateinit var chatCacheRepository: ChatCacheRepository
    private lateinit var chatDraftRepository: ChatDraftRepository
    private var cacheScope: CacheScope? = null

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

    // Пагинация сообщений
    private var isLoadingMessages = false
    private var hasMoreMessagesUp = true
    private var hasMoreMessagesDown = true
    private var firstVisibleMessageId: Long = 0L
    private var lastVisibleMessageId: Long = 0L
    private var loadMessagesJob: Job? = null

    // Непрочитанные сообщения
    private var firstUnreadMessageId: Long = 0L
    private var lastBottomReadTriggerId: Long = -1L

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

    // Активный ответ (reply): ID оригинального сообщения для отправки в forwarded_message_id.
    // 0 = нет активного ответа.
    private var pendingReplyMessageId: Long = 0L
    private var isRestoringDraft = false
    private var draftSaveJob: Job? = null

    // Активное редактирование: ID редактируемого сообщения (0 = режим обычной отправки).
    // pendingEditFileIds — file_id существующих вложений (передаются в EditMessage без изменений).
    private var pendingEditMessageId: Long = 0L
    private var pendingEditFileIds: List<String> = emptyList()

    // Закреплённые сообщения
    private val pinnedById = mutableMapOf<Long, barkfluff.shared.Shared.PinnedMessageInfo>()
    private val pinnedSorted = mutableListOf<barkfluff.shared.Shared.PinnedMessageInfo>()
    private var pinnedTotalCount: Int = 0

    // Inline стикер-панель
    private enum class InputPanelState { NONE, KEYBOARD, STICKER_PANEL }
    private var inputPanelState = InputPanelState.NONE
    private var lastKnownKeyboardHeight = 0
    private var isTransitioningToStickers = false
    private var stickerDataLoaded = false
    private lateinit var stickerPanelAdapter: StickerPanelAdapter

    // Callback назад — включён только когда стикер-панель или оверлей открыты
    private lateinit var backCallback: OnBackPressedCallback

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
        private const val LOAD_MESSAGES_DELAY_MS = 500L
        private const val MIN_VOICE_RECORDING_MS = 500L

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
            // Другой чат — пересоздаём activity с новым intent
            setIntent(intent)
            recreate()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityChatBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        globalParam = GlobalParam(this)
        grpcManager = app.grpcManager
        realtimeService = app.realtimeService
        chatRepository = ChatRepository(this, grpcManager)
        chatCacheRepository = app.chatCacheRepository
        chatDraftRepository = app.chatDraftRepository

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
        cacheScope = CacheScope.from(globalParam)

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
        loadChatInfoAndMessages()
        restoreChatDraft()
        loadPinnedMessages()

        // Устанавливаем этот чат как открытый
        OpenChatManager.setOpenChat(chatId)

        // Убираем уведомление этого чата из шторки если оно висит
        NotificationHelper.dismissForChat(applicationContext, chatId)

        // Подписываемся на обновления в реальном времени
        subscribeToRealtimeEvents()

        // Обновляем подписку на онлайн-статус собеседника и сразу запрашиваем текущий статус
        if (!isGroupChat && otherUserId > 0) {
            realtimeService.changeOnlineSubscription(listOf(otherUserId))
            loadOnlineStatus(otherUserId)
        }

        // Подписка на индикатор набора текста в этом чате
        realtimeService.changeTypingSubscription(listOf(chatId))
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
                    Toast.makeText(this, "Секретный чат не найден", Toast.LENGTH_LONG).show()
                    finish()
                    return
                }
                binding.chatNameTextView.text = "Секретный чат · ${chat.peerUserId}"
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

        val backBaseMargin = (binding.btnBack.layoutParams as ViewGroup.MarginLayoutParams).topMargin
        val moreBaseMargin = (binding.btnMore.layoutParams as ViewGroup.MarginLayoutParams).topMargin
        val infoCardBaseMargin = (binding.chatInfoCard.layoutParams as ViewGroup.MarginLayoutParams).topMargin

        val attachBaseMargin = (binding.attachButton.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        val stickerBaseMargin = (binding.stickerButton.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        val sendBaseMargin = (binding.sendButton.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        val inputBaseMargin = (binding.messageInputLayout.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        val recyclerBasePaddingTop = binding.messagesRecyclerView.paddingTop
        val recyclerBasePaddingBottom = binding.messagesRecyclerView.paddingBottom
        // Высота полосы, зарезервированной под кнопки ввода (inputRowBottom, фикс. 64dp) —
        // именно на столько лента должна не доходить контентом до нижнего края экрана.
        val inputRowBandPx = binding.inputRowBottom.layoutParams.height

        ViewCompat.setOnApplyWindowInsetsListener(binding.chatRootLayout) { _, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            val bottomInset = maxOf(bars.bottom, ime.bottom)

            binding.btnBack.updateLayoutParams<ViewGroup.MarginLayoutParams> { topMargin = backBaseMargin + bars.top }
            binding.btnMore.updateLayoutParams<ViewGroup.MarginLayoutParams> { topMargin = moreBaseMargin + bars.top }
            binding.chatInfoCard.updateLayoutParams<ViewGroup.MarginLayoutParams> { topMargin = infoCardBaseMargin + bars.top }
            // Recyclerview остаётся edge-to-edge (constraint на true parent bottom, как и сверху) —
            // фон/обои ленты уходят под панель ввода и жестовую навигацию, а контент просто
            // не долистывается ниже paddingBottom.
            binding.messagesRecyclerView.updatePadding(
                top = recyclerBasePaddingTop + bars.top,
                bottom = recyclerBasePaddingBottom + inputRowBandPx + bottomInset
            )

            binding.attachButton.updateLayoutParams<ViewGroup.MarginLayoutParams> { bottomMargin = attachBaseMargin + bottomInset }
            binding.stickerButton.updateLayoutParams<ViewGroup.MarginLayoutParams> { bottomMargin = stickerBaseMargin + bottomInset }
            binding.sendButton.updateLayoutParams<ViewGroup.MarginLayoutParams> { bottomMargin = sendBaseMargin + bottomInset }
            binding.messageInputLayout.updateLayoutParams<ViewGroup.MarginLayoutParams> { bottomMargin = inputBaseMargin + bottomInset }

            insets
        }
    }

    private fun setupToolbar() {
        binding.chatNameTextView.text = chatTitle.ifBlank { "Чат" }

        // Показываем статус онлайна только для личных чатов
        if (!isGroupChat && otherUserId > 0) {
            binding.onlineStatusTextView.text = "загрузка..."
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
                    Toast.makeText(this@ChatActivity, "Не удалось определить пользователя для звонка", Toast.LENGTH_SHORT).show()
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
                Toast.makeText(this@ChatActivity, "Не удалось начать звонок", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) {
            Toast.makeText(this, "Сервер звонков не настроен", Toast.LENGTH_SHORT).show()
            return false
        }

        val result = app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true)
        if (result.isFailure) {
            Toast.makeText(this, "Не удалось подключиться к серверу звонков", Toast.LENGTH_SHORT).show()
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

    private fun setupChatBackground() {
        val loadVersion = ++chatBackgroundLoadVersion
        val fileId = globalParam.chatBackgroundFileIdFor(chatId)
        applyDimOverlay()
        if (fileId.isBlank()) {
            binding.chatBackgroundImage.visibility = View.GONE
            return
        }

        binding.chatBackgroundImage.visibility = View.VISIBLE
        val applyBlur = globalParam.chatBackgroundBlur

        lifecycleScope.launch {
            // Сначала пробуем из дискового кэша
            val cachedFile = withContext(Dispatchers.IO) { FileCache.getFile(fileId) }
            if (cachedFile != null && cachedFile.exists()) {
                if (loadVersion == chatBackgroundLoadVersion) {
                    applyBackgroundFromFile(cachedFile, applyBlur, loadVersion)
                }
                return@launch
            }
            // Иначе скачиваем через Files API
            val url = withContext(Dispatchers.IO) {
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            } ?: return@launch

            if (loadVersion != chatBackgroundLoadVersion) return@launch

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

    private fun loadBitmapFromUrl(url: String): android.graphics.Bitmap? {
        return try {
            val conn = java.net.URL(url).openConnection() as java.net.HttpURLConnection
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
            onMessageActionRequested = { anchor, item, rawX, rawY ->
                showMessageActionMenu(anchor, item, rawX, rawY)
            },
            onReplyQuoteClick = { originalMessageId ->
                scrollToAndHighlightMessage(originalMessageId)
            },
            senderInfoProvider = { senderId -> groupMemberInfoCache[senderId] }
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
                    if (firstVisibleItem < 10 && !isLoadingMessages && hasMoreMessagesUp) {
                        loadMessagesUp()
                    }

                    // Подгрузка вниз (новые сообщения)
                    if (lastVisibleItem >= totalItemCount - 10 && !isLoadingMessages && hasMoreMessagesDown) {
                        loadMessagesDown()
                    }

                    // Показ/скрытие кнопки прокрутки вниз
                    updateScrollToBottomButton()

                    // Safety-net: долистали до самого низа и подгружать больше нечего —
                    // помечаем прочитанными все загруженные чужие сообщения (страхует от
                    // случаев, когда прогрессивная пометка при пагинации что-то не зацепила).
                    if (!hasMoreMessagesDown && isRecyclerViewAtBottom() && lastVisibleMessageId != lastBottomReadTriggerId) {
                        lastBottomReadTriggerId = lastVisibleMessageId
                        markAllLoadedMessagesAsRead()
                    }
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
                val name = "${member.firstName} ${member.lastName}".trim().ifBlank { "ID ${member.userId}" }
                val avatarSource = grpcManager.getUserData(member.userId).getOrNull()?.let { user ->
                    avatarSourceFor(user)
                }
                groupMemberInfoCache[member.userId] = name to avatarSource
            }

            messageAdapter.notifyDataSetChanged()
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
                    val name = "${user.firstName} ${user.lastName}".trim().ifBlank { "ID $userId" }
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
        if (isLoadingMessages) return
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

            // Параллельно запрашиваем актуальное состояние чата
            val chatInfoDeferred = async { chatRepository.getChatInfo(chatId) }

            // Ждём 300 мс — за это время список успевает «полететь» вниз
            delay(300)

            try {
                val serverLastMessageId = chatInfoDeferred.await().getOrNull()?.lastMessageId ?: 0L

                if (serverLastMessageId > 0L && serverLastMessageId != lastVisibleMessageId) {
                    // Есть незагруженные сообщения — подгружаем последние
                    isLoadingMessages = true
                    hasMoreMessagesDown = false
                    val result = chatRepository.loadMessages(
                        chatId = chatId,
                        fromMessageId = 0L,
                        offsetBefore = 0,
                        offsetAfter = 0,
                        count = 30
                    )
                    isLoadingMessages = false

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
        return text.isBlank() &&
            !hasPendingAttachments() &&
            pendingReplyMessageId == 0L &&
            pendingEditMessageId == 0L
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
        tintSendButton(androidx.appcompat.R.attr.colorPrimary)
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
                val dx = (event.rawX - voiceDownRawX).coerceAtMost(0f)
                binding.sendButton.translationX = dx.coerceAtLeast(-cancelDistancePx)

                val cancelNow = -dx >= cancelDistancePx
                if (cancelNow != voiceCancelPending) {
                    voiceCancelPending = cancelNow
                    tintSendButton(
                        if (cancelNow) androidx.appcompat.R.attr.colorError
                        else androidx.appcompat.R.attr.colorPrimary
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
            tintSendButton(androidx.appcompat.R.attr.colorPrimary)
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
        addOptimisticMessage(
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
                    Toast.makeText(this@ChatActivity, "Ошибка: стикер без файла", Toast.LENGTH_SHORT).show()
                    return@launch
                }
                sendMessage(text = "", fileIds = listOf(fileId))
            } catch (e: Exception) {
                Log.e(TAG, "Error sending sticker", e)
                Toast.makeText(this@ChatActivity, "Ошибка отправки стикера", Toast.LENGTH_SHORT).show()
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
                addOptimisticMessage(
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
            addOptimisticMessage(
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
            replyId = pendingReplyMessageId,
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
        isRestoringDraft = true
        binding.messageEditText.text?.clear()
        isRestoringDraft = false

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
                Toast.makeText(this@ChatActivity, "Не удалось загрузить файлы", Toast.LENGTH_SHORT).show()
                if (text.isNotBlank()) {
                    sendMessage(text = text)
                }
            }
        }
    }

    private fun sendMessage(text: String = binding.messageEditText.text.toString(), fileIds: List<String> = emptyList()) {
        val messageText = text.trim()

        // Если активен режим редактирования — редактируем существующее сообщение.
        // fileIds игнорируем (вложения не меняются), используем сохранённые pendingEditFileIds.
        val editId = pendingEditMessageId
        if (editId != 0L) {
            sendEdit(editId, messageText)
            return
        }

        // Reply без текста и без файлов — отправляем сам факт пересылки
        val replyId = pendingReplyMessageId
        if (messageText.isBlank() && fileIds.isEmpty() && replyId == 0L) return

        Log.d(TAG, "sendMessage: textLength=${messageText.length}, fileIds=$fileIds, replyId=$replyId")

        // Оптимистично добавляем сообщение в чат сразу со статусом SENDING (M3 Expressive feedback).
        // Освобождаем поле ввода и reply-bar моментально — пользователь не ждёт сетевого ответа.
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
        isRestoringDraft = true
        binding.messageEditText.text?.clear()
        clearPendingReply(saveDraft = false)
        isRestoringDraft = false

        lifecycleScope.launch {
            try {
                // Фиксируем именно отправленную версию до вызова SendMessage: более новая
                // правка пользователя получит следующее generation и не будет удалена.
                val sentDraft = chatDraftRepository.edit(chatId, messageText, replyId)
                val result = chatRepository.sendMessage(
                    chatId = chatId,
                    text = messageText,
                    fileIds = fileIds,
                    forwardedMessageId = replyId
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
                    Toast.makeText(
                        this@ChatActivity,
                        "Ошибка отправки: ${result.exceptionOrNull()?.message}",
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error sending message", e)
                updateOptimisticStatus(localId, ReadStatus.FAILED)
                Toast.makeText(
                    this@ChatActivity,
                    "Ошибка отправки: ${e.message}",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    /** Добавляет оптимистичный item (со статусом SENDING) в конец списка и скроллит вниз. */
    private fun addOptimisticMessage(item: MessageItem) {
        val currentList = messageAdapter.currentList
            .filter { it.type != MessageType.FOOTER }
            .toMutableList()
        currentList.removeAll { it.type == MessageType.UNREAD_SEPARATOR }

        val msgDate = startOfDay(item.timestamp)
        val lastItem = currentList.lastOrNull()
        if (lastItem == null || (lastItem.type != MessageType.MESSAGE && lastItem.type != MessageType.SYSTEM) || startOfDay(lastItem.timestamp) != msgDate) {
            currentList.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
        }
        currentList.add(item)
        messageAdapter.submitList(currentList) {
            val lastIdx = messageAdapter.itemCount - 1
            if (lastIdx >= 0) binding.messagesRecyclerView.scrollToPosition(lastIdx)
        }
    }

    /** Заменяет оптимистичный item (по localId) на серверный с указанным статусом. */
    private fun replaceOptimisticByLocalId(localId: String, replacement: MessageItem) {
        val currentList = messageAdapter.currentList.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            currentList[idx] = replacement.copy(localId = localId)
            messageAdapter.submitList(currentList)
        }
    }

    /** Обновляет статус оптимистичного item (SENDING→SENT/FAILED), не подменяя сам item. */
    private fun updateOptimisticStatus(localId: String, status: ReadStatus) {
        val currentList = messageAdapter.currentList.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            currentList[idx] = currentList[idx].copy(readStatus = status)
            messageAdapter.submitList(currentList)
        }
    }

    /** Обновляет inline-прогресс аплоада медиа на оптимистичном сообщении (0..100). */
    private fun updateOptimisticUploadProgress(localId: String, progress: Int) {
        val currentList = messageAdapter.currentList.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            currentList[idx] = currentList[idx].copy(uploadProgress = progress.coerceIn(0, 100))
            messageAdapter.submitList(currentList)
        }
    }

    /** Сбрасывает uploadProgress (аплоад завершён). Если serverMessageId != 0 — заменяет messageId оптимистичного item. */
    private fun clearOptimisticUploadProgress(localId: String, serverMessageId: Long) {
        val currentList = messageAdapter.currentList.toMutableList()
        val idx = currentList.indexOfFirst { it.localId == localId }
        if (idx >= 0) {
            val item = currentList[idx]
            currentList[idx] = item.copy(
                uploadProgress = null,
                messageId = if (serverMessageId != 0L) serverMessageId else item.messageId,
                readStatus = if (item.readStatus == ReadStatus.SENDING || item.readStatus == ReadStatus.FAILED) ReadStatus.SENT else item.readStatus
            )
            messageAdapter.submitList(currentList)
        }
    }

    private fun sendEdit(messageId: Long, text: String) {
        val fileIds = pendingEditFileIds
        if (text.isBlank() && fileIds.isEmpty()) {
            Toast.makeText(this, "Сообщение не может быть пустым", Toast.LENGTH_SHORT).show()
            return
        }

        lifecycleScope.launch {
            val result = chatRepository.editMessage(messageId, text, fileIds)
            if (result.isSuccess) {
                binding.messageEditText.text?.clear()
                clearPendingEdit()
                // Применяем сразу, чтобы не ждать стрим
                applyEditedMessage(result.getOrNull()!!)
            } else {
                Toast.makeText(
                    this@ChatActivity,
                    "Ошибка редактирования: ${result.exceptionOrNull()?.message}",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    // ─── Reply / Forward UX ────────────────────────────────────────────────────

    private fun setPendingReply(item: MessageItem) {
        pendingReplyMessageId = item.messageId

        val author = item.senderName?.takeIf { it.isNotBlank() }
            ?: if (item.senderId == currentUserId) "Вы" else "Сообщение"
        val preview = if (item.text.isNotBlank()) {
            item.text
        } else {
            buildAttachmentSummary(item.attachments)
        }

        binding.replyPreviewAuthorText.text = "Ответ $author"
        binding.replyPreviewContentText.text = preview
        binding.replyPreviewBar.visibility = View.VISIBLE
        binding.messageEditText.requestFocus()
        updateSendButtonMode()
        scheduleDraftSave()
    }

    private fun clearPendingReply(saveDraft: Boolean = true) {
        pendingReplyMessageId = 0L
        binding.replyPreviewBar.visibility = View.GONE
        updateSendButtonMode()
        if (saveDraft) scheduleDraftSave()
    }

    private fun scheduleDraftSave(immediate: Boolean = false) {
        if (!supportsDrafts || isRestoringDraft || pendingEditMessageId != 0L) return
        draftSaveJob?.cancel()
        draftSaveJob = lifecycleScope.launch {
            val text = binding.messageEditText.text?.toString().orEmpty()
            val replyId = pendingReplyMessageId
            chatDraftRepository.edit(chatId, text, replyId)
            if (immediate) chatDraftRepository.flush(chatId) else {
                delay(2_000)
                chatDraftRepository.flush(chatId)
            }
        }
    }

    private fun restoreChatDraft() {
        if (!supportsDrafts) return
        lifecycleScope.launch {
            val draft = chatDraftRepository.restore(chatId) ?: return@launch
            isRestoringDraft = true
            suppressTypingInput = true
            binding.messageEditText.setText(draft.text)
            suppressTypingInput = false
            isRestoringDraft = false
            if (draft.replyToMessageId == 0L) return@launch

            val item = messageAdapter.currentList.firstOrNull { it.messageId == draft.replyToMessageId }
                ?: chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = draft.replyToMessageId,
                    offsetBefore = 1,
                    offsetAfter = 1
                ).getOrNull()?.firstOrNull { it.id == draft.replyToMessageId }?.let(::toMessageItem)
            if (item != null) {
                isRestoringDraft = true
                setPendingReply(item)
                isRestoringDraft = false
            } else {
                chatDraftRepository.edit(chatId, draft.text, 0L)
                chatDraftRepository.flush(chatId)
            }
        }
    }

    // ─── Edit / Delete UX ─────────────────────────────────────────────────────

    private fun setPendingEdit(item: MessageItem) {
        // Edit и reply — взаимоисключающие режимы
        if (pendingReplyMessageId != 0L) {
            clearPendingReply(saveDraft = false)
        }

        pendingEditMessageId = item.messageId
        // Сохраняем существующие вложения — backend перезаписывает по files_ids,
        // forwarded-вложения он не трогает в любом случае
        pendingEditFileIds = item.attachments
            .filter { it.type != barkfluff.shared.Shared.MessageAttachmentType.FORWARDED_MESSAGE }
            .map { it.fileId }
            .filter { it.isNotBlank() }

        val preview = if (item.text.isNotBlank()) item.text else buildAttachmentSummary(item.attachments)
        binding.editPreviewContentText.text = preview
        binding.editPreviewBar.visibility = View.VISIBLE

        // Программная установка текста не должна запускать typing-heartbeat
        suppressTypingInput = true
        binding.messageEditText.setText(item.text)
        suppressTypingInput = false
        binding.messageEditText.setSelection(binding.messageEditText.text?.length ?: 0)
        binding.messageEditText.requestFocus()
        WindowInsetsControllerCompat(window, binding.chatRootLayout).show(WindowInsetsCompat.Type.ime())
        updateSendButtonMode()
    }

    private fun clearPendingEdit() {
        pendingEditMessageId = 0L
        pendingEditFileIds = emptyList()
        binding.editPreviewBar.visibility = View.GONE
        binding.messageEditText.text?.clear()
        updateSendButtonMode()
    }

    private fun confirmAndDelete(item: MessageItem) {
        com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle("Удалить сообщение?")
            .setMessage("Это действие нельзя отменить.")
            .setNegativeButton("Отмена", null)
            .setPositiveButton("Удалить") { _, _ ->
                lifecycleScope.launch {
                    val result = chatRepository.deleteMessage(item.messageId)
                    if (result.isSuccess) {
                        // Применяем сразу, не дожидаясь стрима
                        removeMessageById(item.messageId)
                    } else {
                        Toast.makeText(
                            this@ChatActivity,
                            "Ошибка удаления: ${result.exceptionOrNull()?.message}",
                            Toast.LENGTH_SHORT
                        ).show()
                    }
                }
            }
            .show()
    }

    private fun applyEditedMessage(msg: barkfluff.shared.Shared.Message) {
        val currentList = messageAdapter.currentList.toMutableList()
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
        messageAdapter.submitList(currentList)
    }

    private fun removeMessageById(messageId: Long) {
        val currentList = messageAdapter.currentList.toMutableList()
        val removed = currentList.removeAll {
            it.type == MessageType.MESSAGE && it.messageId == messageId
        }
        if (removed) {
            messageAdapter.submitList(currentList)
        }
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
            photos > 0 -> "📷 $photos фото"
            videos > 0 -> "🎬 $videos видео"
            audios > 0 -> "🎵 $audios аудио"
            docs > 0 -> "📎 $docs файл(ов)"
            stickers > 0 -> "Стикер"
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
            Toast.makeText(this, "Сообщение не загружено", Toast.LENGTH_SHORT).show()
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

    private fun showMessageActionMenu(anchor: View, item: MessageItem, rawX: Float, rawY: Float) {
        val inflater = layoutInflater
        val popupView = inflater.inflate(R.layout.popup_message_actions, null, false)

        val popup = android.widget.PopupWindow(
            popupView,
            android.view.ViewGroup.LayoutParams.WRAP_CONTENT,
            android.view.ViewGroup.LayoutParams.WRAP_CONTENT,
            false
        ).apply {
            isOutsideTouchable = true
            // focusable=false — чтобы открытие меню не сбрасывало фокус с поля ввода и не скрывало клавиатуру.
            // Закрытие по тапу вне меню обеспечивается isOutsideTouchable=true + ненулевым фоном ниже.
            isFocusable = false
            elevation = 12f * resources.displayMetrics.density
            // Прозрачный фон, чтобы скругления MaterialCardView были видны
            setBackgroundDrawable(android.graphics.drawable.ColorDrawable(android.graphics.Color.TRANSPARENT))
        }

        // Подсветка пузыря сообщения, для которого открыто меню
        val bubble = anchor.findViewById<View>(R.id.messageCard)
        val originalForeground = bubble?.foreground
        if (bubble != null) {
            val tv = android.util.TypedValue()
            theme.resolveAttribute(androidx.appcompat.R.attr.colorPrimary, tv, true)
            val highlightColor = (tv.data and 0x00FFFFFF) or (60 shl 24)
            bubble.foreground = android.graphics.drawable.ColorDrawable(highlightColor)
        }
        popup.setOnDismissListener { bubble?.foreground = originalForeground }

        val dismiss = { popup.dismiss() }

        val onClickWithDismiss: (Int) -> Unit = { actionId ->
            when (actionId) {
                R.id.actionReply -> setPendingReply(item)
                R.id.actionForward -> {
                    // Если сообщение само является пересланным — пересылаем оригинал, а не текущий snapshot
                    val sourceId = item.attachments
                        .firstOrNull { it.type == barkfluff.shared.Shared.MessageAttachmentType.FORWARDED_MESSAGE && it.hasForwardedMessage() }
                        ?.forwardedMessage
                        ?.originalMessageId
                        ?.takeIf { it > 0 }
                        ?: item.messageId
                    com.barkfluff.client.dialog.ForwardChatPickerBottomSheet
                        .newInstance(sourceId)
                        .show(supportFragmentManager, "forward_picker")
                }
                R.id.actionEdit -> setPendingEdit(item)
                R.id.actionDelete -> confirmAndDelete(item)
                R.id.actionPin -> togglePinForMessage(item)
            }
            dismiss()
        }

        // Edit и Delete — только для своих сообщений
        val isOwnMessage = item.senderId == currentUserId
        val editView = popupView.findViewById<View>(R.id.actionEdit)
        val deleteView = popupView.findViewById<View>(R.id.actionDelete)
        editView.visibility = if (isOwnMessage) View.VISIBLE else View.GONE
        deleteView.visibility = if (isOwnMessage) View.VISIBLE else View.GONE

        // Контекстные пункты по содержимому сообщения
        val imageAtts = item.attachments.filter {
            it.type == barkfluff.shared.Shared.MessageAttachmentType.IMAGE ||
            it.type == barkfluff.shared.Shared.MessageAttachmentType.GIF
        }
        val docAtts = item.attachments.filter {
            it.type == barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT
        }
        val hasText = item.text.isNotBlank()

        val copyTextView = popupView.findViewById<TextView>(R.id.actionCopyText)
        val copyImageView = popupView.findViewById<TextView>(R.id.actionCopyImage)
        val saveImagesView = popupView.findViewById<TextView>(R.id.actionSaveImages)
        val saveDocsView = popupView.findViewById<TextView>(R.id.actionSaveDocs)

        copyTextView.isVisible = hasText
        copyImageView.isVisible = imageAtts.size == 1
        saveImagesView.isVisible = imageAtts.isNotEmpty()
        saveDocsView.isVisible = docAtts.isNotEmpty()

        if (saveImagesView.isVisible) {
            saveImagesView.text = if (imageAtts.size == 1) "Сохранить изображение" else "Сохранить изображения"
        }

        copyTextView.setOnClickListener { copyMessageText(item); dismiss() }
        copyImageView.setOnClickListener { copyMessageImage(imageAtts.first()); dismiss() }
        saveImagesView.setOnClickListener { saveMessageImages(imageAtts); dismiss() }
        saveDocsView.setOnClickListener { saveMessageDocuments(docAtts); dismiss() }

        // Пункт «Закрепить» / «Открепить» — переключается по состоянию
        val pinView = popupView.findViewById<TextView>(R.id.actionPin)
        val isPinned = pinnedById.containsKey(item.messageId)
        pinView.text = if (isPinned) "Открепить" else "Закрепить"

        popupView.findViewById<View>(R.id.actionReply).setOnClickListener { onClickWithDismiss(R.id.actionReply) }
        editView.setOnClickListener { onClickWithDismiss(R.id.actionEdit) }
        deleteView.setOnClickListener { onClickWithDismiss(R.id.actionDelete) }
        popupView.findViewById<View>(R.id.actionForward).setOnClickListener { onClickWithDismiss(R.id.actionForward) }
        pinView.setOnClickListener { onClickWithDismiss(R.id.actionPin) }

        // Измеряем popup чтобы аккуратно расположить относительно точки касания и краёв экрана
        popupView.measure(
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
        )
        val popupW = popupView.measuredWidth
        val popupH = popupView.measuredHeight

        val dm = resources.displayMetrics
        val margin = (8 * dm.density).toInt()

        // X: открываем правее точки касания, если не помещается — левее
        val proposedX = rawX.toInt() + margin
        val x = if (proposedX + popupW + margin > dm.widthPixels) {
            (rawX.toInt() - popupW - margin).coerceAtLeast(margin)
        } else {
            proposedX
        }

        // Y: ниже точки касания, если не помещается — выше
        val proposedY = rawY.toInt() + margin
        val y = if (proposedY + popupH + margin > dm.heightPixels) {
            (rawY.toInt() - popupH - margin).coerceAtLeast(margin)
        } else {
            proposedY
        }

        popup.showAtLocation(anchor, android.view.Gravity.NO_GRAVITY or android.view.Gravity.START or android.view.Gravity.TOP, x, y)
    }

    private fun copyMessageText(item: MessageItem) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText("BarkFluff message", item.text))
        Toast.makeText(this, "Текст скопирован", Toast.LENGTH_SHORT).show()
    }

    private fun copyMessageImage(att: barkfluff.shared.Shared.MessageAttachment) {
        lifecycleScope.launch {
            val srcFile = FileCache.getFile(att.fileId) ?: chatRepository.downloadFile(att.fileId)
            if (srcFile == null) {
                Toast.makeText(this@ChatActivity, "Не удалось загрузить изображение", Toast.LENGTH_SHORT).show()
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
                Toast.makeText(this@ChatActivity, "Изображение скопировано", Toast.LENGTH_SHORT).show()
            } catch (e: Exception) {
                Toast.makeText(this@ChatActivity, "Не удалось скопировать", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun saveMessageImages(images: List<barkfluff.shared.Shared.MessageAttachment>) {
        if (images.isEmpty()) return
        Toast.makeText(this, "Сохраняю...", Toast.LENGTH_SHORT).show()
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
                if (saved > 0) "Сохранено в галерею: $saved" else "Не удалось сохранить",
                Toast.LENGTH_SHORT
            ).show()
        }
    }

    private fun saveMessageDocuments(docs: List<barkfluff.shared.Shared.MessageAttachment>) {
        if (docs.isEmpty()) return
        Toast.makeText(this, "Сохраняю...", Toast.LENGTH_SHORT).show()
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
                if (saved > 0) "Сохранено в загрузки: $saved" else "Не удалось сохранить",
                Toast.LENGTH_SHORT
            ).show()
        }
    }

    private fun loadCachedMessages() {
        val scope = cacheScope ?: return
        lifecycleScope.launch {
            val messages = runCatching {
                chatCacheRepository.latestMessages(scope, chatId, limit = 30)
            }.getOrNull().orEmpty()
            if (messages.isEmpty()) return@launch

            displayMessages(messages)
            val sortedMessages = messages.sortedBy { it.sentAt.seconds }
            firstVisibleMessageId = sortedMessages.first().id
            lastVisibleMessageId = sortedMessages.last().id
            hasMoreMessagesUp = messages.size >= 30
            hasMoreMessagesDown = false
            binding.loadingProgress.visibility = View.GONE
        }
    }
    private fun loadChatInfoAndMessages() {
        loadCachedMessages()
        isLoadingMessages = true
        binding.loadingProgress.visibility = View.VISIBLE

        lifecycleScope.launch {
            try {
                // Сначала получаем информацию о чате (включая firstUnreadMessageId)
                val chatInfoResult = chatRepository.getChatInfo(chatId)
                if (chatInfoResult.isSuccess) {
                    val chatInfo = chatInfoResult.getOrNull()!!
                    if (chatInfo.title.isNotBlank()) {
                        chatTitle = chatInfo.title
                        binding.chatNameTextView.text = chatInfo.title
                    }
                    if (chatInfo.pictureFileId.isNotBlank()) {
                        chatAvatarFileId = chatInfo.pictureFileId
                    }
                    isGroupChat = chatInfo.isGroupChat
                    firstUnreadMessageId = chatInfo.firstUnreadMessageId

                    // Синхронизируем состояние mute (для меню и локального guard уведомлений)
                    isChatMuted = chatInfo.muted
                    GlobalParam(this@ChatActivity).setChatMutedLocal(chatId, chatInfo.muted)

                    // Определяем otherUserId из участников чата (если не был передан через intent)
                    if (!isGroupChat && otherUserId == 0L && chatInfo.memberIds.isNotEmpty()) {
                        otherUserId = chatInfo.memberIds.firstOrNull { it != currentUserId } ?: 0L
                        if (otherUserId > 0) {
                            realtimeService.changeOnlineSubscription(listOf(otherUserId))
                            loadOnlineStatus(otherUserId)
                        }
                    }
                    loadChatAvatar()
                    if (!isGroupChat && otherUserId > 0) {
                        binding.onlineStatusTextView.visibility = View.VISIBLE
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading chat info", e)
            }

            // Загружаем сообщения (вокруг первого непрочитанного, если есть)
            loadMessages()
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
        val newMuted = !isChatMuted
        lifecycleScope.launch {
            val result = grpcManager.setChatMuted(chatId, newMuted)
            if (result.isSuccess) {
                isChatMuted = newMuted
                GlobalParam(this@ChatActivity).setChatMutedLocal(chatId, newMuted)
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
                            applyOnlineStatus("в сети", true)
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
                            applyOnlineStatus("в сети", true)
                        } else {
                            val lastSeen = OnlineTimeFormatter.formatLastSeen(this@ChatActivity, userStatus.lastSeen.seconds * 1000)
                            applyOnlineStatus(lastSeen, false)
                        }
                    } else {
                        applyOnlineStatus("был(а) недавно", false)
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

    private fun loadMessages(isRetry: Boolean = false) {
        loadMessagesJob = lifecycleScope.launch {
            try {
                val result = if (firstUnreadMessageId > 0) {
                    // Загружаем сообщения вокруг первого непрочитанного
                    chatRepository.loadMessages(
                        chatId = chatId,
                        fromMessageId = firstUnreadMessageId,
                        offsetBefore = 15,
                        offsetAfter = 30
                    )
                } else {
                    // Нет непрочитанных — загружаем последние сообщения
                    chatRepository.loadMessages(
                        chatId = chatId,
                        fromMessageId = 0L,
                        offsetBefore = 0,
                        offsetAfter = 0,
                        count = 30
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
                    hasMoreMessagesDown = true // Попробуем подгрузить, остановимся если нет новых

                    // Отмечаем сообщения как прочитанные
                    markVisibleMessagesAsRead(messages)
                } else {
                    if (!isRetry) {
                        // Первая попытка не удалась — каналы могли протухнуть после фона, retry
                        Log.w(TAG, "Message load failed, retrying after channel refresh...")
                        delay(300)
                        loadMessages(isRetry = true)
                        return@launch
                    }
                    Toast.makeText(
                        this@ChatActivity,
                        "Ошибка загрузки сообщений",
                        Toast.LENGTH_SHORT
                    ).show()
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
                isLoadingMessages = false
                binding.loadingProgress.visibility = View.GONE
            }
        }
    }

    /**
     * Отмечает непрочитанные сообщения от других пользователей как прочитанные.
     */
    private fun markVisibleMessagesAsRead(messages: List<barkfluff.shared.Shared.Message>) {
        val unreadMessageIds = messages
            .filter { it.senderId != currentUserId && !it.readByList.contains(currentUserId) }
            .map { it.id }

        if (unreadMessageIds.isNotEmpty()) {
            lifecycleScope.launch {
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
     * Помечает прочитанными все чужие сообщения, уже загруженные в адаптер. Вызывается
     * при достижении самого низа списка — сервер идемпотентен, повторная пометка безопасна.
     */
    private fun markAllLoadedMessagesAsRead() {
        val unreadMessageIds = messageAdapter.currentList
            .filter { it.type == MessageType.MESSAGE && it.senderId != currentUserId }
            .map { it.messageId }

        if (unreadMessageIds.isNotEmpty()) {
            lifecycleScope.launch {
                try {
                    chatRepository.markAsRead(unreadMessageIds)
                    Log.d(TAG, "Marked all ${unreadMessageIds.size} loaded messages as read (reached bottom)")
                } catch (e: Exception) {
                    Log.e(TAG, "Error marking all messages as read on scroll to bottom", e)
                }
            }
        }
    }

    private fun loadMessagesUp() {
        if (isLoadingMessages || !hasMoreMessagesUp) return

        isLoadingMessages = true
        Log.d(TAG, "Loading messages up from $firstVisibleMessageId")

        loadMessagesJob = lifecycleScope.launch {
            try {
                cacheScope?.let { scope ->
                    val cached = chatCacheRepository.messagesBefore(
                        scope,
                        chatId,
                        firstVisibleMessageId,
                        limit = 30
                    )
                    if (cached.isNotEmpty()) {
                        prependMessages(cached)
                        firstVisibleMessageId = cached.minBy { it.sentAt.seconds }.id
                        hasMoreMessagesUp = cached.size >= 30
                        return@launch
                    }
                }
                val result = chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = firstVisibleMessageId,
                    offsetBefore = 30,
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
                        hasMoreMessagesUp = messages.size >= 30
                    } else {
                        hasMoreMessagesUp = false
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading messages up", e)
            } finally {
                isLoadingMessages = false
            }
        }
    }

    private fun loadMessagesDown() {
        if (isLoadingMessages || !hasMoreMessagesDown) return

        isLoadingMessages = true
        Log.d(TAG, "Loading messages down from $lastVisibleMessageId")

        loadMessagesJob = lifecycleScope.launch {
            try {
                val result = chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = lastVisibleMessageId,
                    offsetBefore = 0,
                    offsetAfter = 30
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
                        hasMoreMessagesDown = messages.size >= 30
                        markVisibleMessagesAsRead(messages)
                    } else {
                        hasMoreMessagesDown = false
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading messages down", e)
            } finally {
                isLoadingMessages = false
            }
        }
    }

    private fun displayMessages(messages: List<barkfluff.shared.Shared.Message>) {
        val sortedMessages = messages.sortedBy { it.sentAt.seconds }
        val messageItems = messagesWithDateSeparators(sortedMessages).toMutableList()

        // Вставляем разделитель непрочитанных сообщений
        var scrollToPosition = messageItems.size - 1
        if (firstUnreadMessageId > 0) {
            val unreadIndex = messageItems.indexOfFirst {
                it.type == MessageType.MESSAGE && it.messageId == firstUnreadMessageId
            }
            if (unreadIndex >= 0) {
                messageItems.add(unreadIndex, MessageItem.createUnreadSeparator())
                scrollToPosition = unreadIndex // Скроллим к разделителю
            }
        }

        messageAdapter.submitList(messageItems) {
            if (scrollToPosition >= 0) {
                // Используем scrollToPositionWithOffset чтобы разделитель непрочитанных
                // появился в верхней части экрана, а не где-то в видимой области
                val layoutManager = binding.messagesRecyclerView.layoutManager as LinearLayoutManager
                layoutManager.scrollToPositionWithOffset(scrollToPosition, 0)
            }
            updateScrollToBottomButton()
        }
    }

    /**
     * Добавляет разделители дат между сообщениями.
     */
    private fun messagesWithDateSeparators(messages: List<barkfluff.shared.Shared.Message>): List<MessageItem> {
        val result = mutableListOf<MessageItem>()
        var lastDate: Long = -1

        for (msg in messages) {
            val msgDate = startOfDay(msg.sentAt.seconds * 1000)

            // Если дата изменилась — добавляем разделитель
            if (msgDate != lastDate) {
                result.add(MessageItem.createDateSeparator(formatDateSeparator(msgDate)))
                lastDate = msgDate
            }

            result.add(toMessageItem(msg))
        }

        return result
    }

    private fun toMessageItem(msg: barkfluff.shared.Shared.Message): MessageItem {
        val isSystem = msg.type == barkfluff.shared.Shared.MessageContentType.SYSTEM
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
            isEdited = msg.isEdited
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
            messageDate == today -> "Сегодня"
            messageDate == yesterday -> "Вчера"
            else -> SimpleDateFormat("dd MMMM yyyy", Locale("ru")).format(Date(timestampMillis))
        }
    }

    private fun prependMessages(messages: List<barkfluff.shared.Shared.Message>) {
        val currentList = messageAdapter.currentList
            .filter { it.type != MessageType.FOOTER }
            .toMutableList()

        // Фильтруем дубликаты
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

        // Удаляем первый разделитель если он есть (будет заменен)
        if (currentList.isNotEmpty() && currentList.first().type == MessageType.DATE_SEPARATOR) {
            currentList.removeAt(0)
        }

        val newItems = messagesWithDateSeparators(sortedMessages)

        // Вставляем новые элементы в начало
        currentList.addAll(0, newItems)
        messageAdapter.submitList(currentList)
    }

    private fun appendMessages(messages: List<barkfluff.shared.Shared.Message>) {
        val currentList = messageAdapter.currentList
            .filter { it.type != MessageType.FOOTER }
            .toMutableList()

        // Фильтруем дубликаты
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

        // Определяем дату последнего сообщения в текущем списке
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

        messageAdapter.submitList(currentList)
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

    private fun subscribeToRealtimeEvents() {
        lifecycleScope.launch {
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

        // Подписка на прогресс аплоада медиа — обновляем uploadProgress и статус
        // оптимистичных сообщений в реальном времени.
        lifecycleScope.launch {
            com.barkfluff.client.send.MediaSendService.uploadEvents.collect { event ->
                if (event.chatId != chatId) return@collect
                when (event.state) {
                    com.barkfluff.client.send.UploadState.PREPARING -> {
                        updateOptimisticUploadProgress(event.localId, event.progress)
                    }
                    com.barkfluff.client.send.UploadState.UPLOADING -> {
                        updateOptimisticUploadProgress(event.localId, event.progress)
                    }
                    com.barkfluff.client.send.UploadState.SENDING -> {
                        updateOptimisticUploadProgress(event.localId, 100)
                    }
                    com.barkfluff.client.send.UploadState.SENT -> {
                        // Снимаем прогресс — финальный item придёт через realtime newMessages
                        // (либо оптимистичный заменится по messageId, либо останется как SENT).
                        clearOptimisticUploadProgress(event.localId, event.serverMessageId)
                    }
                    com.barkfluff.client.send.UploadState.FAILED -> {
                        updateOptimisticStatus(event.localId, ReadStatus.FAILED)
                        clearOptimisticUploadProgress(event.localId, 0L)
                    }
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.messagesRead.collect { event ->
                if (event.chatId == chatId) {
                    updateMessageReadStatus(event.messageId, event.newReadByList)
                    cacheScope?.let { scope ->
                        chatCacheRepository.updateReadBy(scope, chatId, event.messageId, event.newReadByList)
                    }
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.messageEdited.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    applyEditedMessage(event.message)
                    cacheScope?.let { scope ->
                        chatCacheRepository.saveMessages(scope, chatId, listOf(event.message))
                    }
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.messageDeleted.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    removeMessageById(event.messageId)
                    cacheScope?.let { scope ->
                        chatCacheRepository.deleteMessage(scope, chatId, event.messageId)
                    }
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.messagePinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onMessagePinnedRemote(event.messageId, event.pinnerUserId, event.pinnedAt.seconds * 1000)
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.messageUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onMessageUnpinnedRemote(event.messageId)
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.allMessagesUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    onAllMessagesUnpinnedRemote()
                }
            }
        }

        // Подписка на онлайн-статусы обрабатывается в loadOnlineStatus()

        // Индикатор "печатает..."
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

    // ═══════════════════════════════════════════════════════════════
    // Закреплённые сообщения
    // ═══════════════════════════════════════════════════════════════

    private fun setupPinnedBar() {
        binding.pinnedMessageBar.setOnClickListener {
            val first = pinnedSorted.firstOrNull() ?: return@setOnClickListener
            scrollToMessageId(first.message.id)
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

    private fun loadPinnedMessages() {
        lifecycleScope.launch {
            val result = grpcManager.listPinnedMessages(chatId)
            if (result.isSuccess) {
                val (list, total) = result.getOrNull() ?: (emptyList<barkfluff.shared.Shared.PinnedMessageInfo>() to 0)
                pinnedById.clear()
                pinnedSorted.clear()
                pinnedById.putAll(list.associateBy { it.message.id })
                pinnedSorted.addAll(list)
                pinnedTotalCount = total
                updatePinnedBar()
            }
        }
    }

    private fun onMessagePinnedRemote(messageId: Long, pinnerUserId: Long, pinnedAtMs: Long) {
        if (pinnedById.containsKey(messageId)) return
        // Чтобы получить полный Message — перезагружаем последнюю страницу закрепов.
        lifecycleScope.launch {
            val result = grpcManager.listPinnedMessages(chatId)
            if (result.isSuccess) {
                val (list, total) = result.getOrNull() ?: return@launch
                pinnedById.clear()
                pinnedSorted.clear()
                pinnedById.putAll(list.associateBy { it.message.id })
                pinnedSorted.addAll(list)
                pinnedTotalCount = total
                updatePinnedBar()
            }
        }
    }

    private fun onMessageUnpinnedRemote(messageId: Long) {
        val removed = pinnedById.remove(messageId) ?: return
        pinnedSorted.remove(removed)
        pinnedTotalCount = (pinnedTotalCount - 1).coerceAtLeast(0)
        updatePinnedBar()
    }

    private fun onAllMessagesUnpinnedRemote() {
        pinnedById.clear()
        pinnedSorted.clear()
        pinnedTotalCount = 0
        updatePinnedBar()
    }

    private fun updatePinnedBar() {
        val first = pinnedSorted.firstOrNull()
        if (first == null) {
            binding.pinnedMessageBar.visibility = View.GONE
            return
        }
        binding.pinnedMessageBar.visibility = View.VISIBLE
        binding.pinnedTitle.text = if (pinnedTotalCount > 1) {
            "Закреплённое сообщение · ${pinnedTotalCount}"
        } else {
            "Закреплённое сообщение"
        }
        val text = first.message.content?.text ?: ""
        binding.pinnedPreview.text = if (text.isBlank()) "[вложение]" else text
    }

    private fun scrollToMessageId(messageId: Long) {
        val list = messageAdapter.currentList
        val idx = list.indexOfFirst { it.type == MessageType.MESSAGE && it.messageId == messageId }
        if (idx >= 0) {
            binding.messagesRecyclerView.smoothScrollToPosition(idx)
        } else {
            // Сообщение не загружено — подгружаем окно вокруг него.
            lifecycleScope.launch {
                val result = chatRepository.loadMessages(chatId, fromMessageId = messageId, offsetBefore = 20, offsetAfter = 20)
                if (result.isSuccess) {
                    rebuildMessagesFromList(result.getOrNull() ?: emptyList())
                    val newIdx = messageAdapter.currentList.indexOfFirst { it.type == MessageType.MESSAGE && it.messageId == messageId }
                    if (newIdx >= 0) binding.messagesRecyclerView.scrollToPosition(newIdx)
                }
            }
        }
    }

    private fun togglePinForMessage(item: MessageItem) {
        val isPinned = pinnedById.containsKey(item.messageId)
        lifecycleScope.launch {
            if (isPinned) {
                val result = grpcManager.unpinMessage(chatId, item.messageId)
                if (result.isFailure) {
                    Toast.makeText(this@ChatActivity, "Не удалось открепить сообщение", Toast.LENGTH_SHORT).show()
                }
            } else {
                val result = grpcManager.pinMessage(chatId, item.messageId)
                if (result.isFailure) {
                    val cause = result.exceptionOrNull()
                    val msg = if (cause is GrpcManager.PinErrorException && cause.isTooManyPinned) {
                        "Достигнут лимит закреплённых сообщений (100). Открепите старые, чтобы закрепить новое."
                    } else {
                        "Не удалось закрепить сообщение"
                    }
                    Toast.makeText(this@ChatActivity, msg, Toast.LENGTH_LONG).show()
                } else {
                    // Оптимистичное обновление до прихода realtime-события
                    val pinned = result.getOrNull() ?: return@launch
                    if (!pinnedById.containsKey(pinned.message.id)) {
                        pinnedById[pinned.message.id] = pinned
                        pinnedSorted.add(0, pinned)
                        pinnedTotalCount++
                        updatePinnedBar()
                    }
                }
            }
        }
    }

    private fun rebuildMessagesFromList(messages: List<barkfluff.shared.Shared.Message>) {
        messageAdapter.submitList(messages.map { toMessageItem(it) })
    }

    private fun addNewMessage(msg: barkfluff.shared.Shared.Message) {
        val currentList = messageAdapter.currentList
            .filter { it.type != MessageType.FOOTER }
            .toMutableList()

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
                messageAdapter.submitList(currentList)
                return
            }
        }

        // Проверка дубликата
        if (currentList.any { (it.type == MessageType.MESSAGE || it.type == MessageType.SYSTEM) && it.messageId == msg.id }) {
            return
        }

        val messageItem = toMessageItem(msg)

        // Убираем разделитель непрочитанных если он ещё есть
        currentList.removeAll { it.type == MessageType.UNREAD_SEPARATOR }

        // Проверяем, нужно ли добавить разделитель даты
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

        val isOwnMessage = msg.senderId == currentUserId
        val wasAtBottom = isRecyclerViewAtBottom()

        currentList.add(messageItem)
        messageAdapter.submitList(currentList) {
            // Своё сообщение — всегда скроллим вниз
            // Чужое сообщение — скроллим только если уже были внизу
            // size-1 = footer, size-2 = последнее сообщение
            if (isOwnMessage || wasAtBottom) {
                val lastIdx = messageAdapter.itemCount - 1
                if (lastIdx >= 0) binding.messagesRecyclerView.scrollToPosition(lastIdx)
            }
            updateScrollToBottomButton()
        }

        lastVisibleMessageId = msg.id

        // Если сообщение от другого пользователя — сразу отмечаем как прочитанное
        if (msg.senderId != currentUserId) {
            lifecycleScope.launch {
                try {
                    chatRepository.markAsRead(listOf(msg.id))
                } catch (e: Exception) {
                    Log.e(TAG, "Error marking new message as read", e)
                }
            }
        }
    }

    private fun updateMessageReadStatus(messageId: Long, newReadBy: List<Long>) {
        val currentList = messageAdapter.currentList
            .filter { it.type != MessageType.FOOTER }
            .toMutableList()
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
        messageAdapter.submitList(currentList)
    }


    override fun onStart() {
        super.onStart()
        // При возврате из фона — подгружаем сообщения, пришедшие пока приложение было свёрнуто.
        // Ждём пока RealtimeService переподключится (каналы пересоздаются в ProcessLifecycleOwner.onStart).
        if (lastVisibleMessageId > 0L && !isLoadingMessages) {
            lifecycleScope.launch {
                // Сначала проверяем и обновляем токен при необходимости
                val tokenValid = grpcManager.ensureTokenValid(this@ChatActivity)
                if (!tokenValid) {
                    Log.w(TAG, "onStart: Token refresh failed, finishing activity")
                    finish()
                    return@launch
                }

                waitForConnection()
                if (lastVisibleMessageId > 0L && !isLoadingMessages) {
                    Log.d(TAG, "onStart: loading missed messages from lastVisibleMessageId=$lastVisibleMessageId")
                    hasMoreMessagesDown = true
                    loadMessagesDown()

                    // Догоняем edit/delete события, пропущенные пока стримы были оффлайн
                    syncRecentMessages()
                }
            }
        }
    }

    /**
     * Подтягивает свежее состояние уже загруженных сообщений и применяет diff:
     *  — сообщения, которых больше нет на сервере, удаляются из адаптера (другое устройство удалило);
     *  — сообщения с обновлённым текстом/вложениями/isEdited обновляются.
     * Нужен после возвращения из фона: события стримов, эмитнутые во время паузы, теряются.
     */
    private suspend fun syncRecentMessages() {
        val visibleMessages = messageAdapter.currentList
            .filter { it.type == MessageType.MESSAGE }
        if (visibleMessages.isEmpty()) return

        val earliestId = visibleMessages.minOf { it.messageId }
        val latestId = visibleMessages.maxOf { it.messageId }

        // Запрашиваем окно от самого раннего видимого сообщения и до конца
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

        // Граница "проверенного" диапазона: если сервер вернул максимум (50) — за пределы заглядывать не можем.
        // Если меньше — значит больше сообщений в этом окне нет, можно безопасно удалять отсутствующие.
        val checkedUpperBound = if (serverMessages.size < 50) Long.MAX_VALUE else serverMessages.maxOfOrNull { it.id } ?: earliestId

        // Удаляем локальные сообщения, которых нет на сервере, но только в проверенном диапазоне
        val deletedIds = visibleMessages
            .filter { it.messageId in earliestId..checkedUpperBound && it.messageId !in serverIds }
            .map { it.messageId }
        for (id in deletedIds) {
            Log.d(TAG, "syncRecentMessages: removing locally known but server-missing messageId=$id")
            removeMessageById(id)
        }

        // Обновляем сообщения, у которых на сервере другой текст/isEdited/состав вложений
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

    /**
     * Ждёт пока RealtimeService переподключится (CONNECTED) или таймаут 5 секунд.
     */
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

    override fun onResume() {
        super.onResume()
        setupChatBackground()
        refreshChatBackgroundSettings()
        // Обновляем список, чтобы отразить изменения кэша (например, после удаления видео из кэша)
        if (::messageAdapter.isInitialized) {
            messageAdapter.messageCornerRadiusDp = globalParam.chatMessageCornerRadius
            messageAdapter.stickerSizeDp = globalParam.chatStickerSizeDp
            messageAdapter.notifyDataSetChanged()
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
        super.onStop()
    }

    override fun onDestroy() {
        super.onDestroy()
        // Сбрасываем открытый чат
        OpenChatManager.closeChat()
        chatRepository.close()
        loadMessagesJob?.cancel()
        onlineStatusJob?.cancel()
        onlineStatusSubscription?.cancel()
        stopTypingHeartbeat(sendCancel = true)
        realtimeService.changeTypingSubscription(emptyList())
    }
}
