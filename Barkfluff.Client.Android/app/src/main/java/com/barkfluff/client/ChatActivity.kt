package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.graphics.Bitmap
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.ReadStatus
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.ActivityChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.ImageCompressor
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

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

    // Выбор изображений
    private val imagePickerLauncher = registerForActivityResult(
        ActivityResultContracts.GetMultipleContents()
    ) { uris ->
        if (uris.isNotEmpty()) {
            handleSelectedImages(uris)
        }
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

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityChatBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)
        grpcManager = GrpcManager()
        realtimeService = (application as BarkFluffApplication).realtimeService
        chatRepository = ChatRepository(this)

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
        loadChatInfo()
        loadMessages()

        // Устанавливаем этот чат как открытый
        OpenChatManager.setOpenChat(chatId)

        // Подписываемся на обновления в реальном времени
        subscribeToRealtimeEvents()
    }

    private fun setupToolbar() {
        binding.chatNameTextView.text = chatTitle.ifBlank { "Чат" }
        binding.onlineStatusTextView.text = "загрузка..."

        // Загрузка аватара чата
        loadChatAvatar()

        // Кнопка назад
        binding.toolbar.setNavigationOnClickListener {
            finish()
        }

        // Кнопка информации о чате
        binding.chatInfoButton.setOnClickListener {
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
                userId = chatId.hashCode().toLong()
            ) {
                val result = grpcManager.getFileDownloadUrl(fileId)
                result.getOrNull()
            }
        } else {
            binding.chatAvatar.visibility = View.GONE
            binding.chatAvatarPlaceholder.visibility = View.VISIBLE
            binding.chatAvatarPlaceholder.text = getInitials(chatTitle)
        }
    }

    private fun getInitials(name: String): String {
        val parts = name.trim().split("\\s+".toRegex())
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
            getFileUrl = { null },
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
        imagePickerLauncher.launch("image/*")
    }

    private fun handleSelectedImages(uris: List<Uri>) {
        if (uris.isEmpty()) return

        lifecycleScope.launch {
            val maxImages = 10
            val selectedUris = uris.take(maxImages)

            if (selectedUris.size > 1) {
                Toast.makeText(
                    this@ChatActivity,
                    "Выбрано изображений: ${selectedUris.size}",
                    Toast.LENGTH_SHORT
                ).show()
            }

            // Сжимаем и загружаем каждое изображение
            val fileIds = mutableListOf<String>()
            for ((index, uri) in selectedUris.withIndex()) {
                try {
                    // Сжатие
                    val compressedBytes = ImageCompressor.compressImage(uri, this@ChatActivity)
                        .getOrNull() ?: continue

                    // Загрузка на сервер
                    val uploadResult = chatRepository.uploadFile(
                        compressedBytes,
                        barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                    )

                    if (uploadResult.isSuccess) {
                        fileIds.add(uploadResult.getOrNull()!!)
                        Log.d(TAG, "Image ${index + 1}/${selectedUris.size} uploaded: ${fileIds.last()}")
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error processing image", e)
                }
            }

            if (fileIds.isNotEmpty()) {
                // Отправляем сообщение с вложениями
                sendMessage(fileIds = fileIds)
            } else {
                Toast.makeText(
                    this@ChatActivity,
                    "Не удалось загрузить изображения",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    private fun sendMessage(text: String = binding.messageEditText.text.toString(), fileIds: List<String> = emptyList()) {
        val messageText = text.trim()
        if (messageText.isBlank() && fileIds.isEmpty()) return

        lifecycleScope.launch {
            try {
                // Блокируем поле ввода на время отправки
                binding.messageEditText.isEnabled = false
                binding.sendButton.isEnabled = false

                val result = chatRepository.sendMessage(
                    chatId = chatId,
                    text = messageText,
                    fileIds = fileIds
                )

                if (result.isSuccess) {
                    binding.messageEditText.text?.clear()
                    // Сообщение появится через realtime event
                } else {
                    Toast.makeText(
                        this@ChatActivity,
                        "Ошибка отправки: ${result.exceptionOrNull()?.message}",
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } finally {
                binding.messageEditText.isEnabled = true
                binding.sendButton.isEnabled = true
                binding.messageEditText.requestFocus()
            }
        }
    }

    private fun loadChatInfo() {
        lifecycleScope.launch {
            try {
                val chatInfoResult = chatRepository.getChatInfo(chatId)
                if (chatInfoResult.isSuccess) {
                    val chatInfo = chatInfoResult.getOrNull()!!
                    if (chatInfo.title.isNotBlank()) {
                        binding.chatNameTextView.text = chatInfo.title
                    }
                    if (!isGroupChat && otherUserId > 0) {
                        loadOnlineStatus(otherUserId)
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading chat info", e)
            }
        }
    }

    private fun loadOnlineStatus(userId: Long) {
        lifecycleScope.launch {
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
                        }
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading online status", e)
            }
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
        isLoadingMessages = true
        binding.loadingProgress.visibility = View.VISIBLE

        loadMessagesJob = lifecycleScope.launch {
            delay(LOAD_MESSAGES_DELAY_MS)

            try {
                val result = chatRepository.loadMessages(
                    chatId = chatId,
                    fromMessageId = 0L,
                    offsetBefore = 0,
                    offsetAfter = 0,
                    count = 30
                )

                if (result.isSuccess) {
                    val messages = result.getOrNull()!!
                    displayMessages(messages)

                    if (messages.isNotEmpty()) {
                        firstVisibleMessageId = messages.first().id
                        lastVisibleMessageId = messages.last().id
                    }

                    hasMoreMessagesUp = messages.size >= 30
                    hasMoreMessagesDown = false // Пока не загружали новые
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
                        firstVisibleMessageId = messages.first().id
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
                        lastVisibleMessageId = messages.last().id
                        hasMoreMessagesDown = messages.size >= 30
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
        val messageItems = messages.map { msg ->
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
        }
        messageAdapter.submitList(messageItems)
    }

    private fun prependMessages(messages: List<barkfluff.shared.Shared.Message>) {
        val currentList = messageAdapter.currentList.toMutableList()
        val newItems = messages.map { msg ->
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
        }
        currentList.addAll(0, newItems)
        messageAdapter.submitList(currentList)
    }

    private fun appendMessages(messages: List<barkfluff.shared.Shared.Message>) {
        val currentList = messageAdapter.currentList.toMutableList()
        val newItems = messages.map { msg ->
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
        }
        currentList.addAll(newItems)
        messageAdapter.submitList(currentList)
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

        lifecycleScope.launch {
            realtimeService.onlineStatuses.collect { status ->
                if (!isGroupChat && status.userId == otherUserId) {
                    withContext(Dispatchers.Main) {
                        val isOnline = status.status.getNumber() == barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE.getNumber()
                        if (isOnline) {
                            binding.onlineStatusTextView.text = "в сети"
                            binding.onlineIndicator.visibility = View.VISIBLE
                        } else {
                            binding.onlineStatusTextView.text = "был(а) недавно"
                            binding.onlineIndicator.visibility = View.GONE
                        }
                    }
                }
            }
        }
    }

    private fun addNewMessage(msg: barkfluff.shared.Shared.Message) {
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

        val currentList = messageAdapter.currentList.toMutableList()
        currentList.add(messageItem)
        messageAdapter.submitList(currentList)

        // Прокрутка к новому сообщению
        binding.messagesRecyclerView.scrollToPosition(currentList.size - 1)

        lastVisibleMessageId = msg.id
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

    override fun onDestroy() {
        super.onDestroy()
        // Сбрасываем открытый чат
        OpenChatManager.closeChat()
        chatRepository.close()
        grpcManager.shutdown()
        loadMessagesJob?.cancel()
    }
}
