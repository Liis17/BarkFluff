package com.barkfluff.messenger.presentation.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.messenger.data.repository.ChatRepository
import com.barkfluff.messenger.domain.model.Chat
import com.barkfluff.messenger.domain.model.Message
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class ChatEvent {
    data class Error(val message: String) : ChatEvent()
    object MessageSent : ChatEvent()
    data class NewMessage(val message: Message) : ChatEvent()
}

data class ChatListState(
    val isLoading: Boolean = false,
    val chats: List<Chat> = emptyList(),
    val error: String? = null
)

data class ChatState(
    val isLoading: Boolean = false,
    val messages: List<Message> = emptyList(),
    val chat: Chat? = null,
    val error: String? = null
)

@HiltViewModel
class ChatViewModel @Inject constructor(
    private val chatRepository: ChatRepository
) : ViewModel() {

    private val _chatListState = MutableStateFlow(ChatListState())
    val chatListState: StateFlow<ChatListState> = _chatListState.asStateFlow()

    private val _chatState = MutableStateFlow(ChatState())
    val chatState: StateFlow<ChatState> = _chatState.asStateFlow()

    private val _events = MutableSharedFlow<ChatEvent>()
    val events = _events.asSharedFlow()

    init {
        loadChats()
        subscribeToMessages()
    }

    fun loadChats() {
        viewModelScope.launch {
            _chatListState.value = _chatListState.value.copy(isLoading = true)

            chatRepository.getChats()
                .onSuccess { chats ->
                    _chatListState.value = _chatListState.value.copy(
                        chats = chats,
                        isLoading = false,
                        error = null
                    )
                }
                .onFailure { error ->
                    _chatListState.value = _chatListState.value.copy(
                        isLoading = false,
                        error = error.message
                    )
                }
        }
    }

    fun loadChat(chatId: String) {
        viewModelScope.launch {
            _chatState.value = _chatState.value.copy(isLoading = true)

            // Find chat from list
            val chat = _chatListState.value.chats.find { it.id == chatId }
            _chatState.value = _chatState.value.copy(chat = chat)

            // Load messages
            chatRepository.getMessages(chatId)
                .onSuccess { messages ->
                    _chatState.value = _chatState.value.copy(
                        messages = messages.reversed(), // Reverse for chat UI
                        isLoading = false,
                        error = null
                    )
                }
                .onFailure { error ->
                    _chatState.value = _chatState.value.copy(
                        isLoading = false,
                        error = error.message
                    )
                }
        }
    }

    fun sendMessage(
        recipientId: Long? = null,
        chatId: String? = null,
        text: String? = null,
        fileIds: List<String> = emptyList()
    ) {
        viewModelScope.launch {
            chatRepository.sendMessage(recipientId, chatId, text, fileIds)
                .onSuccess { message ->
                    // Add message to current chat if it's the same
                    if (message.chatId == _chatState.value.chat?.id) {
                        _chatState.value = _chatState.value.copy(
                            messages = _chatState.value.messages + message
                        )
                    }
                    _events.emit(ChatEvent.MessageSent)
                }
                .onFailure { error ->
                    _events.emit(ChatEvent.Error(error.message ?: "Failed to send message"))
                }
        }
    }

    fun markAsRead(messageIds: List<String>) {
        viewModelScope.launch {
            chatRepository.markAsRead(messageIds)
        }
    }

    fun createGroupChat(title: String, userIds: List<Long>) {
        viewModelScope.launch {
            chatRepository.createGroupChat(title, userIds)
                .onSuccess { chat ->
                    _chatListState.value = _chatListState.value.copy(
                        chats = listOf(chat) + _chatListState.value.chats
                    )
                }
                .onFailure { error ->
                    _events.emit(ChatEvent.Error(error.message ?: "Failed to create group"))
                }
        }
    }

    private fun subscribeToMessages() {
        viewModelScope.launch {
            chatRepository.subscribeToNewMessages()
                .catch { error ->
                    _events.emit(ChatEvent.Error(error.message ?: "Connection error"))
                }
                .collect { message ->
                    // Add message to current chat if it's the same
                    if (message.chatId == _chatState.value.chat?.id) {
                        _chatState.value = _chatState.value.copy(
                            messages = _chatState.value.messages + message
                        )
                    }

                    // Update last message in chat list
                    val updatedChats = _chatListState.value.chats.map { chat ->
                        if (chat.id == message.chatId) {
                            chat.copy(lastMessage = message)
                        } else {
                            chat
                        }
                    }
                    _chatListState.value = _chatListState.value.copy(chats = updatedChats)

                    _events.emit(ChatEvent.NewMessage(message))
                }
        }
    }

    fun loadMoreMessages(chatId: String, fromMessageId: String) {
        viewModelScope.launch {
            chatRepository.getMessages(chatId, fromMessageId)
                .onSuccess { messages ->
                    _chatState.value = _chatState.value.copy(
                        messages = messages.reversed() + _chatState.value.messages
                    )
                }
        }
    }
}
