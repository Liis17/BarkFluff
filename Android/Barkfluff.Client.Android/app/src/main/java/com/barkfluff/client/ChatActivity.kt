package com.barkfluff.client

import android.animation.ValueAnimator
import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.view.animation.DecelerateInterpolator
import android.widget.TextView
import android.widget.Toast
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
import com.barkfluff.client.utils.ImageCompressor
import com.barkfluff.client.utils.KeyboardHeightTracker
import com.barkfluff.client.utils.StickerCache
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.MessageItemAnimator
import com.barkfluff.client.utils.MessageTimeSpacingDecoration
import com.yalantis.ucrop.UCrop
import androidx.activity.OnBackPressedCallback
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.recyclerview.widget.GridLayoutManager
import kotlinx.coroutines.async
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
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

    private var chatId: String = ""
    private var chatTitle: String = ""
    private var chatAvatarFileId: String? = null
    private var isGroupChat: Boolean = false
    private var otherUserId: Long = 0L
    private var currentUserId: Long = 0L

    // Пагинация сообщений
    private var isLoadingMessages = false
    private var hasMoreMessagesUp = true
    private var hasMoreMessagesDown = true
    private var firstVisibleMessageId: Long = 0L
    private var lastVisibleMessageId: Long = 0L
    private var loadMessagesJob: Job? = null

    // Непрочитанные сообщения
    private var firstUnreadMessageId: Long = 0L

    // Вставленные из буфера обмена изображения
    private val pendingPastedImages = mutableListOf<Uri>()
    private val pendingStickerUris = mutableListOf<Uri>()
    private val pendingDocumentUris = mutableListOf<Uri>()
    private val pendingCropQueue = ArrayDeque<Uri>()

    // Inline стикер-панель
    private enum class InputPanelState { NONE, KEYBOARD, STICKER_PANEL }
    private var inputPanelState = InputPanelState.NONE
    private var lastKnownKeyboardHeight = 0
    private var isTransitioningToStickers = false
    private var stickerDataLoaded = false
    private lateinit var stickerPanelAdapter: StickerPanelAdapter

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

    companion object {
        private const val TAG = "ChatActivity"
        private const val EXTRA_CHAT_ID = "chat_id"
        private const val EXTRA_CHAT_TITLE = "chat_title"
        private const val EXTRA_CHAT_AVATAR_FILE_ID = "chat_avatar_file_id"
        private const val EXTRA_IS_GROUP_CHAT = "is_group_chat"
        private const val EXTRA_OTHER_USER_ID = "other_user_id"
        private const val LOAD_MESSAGES_DELAY_MS = 500L
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

        Log.d(TAG, "ChatActivity created: chatId=$chatId, title=$chatTitle, isGroupChat=$isGroupChat, otherUserId=$otherUserId")

        setupToolbar()
        setupMessagesRecyclerView()
        setupMessageInput()
        setupStickerPanel()
        setupKeyboardTracking()
        setupChatBackground()
        loadChatInfoAndMessages()

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
        binding.toolbar.setNavigationOnClickListener {
            finish()
        }

        // Клик на контейнер с информацией о чате (аватар + имя) — открывает профиль
        binding.chatInfoContainer.setOnClickListener {
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
    private fun setupChatBackground() {
        val fileId = globalParam.chatBackgroundFileId
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
                applyBackgroundFromFile(cachedFile, applyBlur)
                return@launch
            }
            // Иначе скачиваем через Files API
            val url = withContext(Dispatchers.IO) {
                chatRepository.getFileDownloadUrl(fileId).getOrNull()
            } ?: return@launch

            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
                // API 31+: загружаем через Coil, blur через RenderEffect
                binding.chatBackgroundImage.load(url, AvatarLoader.getImageLoader(this@ChatActivity)) {
                    crossfade(true)
                    listener(onSuccess = { _, _ ->
                        applyRenderEffectBlur(applyBlur)
                    })
                }
            } else {
                // API 26–30: загружаем Bitmap, blur через ScriptIntrinsicBlur
                val bitmap = withContext(Dispatchers.IO) {
                    loadBitmapFromUrl(url)
                }
                if (bitmap != null) {
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

    private fun applyBackgroundFromFile(file: java.io.File, applyBlur: Boolean) {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
            binding.chatBackgroundImage.load(file, AvatarLoader.getImageLoader(this)) {
                crossfade(false)
                listener(onSuccess = { _, _ ->
                    applyRenderEffectBlur(applyBlur)
                })
            }
        } else {
            lifecycleScope.launch {
                val bitmap = withContext(Dispatchers.IO) {
                    android.graphics.BitmapFactory.decodeFile(file.absolutePath)
                } ?: return@launch
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
            messageCornerRadiusDp = globalParam.chatMessageCornerRadius
        )

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
                }
            })
        }

        // Обработчик клика на кнопку прокрутки вниз
        binding.scrollToBottomButton.setOnClickListener {
            scrollToLatestMessages()
        }
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
        binding.sendButton.setOnClickListener {
            when {
                pendingDocumentUris.isNotEmpty() || pendingPastedImages.isNotEmpty() || pendingStickerUris.isNotEmpty() ->
                    sendMessageWithPendingAttachments()
                else ->
                    sendMessage()
            }
        }

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
                        Log.d(TAG, "onReceiveContent: uri=$uri, resolverMime=$resolverMime, path=${uri.path}")
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

        // Back press закрывает стикер-панель или оверлей предпросмотра
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                if (binding.stickerPreviewOverlay.visibility == View.VISIBLE) {
                    hideStickerPreview()
                } else if (inputPanelState == InputPanelState.STICKER_PANEL) {
                    hideStickerPanel()
                } else {
                    isEnabled = false
                    onBackPressedDispatcher.onBackPressed()
                    isEnabled = true
                }
            }
        })
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

        if (!stickerDataLoaded) {
            loadStickerPanelData()
        }
    }

    private fun hideStickerPanel() {
        binding.stickerPanelContainer.visibility = View.GONE
        inputPanelState = InputPanelState.NONE
    }

    private fun showStickerPreview(sticker: barkfluff.files.FilesApiOuterClass.StickerInfo) {
        val fileId = sticker.fileId
        if (fileId.isBlank()) return
        binding.stickerPreviewOverlay.visibility = View.VISIBLE
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
        Log.d(TAG, "handleSelectedImages: uris.size=${result.uris.size}, uris=${result.uris}, sendAsFile=${result.sendAsFile}, sendSeparately=${result.sendSeparately}, caption=${result.captionText}, isDocuments=${result.isDocuments}, fromCamera=${result.fromCamera}")

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

        // Обычный выбор фото из галереи — отправляем сразу
        val sendAsFile = result.sendAsFile
        val sendSeparately = result.sendSeparately
        val captionText = result.captionText

        lifecycleScope.launch {
            val selectedUris = uris.take(ImagePickerBottomSheet.MAX_SELECTION)

            if (selectedUris.size > 1) {
                Toast.makeText(
                    this@ChatActivity,
                    "Выбрано изображений: ${selectedUris.size}",
                    Toast.LENGTH_SHORT
                ).show()
            }

            // Загружаем каждое изображение/видео
            val fileIds = mutableListOf<String>()
            for ((index, uri) in selectedUris.withIndex()) {
                try {
                    val mimeType = contentResolver.getType(uri)
                    val isWebp = mimeType == "image/webp"
                    val isVideo = mimeType?.startsWith("video/") == true

                    // Определяем тип файла для загрузки
                    val uploadFileType = when {
                        sendAsFile -> barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
                        isVideo -> barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_VIDEO
                        isWebp -> barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_STICKER
                        else -> barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                    }

                    val bytes = if (sendAsFile || isWebp || isVideo) {
                        // Без сжатия — читаем оригинальный файл
                        readBytesFromUri(uri)
                    } else {
                        // Со сжатием
                        ImageCompressor.compressImage(uri, this@ChatActivity).getOrNull()
                    }

                    if (bytes == null) continue

                    // Для видео и DOCUMENT передаём оригинальные имя/MIME
                    val (passName, passMime) = if (sendAsFile || isVideo) {
                        getDocumentInfo(uri)
                    } else {
                        null to null
                    }

                    // Загрузка на сервер
                    val uploadResult = chatRepository.uploadFile(
                        bytes,
                        uploadFileType,
                        fileName = passName,
                        mimeType = passMime
                    )

                    if (uploadResult.isSuccess) {
                        val fileId = uploadResult.getOrNull()!!
                        fileIds.add(fileId)
                        Log.d(TAG, "Media ${index + 1}/${selectedUris.size} uploaded: $fileId, fileIds.size=${fileIds.size}")
                    } else {
                        Log.e(TAG, "Media ${index + 1}/${selectedUris.size} upload failed: ${uploadResult.exceptionOrNull()?.message}")
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error processing media ${index + 1}/${selectedUris.size}", e)
                }
            }

            Log.d(TAG, "After upload loop: fileIds.size=${fileIds.size}, fileIds=$fileIds")

            if (fileIds.isNotEmpty()) {
                Log.d(TAG, "Sending ${fileIds.size} fileIds: $fileIds, sendSeparately=$sendSeparately")
                if (sendSeparately) {
                    // Отправляем каждое изображение отдельным сообщением
                    // Первое сообщение получает подпись, остальные без текста
                    for ((index, fileId) in fileIds.withIndex()) {
                        val text = if (index == 0) captionText else ""
                        sendMessage(text = text, fileIds = listOf(fileId))
                    }
                } else {
                    // Отправляем все изображения в одном сообщении с подписью
                    sendMessage(text = captionText, fileIds = fileIds)
                }
            } else {
                Log.w(TAG, "No fileIds to send! selectedUris.size=${selectedUris.size}")

                Toast.makeText(
                    this@ChatActivity,
                    "Не удалось загрузить изображения",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
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
            Log.e(TAG, "Error querying document info for $uri", e)
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
        binding.messageEditText.text?.clear()

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
        if (messageText.isBlank() && fileIds.isEmpty()) return

        Log.d(TAG, "sendMessage: text='$messageText', fileIds=$fileIds")

        lifecycleScope.launch {
            try {
                val result = chatRepository.sendMessage(
                    chatId = chatId,
                    text = messageText,
                    fileIds = fileIds
                )

                if (result.isSuccess) {
                    binding.messageEditText.text?.clear()
                    // Не закрываем клавиатуру и не забираем фокус — пользователь может продолжать печатать
                } else {
                    Toast.makeText(
                        this@ChatActivity,
                        "Ошибка отправки: ${result.exceptionOrNull()?.message}",
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error sending message", e)
                Toast.makeText(
                    this@ChatActivity,
                    "Ошибка отправки: ${e.message}",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    private fun loadChatInfoAndMessages() {
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

    private var onlineStatusJob: Job? = null
    private var onlineStatusSubscription: Job? = null

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
                            binding.onlineStatusTextView.text = "в сети"
                            binding.onlineIndicator.visibility = View.VISIBLE
                        } else {
                            val lastSeen = formatLastSeen(status.lastSeen.seconds * 1000)
                            binding.onlineStatusTextView.text = lastSeen
                            binding.onlineIndicator.visibility = View.GONE
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
                            binding.onlineStatusTextView.text = "в сети"
                            binding.onlineIndicator.visibility = View.VISIBLE
                        } else {
                            val lastSeen = formatLastSeen(userStatus.lastSeen.seconds * 1000)
                            binding.onlineStatusTextView.text = lastSeen
                            binding.onlineIndicator.visibility = View.GONE
                        }
                    } else {
                        binding.onlineStatusTextView.text = "был(а) недавно"
                        binding.onlineIndicator.visibility = View.GONE
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

    private fun formatLastSeen(timestampMillis: Long): String {
        if (timestampMillis <= 0) return "был(а) недавно"

        val now = System.currentTimeMillis()
        val diff = now - timestampMillis

        return when {
            diff < 60_000 -> "был(а) только что"
            diff < 3600_000 -> "был(а) ${diff / 60_000} мин. назад"
            diff < 86400_000 -> "был(а) ${diff / 3600_000} ч. назад"
            else -> "был(а) ${diff / 86400_000} дн. назад"
        }
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

    private fun loadMessagesUp() {
        if (isLoadingMessages || !hasMoreMessagesUp) return

        isLoadingMessages = true
        Log.d(TAG, "Loading messages up from $firstVisibleMessageId")

        loadMessagesJob = lifecycleScope.launch {
            try {
                val result = chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = firstVisibleMessageId,
                    offsetBefore = 30,
                    offsetAfter = 0
                )

                if (result.isSuccess) {
                    val messages = result.getOrNull()!!
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

            result.add(
                MessageItem(
                    messageId = msg.id,
                    senderId = msg.senderId,
                    text = msg.content?.text ?: "",
                    timestamp = msg.sentAt.seconds * 1000,
                    attachments = msg.content?.attachmentsList ?: emptyList(),
                    readStatus = if (msg.senderId == currentUserId) {
                        if (msg.readByList.any { it != currentUserId }) ReadStatus.READ else ReadStatus.SENT
                    } else {
                        ReadStatus.NONE
                    }
                )
            )
        }

        return result
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
        val currentList = messageAdapter.currentList.toMutableList()

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
        val currentList = messageAdapter.currentList.toMutableList()

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
            currentList.add(
                MessageItem(
                    messageId = msg.id,
                    senderId = msg.senderId,
                    text = msg.content?.text ?: "",
                    timestamp = msg.sentAt.seconds * 1000,
                    attachments = msg.content?.attachmentsList ?: emptyList(),
                    readStatus = if (msg.senderId == currentUserId) {
                        if (msg.readByList.any { it != currentUserId }) ReadStatus.READ else ReadStatus.SENT
                    } else {
                        ReadStatus.NONE
                    }
                )
            )
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
                    addNewMessage(msg)
                }
            }
        }

        lifecycleScope.launch {
            realtimeService.messagesRead.collect { event ->
                if (event.chatId == chatId) {
                    updateMessageReadStatus(event.messageId, event.newReadByList)
                }
            }
        }

        // Подписка на онлайн-статусы обрабатывается в loadOnlineStatus()
    }

    private fun addNewMessage(msg: barkfluff.shared.Shared.Message) {
        val currentList = messageAdapter.currentList.toMutableList()

        // Проверка дубликата
        if (currentList.any { it.type == MessageType.MESSAGE && it.messageId == msg.id }) {
            return
        }

        val messageItem = MessageItem(
            messageId = msg.id,
            senderId = msg.senderId,
            text = msg.content?.text ?: "",
            timestamp = msg.sentAt.seconds * 1000,
            attachments = msg.content?.attachmentsList ?: emptyList(),
            readStatus = if (msg.senderId == currentUserId) {
                if (msg.readByList.any { it != currentUserId }) ReadStatus.READ else ReadStatus.SENT
            } else {
                ReadStatus.NONE
            }
        )

        // Убираем разделитель непрочитанных если он ещё есть
        currentList.removeAll { it.type == MessageType.UNREAD_SEPARATOR }

        // Проверяем, нужно ли добавить разделитель даты
        val msgDate = startOfDay(msg.sentAt.seconds * 1000)
        val lastItem = currentList.lastOrNull()
        if (lastItem != null && lastItem.type == MessageType.MESSAGE) {
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
            if (isOwnMessage || wasAtBottom) {
                binding.messagesRecyclerView.scrollToPosition(currentList.size - 1)
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
        val currentList = messageAdapter.currentList.toMutableList()
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
                }
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
        // Обновляем список, чтобы отразить изменения кэша (например, после удаления видео из кэша)
        if (::messageAdapter.isInitialized) {
            messageAdapter.notifyDataSetChanged()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        // Сбрасываем открытый чат
        OpenChatManager.closeChat()
        chatRepository.close()
        loadMessagesJob?.cancel()
        onlineStatusJob?.cancel()
        onlineStatusSubscription?.cancel()
    }
}
