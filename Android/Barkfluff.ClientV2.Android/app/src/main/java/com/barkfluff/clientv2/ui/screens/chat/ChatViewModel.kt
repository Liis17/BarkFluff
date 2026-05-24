package com.barkfluff.clientv2.ui.screens.chat

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.files.FilesApiOuterClass
import barkfluff.shared.Shared
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.repository.ChatRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ChatUiState(
    val title: String = "",
    val messages: List<Shared.Message> = emptyList(),
    val loading: Boolean = true,
    val sending: Boolean = false,
    val error: String? = null,
)

class ChatViewModel(
    private val chatId: String,
    private val chatRepository: ChatRepository,
    private val realtimeService: RealtimeService,
    globalParam: GlobalParam
) : ViewModel() {

    val myUserId: Long = globalParam.userId

    private val _ui = MutableStateFlow(ChatUiState())
    val ui: StateFlow<ChatUiState> = _ui.asStateFlow()

    init {
        loadInfo()
        loadMessages()
        observeRealtime()
    }

    private fun loadInfo() {
        viewModelScope.launch {
            chatRepository.getChatInfo(chatId).onSuccess { info ->
                _ui.update { it.copy(title = info.title) }
            }
        }
    }

    fun loadMessages() {
        viewModelScope.launch {
            _ui.update { it.copy(loading = true, error = null) }
            chatRepository.loadMessages(chatId)
                .onSuccess { msgs ->
                    val sorted = msgs.sortedBy { sentMillis(it) }
                    _ui.update { it.copy(loading = false, messages = sorted) }
                    markRead(sorted)
                }
                .onFailure { e -> _ui.update { it.copy(loading = false, error = e.message ?: "Не удалось загрузить сообщения") } }
        }
    }

    fun sendText(text: String, forwardedMessageId: Long = 0L) {
        val trimmed = text.trim()
        if (trimmed.isEmpty() && forwardedMessageId == 0L) return
        viewModelScope.launch {
            _ui.update { it.copy(sending = true) }
            chatRepository.sendMessage(chatId, trimmed, forwardedMessageId = forwardedMessageId)
                .onSuccess { appendMessage(it) }
            _ui.update { it.copy(sending = false) }
        }
    }

    /** Пересылка сообщения в другой чат (forwardedMessageId, целевой chatId). */
    fun forwardMessage(messageId: Long, targetChatId: String) {
        viewModelScope.launch {
            chatRepository.sendMessage(targetChatId, "", forwardedMessageId = messageId)
        }
    }

    fun sendImage(bytes: ByteArray) {
        if (bytes.isEmpty()) return
        viewModelScope.launch {
            _ui.update { it.copy(sending = true) }
            chatRepository.uploadFile(bytes, FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_IMAGE)
                .onSuccess { fileId ->
                    chatRepository.sendMessage(chatId, "", listOf(fileId)).onSuccess { appendMessage(it) }
                }
            _ui.update { it.copy(sending = false) }
        }
    }

    fun editMessage(messageId: Long, text: String) {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) return
        viewModelScope.launch {
            _ui.update { it.copy(sending = true) }
            chatRepository.editMessage(messageId, trimmed).onSuccess { replaceMessage(it) }
            _ui.update { it.copy(sending = false) }
        }
    }

    fun deleteMessage(messageId: Long) {
        viewModelScope.launch {
            chatRepository.deleteMessage(messageId).onSuccess { removeMessage(messageId) }
        }
    }

    private fun replaceMessage(msg: Shared.Message) {
        _ui.update { state ->
            val updated = if (state.messages.any { it.id == msg.id })
                state.messages.map { if (it.id == msg.id) msg else it }
            else state.messages + msg
            state.copy(messages = updated.sortedBy { sentMillis(it) })
        }
    }

    private fun removeMessage(id: Long) {
        _ui.update { state -> state.copy(messages = state.messages.filterNot { it.id == id }) }
    }

    private fun observeRealtime() {
        viewModelScope.launch {
            realtimeService.newMessages.collect { event ->
                if (event.chatId == chatId && event.hasMessage()) {
                    appendMessage(event.message)
                    markRead(listOf(event.message))
                }
            }
        }
        viewModelScope.launch {
            realtimeService.messageEdited.collect { event ->
                if (event.chatId == chatId && event.hasMessage()) replaceMessage(event.message)
            }
        }
        viewModelScope.launch {
            realtimeService.messageDeleted.collect { event ->
                if (event.chatId == chatId) removeMessage(event.messageId)
            }
        }
    }

    private fun appendMessage(msg: Shared.Message) {
        _ui.update { state ->
            if (state.messages.any { it.id == msg.id }) state
            else state.copy(messages = (state.messages + msg).sortedBy { sentMillis(it) })
        }
    }

    private fun markRead(msgs: List<Shared.Message>) {
        val ids = msgs.filter { it.senderId != myUserId && it.id > 0 }.map { it.id }
        if (ids.isEmpty()) return
        viewModelScope.launch { chatRepository.markAsRead(ids) }
    }

    private fun sentMillis(msg: Shared.Message): Long =
        if (msg.hasSentAt()) msg.sentAt.seconds * 1000 + msg.sentAt.nanos / 1_000_000 else 0L
}
