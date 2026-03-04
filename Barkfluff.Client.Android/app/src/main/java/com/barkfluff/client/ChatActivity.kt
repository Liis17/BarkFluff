package com.barkfluff.client

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
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
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.picker.ImagePickerBottomSheet
import com.barkfluff.client.picker.ImagePickerResult
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.ImageCompressor
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
            scope = scope
        )

        binding.messagesRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@ChatActivity).apply {
                stackFromEnd = true // Прокрутка к концу списка
            }
            adapter = messageAdapter

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

        // TODO: Заполнить данными профиля
        // Для группового чата — показать название и участников
        // Для ЛС — показать имя, аватар, био, время онлайна

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
