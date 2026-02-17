package com.barkfluff.android.repository

import barkfluff.messages.ListChatsRequest
import barkfluff.messages.MessagesApiGrpcKt
import barkfluff.shared.PageRequest
import com.barkfluff.android.data.local.AppDataStore
import com.barkfluff.android.data.model.ChatItem
import com.barkfluff.android.data.remote.GrpcChannelFactory
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class ChatRepository @Inject constructor(
    private val dataStore: AppDataStore,
    private val channelFactory: GrpcChannelFactory,
    private val authRepository: AuthRepository,
) {
    private suspend fun getStub(): MessagesApiGrpcKt.MessagesApiCoroutineStub {
        val config = dataStore.getServerConfig()
            ?: error("Server not configured")
        val channel = channelFactory.getChannel(
            config.messagesHost, config.messagesPort, config.messagesTls
        )
        return MessagesApiGrpcKt.MessagesApiCoroutineStub(channel)
    }

    suspend fun listChats(offset: Int = 0, size: Int = 50): Result<List<ChatItem>> = runCatching {
        authRepository.ensureValidToken()
        val request = ListChatsRequest.newBuilder()
            .setPagination(
                PageRequest.newBuilder()
                    .setOffset(offset)
                    .setSize(size)
                    .build()
            )
            .build()
        val response = getStub().listChats(request)
        response.chatsList.map { chat ->
            val lastMsg = if (chat.hasLastMessage()) chat.lastMessage else null
            val lastText = lastMsg?.content?.text?.takeIf { it.isNotEmpty() }
            val lastTime = lastMsg?.sentAt?.let { ts ->
                Instant.ofEpochSecond(ts.seconds, ts.nanos.toLong())
            }
            ChatItem(
                id = chat.id,
                title = chat.title,
                picture = chat.picture.takeIf { it.isNotEmpty() },
                isGroupChat = chat.isGroupChat,
                lastMessageText = lastText,
                lastMessageTime = lastTime,
                unreadCount = chat.countUnread,
            )
        }
    }
}
