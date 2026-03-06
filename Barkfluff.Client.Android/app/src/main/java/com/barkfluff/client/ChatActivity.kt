package com.barkfluff.client

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.adapter.ReadStatus
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.ActivityChatBinding
import com.barkfluff.client.databinding.DialogChatProfileBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.picker.ImagePickerBottomSheet
import com.barkfluff.client.picker.ImagePickerResult
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.ImageCompressor
import com.barkfluff.client.utils.MessageItemAnimator
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
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

    companion object {
        private const val TAG = "ChatActivity"
        private const val EXTRA_CHAT_ID = "chat_id"
        private const val EXTRA_CHAT_TITLE = "chat_title"
        private const val EXTRA_CHAT_AVATAR_FILE_ID = "chat_avatar_file_id"
        private const val EXTRA_IS_GROUP_CHAT = "is_group_chat"
        private const val EXTRA_OTHER_USER_ID = "other_user_id"
        private const val LOAD_MESSAGES_DELAY_MS = 500L
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
        loadChatInfoAndMessages()

        // Устанавливаем этот чат как открытый
        OpenChatManager.setOpenChat(chatId)

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
            showChatProfile()
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
            binding.chatAvatar.visibility = View.GONE
            binding.chatAvatarPlaceholder.visibility = View.VISIBLE
            binding.chatAvatarPlaceholder.text = getInitials(chatTitle)
        }
    }

    private fun getInitials(name: String): String {
        val parts = name.trim().split("\\s+".toRegex()).filter { it.isNotEmpty() }
        return when {
            parts.size >= 2 -> "${parts[0].first()}${parts[1].first()}".uppercase()
            parts.size == 1 -> parts[0].first().uppercase()
            else -> "?"
        }
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
            scope = scope
        )

        binding.messagesRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@ChatActivity).apply {
                stackFromEnd = true // Прокрутка к концу списка
            }
            adapter = messageAdapter
            itemAnimator = MessageItemAnimator()

            // Обработчик скролла для пагинации
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
                }
            })
        }
    }

    private fun setupMessageInput() {
        binding.sendButton.setOnClickListener {
            sendMessage()
        }

        binding.messageEditText.setOnEditorActionListener { _, _, _ ->
            sendMessage()
            true
        }

        binding.attachButton.setOnClickListener {
            pickImages()
        }
    }

    private fun pickImages() {
        val imagePicker = ImagePickerBottomSheet.newInstance { result ->
            handleSelectedImages(result)
        }
        imagePicker.show(supportFragmentManager, "ImagePickerBottomSheet")
    }

    private fun handleSelectedImages(result: ImagePickerResult) {
        Log.d(TAG, "handleSelectedImages: uris.size=${result.uris.size}, uris=${result.uris}, sendAsFile=${result.sendAsFile}, sendSeparately=${result.sendSeparately}")

        val uris = result.uris
        if (uris.isEmpty()) return

        val sendAsFile = result.sendAsFile
        val sendSeparately = result.sendSeparately

        lifecycleScope.launch {
            val selectedUris = uris.take(ImagePickerBottomSheet.MAX_SELECTION)

            if (selectedUris.size > 1) {
                Toast.makeText(
                    this@ChatActivity,
                    "Выбрано изображений: ${selectedUris.size}",
                    Toast.LENGTH_SHORT
                ).show()
            }

            // Определяем тип файла для загрузки
            val uploadFileType = if (sendAsFile) {
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
            } else {
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE
            }

            // Загружаем каждое изображение
            val fileIds = mutableListOf<String>()
            for ((index, uri) in selectedUris.withIndex()) {
                try {
                    val bytes = if (sendAsFile) {
                        // Без сжатия — читаем оригинальный файл
                        readBytesFromUri(uri)
                    } else {
                        // Со сжатием
                        ImageCompressor.compressImage(uri, this@ChatActivity).getOrNull()
                    }

                    if (bytes == null) continue

                    // Загрузка на сервер
                    val uploadResult = chatRepository.uploadFile(bytes, uploadFileType)

                    if (uploadResult.isSuccess) {
                        val fileId = uploadResult.getOrNull()!!
                        fileIds.add(fileId)
                        Log.d(TAG, "Image ${index + 1}/${selectedUris.size} uploaded: $fileId, fileIds.size=${fileIds.size}")
                    } else {
                        Log.e(TAG, "Image ${index + 1}/${selectedUris.size} upload failed: ${uploadResult.exceptionOrNull()?.message}")
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error processing image ${index + 1}/${selectedUris.size}", e)
                }
            }

            Log.d(TAG, "After upload loop: fileIds.size=${fileIds.size}, fileIds=$fileIds")

            if (fileIds.isNotEmpty()) {
                Log.d(TAG, "Sending ${fileIds.size} fileIds: $fileIds, sendSeparately=$sendSeparately")
                if (sendSeparately) {
                    // Отправляем каждое изображение отдельным сообщением
                    for (fileId in fileIds) {
                        sendMessage(fileIds = listOf(fileId))
                    }
                } else {
                    // Отправляем все изображения в одном сообщении
                    sendMessage(fileIds = fileIds)
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

    private fun loadMessages() {
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
                    Toast.makeText(
                        this@ChatActivity,
                        "Ошибка загрузки сообщений",
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading messages", e)
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
                binding.messagesRecyclerView.scrollToPosition(scrollToPosition)
            }
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

    private fun showChatProfile() {
        val dialogView = layoutInflater.inflate(R.layout.dialog_chat_profile, null)
        val dialog = MaterialAlertDialogBuilder(this)
            .setView(dialogView)
            .setNegativeButton("Закрыть", null)
            .create()

        // Получаем ссылки на view
        val avatarImageView = dialogView.findViewById<com.google.android.material.imageview.ShapeableImageView>(R.id.profileAvatarImageView)
        val avatarPlaceholder = dialogView.findViewById<TextView>(R.id.profileAvatarPlaceholder)
        val nameTextView = dialogView.findViewById<TextView>(R.id.profileNameTextView)
        val usernameTextView = dialogView.findViewById<TextView>(R.id.profileUsernameTextView)
        val onlineStatusTextView = dialogView.findViewById<TextView>(R.id.profileOnlineStatusTextView)
        val bioTextView = dialogView.findViewById<TextView>(R.id.profileBioTextView)
        val onlineIndicator = dialogView.findViewById<View>(R.id.onlineIndicator)
        val mediaTypeChipGroup = dialogView.findViewById<com.google.android.material.chip.ChipGroup>(R.id.mediaTypeChipGroup)
        val chipPhotos = dialogView.findViewById<com.google.android.material.chip.Chip>(R.id.chipPhotos)
        val chipVideos = dialogView.findViewById<com.google.android.material.chip.Chip>(R.id.chipVideos)
        val chipFiles = dialogView.findViewById<com.google.android.material.chip.Chip>(R.id.chipFiles)
        val attachmentsContainer = dialogView.findViewById<View>(R.id.attachmentsContainer)
        val attachmentsRecyclerView = dialogView.findViewById<androidx.recyclerview.widget.RecyclerView>(R.id.attachmentsRecyclerView)
        val attachmentsLoading = dialogView.findViewById<View>(R.id.attachmentsLoading)
        val attachmentsEmpty = dialogView.findViewById<TextView>(R.id.attachmentsEmpty)

        // Адаптер для вложений
        var attachmentAdapter: com.barkfluff.client.adapter.AttachmentPreviewAdapter? = null
        attachmentAdapter = com.barkfluff.client.adapter.AttachmentPreviewAdapter(
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onAttachmentClick = { attachmentInfo ->
                val att = attachmentInfo.attachment
                when (att.type) {
                    barkfluff.shared.Shared.MessageAttachmentType.IMAGE,
                    barkfluff.shared.Shared.MessageAttachmentType.GIF -> {
                        val adapter = attachmentAdapter
                        if (adapter != null) {
                            val allFileIds = adapter.currentList.map { it.attachment.fileId }
                            val allPreviewUrls = adapter.currentList.map { it.attachment.previewUrl }
                            val position = adapter.currentList.indexOf(attachmentInfo).coerceAtLeast(0)
                            startActivity(
                                ImageViewerActivity.createIntent(
                                    this@ChatActivity, allFileIds, allPreviewUrls, position
                                )
                            )
                        }
                    }
                    barkfluff.shared.Shared.MessageAttachmentType.VIDEO -> {
                        val cachedPath = FileCache.getFile(att.fileId)?.absolutePath
                        startActivity(
                            MediaViewerActivity.createIntent(
                                this@ChatActivity,
                                att.fileId,
                                att.fileName.ifBlank { "Видео" },
                                cachedPath
                            )
                        )
                    }
                    else -> {
                        // Документ / аудио — скачать и открыть системным приложением
                        lifecycleScope.launch {
                            try {
                                val file = withContext(Dispatchers.IO) {
                                    FileCache.getFile(att.fileId)
                                        ?: chatRepository.downloadFile(att.fileId)
                                }
                                if (file != null) {
                                    val uri = androidx.core.content.FileProvider.getUriForFile(
                                        this@ChatActivity,
                                        "${packageName}.fileprovider",
                                        file
                                    )
                                    val mimeType = getMimeType(att.fileName) ?: "*/*"
                                    val intent = Intent(Intent.ACTION_VIEW).apply {
                                        setDataAndType(uri, mimeType)
                                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                                    }
                                    startActivity(Intent.createChooser(intent, "Открыть с помощью"))
                                } else {
                                    Toast.makeText(
                                        this@ChatActivity,
                                        "Не удалось скачать файл",
                                        Toast.LENGTH_SHORT
                                    ).show()
                                }
                            } catch (e: Exception) {
                                Log.e(TAG, "Error opening file", e)
                                Toast.makeText(
                                    this@ChatActivity,
                                    "Ошибка открытия файла",
                                    Toast.LENGTH_SHORT
                                ).show()
                            }
                        }
                    }
                }
            }
        )
        attachmentsRecyclerView.layoutManager = androidx.recyclerview.widget.GridLayoutManager(this, 3)
        attachmentsRecyclerView.adapter = attachmentAdapter

        if (isGroupChat) {
            // Для группового чата — показываем название чата
            nameTextView.text = chatTitle.trim()
            usernameTextView.visibility = View.GONE
            onlineStatusTextView.visibility = View.GONE
            bioTextView.visibility = View.GONE

            // Загрузка аватара чата
            if (!chatAvatarFileId.isNullOrBlank()) {
                AvatarLoader.loadByFileId(
                    imageView = avatarImageView,
                    placeholderView = avatarPlaceholder,
                    fileId = chatAvatarFileId!!,
                    displayName = chatTitle,
                    userId = chatId.hashCode().toLong(),
                    size = 240
                ) {
                    chatRepository.getFileDownloadUrl(chatAvatarFileId!!).getOrNull()
                }
            } else {
                avatarImageView.visibility = View.GONE
                avatarPlaceholder.visibility = View.VISIBLE
                avatarPlaceholder.text = getInitials(chatTitle)
            }
        } else {
            // Для ЛС — загружаем данные пользователя
            if (otherUserId > 0) {
                lifecycleScope.launch {
                    try {
                        val userResult = chatRepository.getUserData(otherUserId)
                        if (userResult.isSuccess) {
                            val user = userResult.getOrNull()!!

                            // Имя
                            val displayName = "${user.firstName} ${user.lastName}".trim()
                            nameTextView.text = if (displayName.isNotBlank()) displayName else user.username

                            // Username
                            usernameTextView.text = "@${user.username}"
                            usernameTextView.visibility = View.VISIBLE

                            // Био
                            if (user.bio.isNotBlank()) {
                                bioTextView.text = user.bio
                                bioTextView.visibility = View.VISIBLE
                            } else {
                                bioTextView.visibility = View.GONE
                            }

                            // Аватар - используем ПОЛНУЮ версию, а не превью
                            val avatarFileId = user.profilePictureFileId
                            if (!avatarFileId.isNullOrBlank()) {
                                AvatarLoader.loadByFileId(
                                    imageView = avatarImageView,
                                    placeholderView = avatarPlaceholder,
                                    fileId = avatarFileId,
                                    displayName = displayName,
                                    userId = otherUserId,
                                    size = 240
                                ) {
                                    chatRepository.getFileDownloadUrl(avatarFileId).getOrNull()
                                }
                            } else {
                                avatarImageView.visibility = View.GONE
                                avatarPlaceholder.visibility = View.VISIBLE
                                avatarPlaceholder.text = getInitials(displayName)
                            }
                        }
                    } catch (e: Exception) {
                        Log.e(TAG, "Error loading user profile", e)
                    }
                }

                // Статус онлайна
                lifecycleScope.launch {
                    try {
                        val onlinerClient = grpcManager.onlinerClient
                        if (onlinerClient != null) {
                            val request = barkfluff.onliner.OnlinerApiOuterClass.GetOnlineStatusRequest.newBuilder()
                                .addUserIds(otherUserId)
                                .build()
                            val response = onlinerClient.getOnlineStatus(request)
                            val userStatus = response.usersStatusesList.firstOrNull()

                            if (userStatus != null) {
                                val isOnline = userStatus.status.getNumber() == barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE.getNumber()
                                if (isOnline) {
                                    onlineStatusTextView.text = "в сети"
                                    onlineStatusTextView.setTextColor(ContextCompat.getColor(this@ChatActivity, R.color.primary))
                                    onlineIndicator.visibility = View.VISIBLE
                                } else {
                                    val lastSeen = formatLastSeen(userStatus.lastSeen.seconds * 1000)
                                    onlineStatusTextView.text = lastSeen
                                    onlineStatusTextView.setTextColor(ContextCompat.getColor(this@ChatActivity, R.color.on_surface_variant))
                                    onlineIndicator.visibility = View.GONE
                                }
                            } else {
                                onlineStatusTextView.text = "был(а) недавно"
                                onlineIndicator.visibility = View.GONE
                            }
                        }
                    } catch (e: Exception) {
                        Log.e(TAG, "Error loading online status for profile", e)
                    }
                }
            }
        }

        // Обработка выбора типа медиа
        var currentType: barkfluff.shared.Shared.MessageAttachmentType? = null

        fun loadAttachments(type: barkfluff.shared.Shared.MessageAttachmentType) {
            attachmentsContainer.visibility = View.VISIBLE
            attachmentsLoading.visibility = View.VISIBLE
            attachmentsRecyclerView.visibility = View.GONE
            attachmentsEmpty.visibility = View.GONE

            lifecycleScope.launch {
                try {
                    val result = chatRepository.getChatAttachments(chatId, type)
                    if (result.isSuccess) {
                        val attachments = result.getOrNull()!!
                        if (attachments.isEmpty()) {
                            attachmentsLoading.visibility = View.GONE
                            attachmentsEmpty.visibility = View.VISIBLE
                            attachmentsEmpty.text = when (type) {
                                barkfluff.shared.Shared.MessageAttachmentType.IMAGE -> "Нет фото"
                                barkfluff.shared.Shared.MessageAttachmentType.VIDEO -> "Нет видео"
                                else -> "Нет файлов"
                            }
                        } else {
                            attachmentAdapter.submitList(attachments)
                            attachmentsLoading.visibility = View.GONE
                            attachmentsRecyclerView.visibility = View.VISIBLE
                        }
                    } else {
                        attachmentsLoading.visibility = View.GONE
                        attachmentsEmpty.visibility = View.VISIBLE
                        attachmentsEmpty.text = "Ошибка загрузки"
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error loading attachments", e)
                    attachmentsLoading.visibility = View.GONE
                    attachmentsEmpty.visibility = View.VISIBLE
                }
            }
        }

        chipPhotos.setOnClickListener {
            if (chipPhotos.isChecked) {
                attachmentsRecyclerView.layoutManager =
                    androidx.recyclerview.widget.GridLayoutManager(this@ChatActivity, 3)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.IMAGE)
            } else {
                attachmentsContainer.visibility = View.GONE
            }
        }

        chipVideos.setOnClickListener {
            if (chipVideos.isChecked) {
                attachmentsRecyclerView.layoutManager =
                    androidx.recyclerview.widget.GridLayoutManager(this@ChatActivity, 3)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.VIDEO)
            } else {
                attachmentsContainer.visibility = View.GONE
            }
        }

        chipFiles.setOnClickListener {
            if (chipFiles.isChecked) {
                attachmentsRecyclerView.layoutManager =
                    androidx.recyclerview.widget.LinearLayoutManager(this@ChatActivity)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT)
            } else {
                attachmentsContainer.visibility = View.GONE
            }
        }

        // По умолчанию открываем вкладку с фотографиями
        chipPhotos.isChecked = true
        attachmentsRecyclerView.layoutManager =
            androidx.recyclerview.widget.GridLayoutManager(this, 3)
        loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.IMAGE)

        dialog.show()
    }

    override fun onStart() {
        super.onStart()
        // При возврате из фона — подгружаем сообщения, пришедшие пока приложение было свёрнуто.
        // Ждём пока RealtimeService переподключится (каналы пересоздаются в ProcessLifecycleOwner.onStart).
        if (lastVisibleMessageId > 0L && !isLoadingMessages) {
            lifecycleScope.launch {
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
