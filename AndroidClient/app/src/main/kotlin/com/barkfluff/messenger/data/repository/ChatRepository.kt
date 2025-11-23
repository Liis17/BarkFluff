package com.barkfluff.messenger.data.repository

import com.barkfluff.messenger.domain.model.*
import io.grpc.ManagedChannel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.withContext
import java.time.Instant
import javax.inject.Inject
import javax.inject.Named

class ChatRepository @Inject constructor(
    @Named("messagesChannel") private val messagesChannel: ManagedChannel,
    @Named("updatesChannel") private val updatesChannel: ManagedChannel
) {

    suspend fun getChats(offset: Int = 0, limit: Int = 50): Result<List<Chat>> =
        withContext(Dispatchers.IO) {
            try {
                val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

                val request = messages_api.MessagesApi.ListChatsRequest.newBuilder()
                    .setPage(
                        shared.Shared.PageRequest.newBuilder()
                            .setOffset(offset)
                            .setSize(limit)
                            .build()
                    )
                    .build()

                val response = stub.listChats(request)

                val chats = response.chatsList.map { chatProto ->
                    Chat(
                        id = chatProto.id,
                        title = chatProto.title,
                        pictureUrl = chatProto.picture.takeIf { it.isNotBlank() },
                        isGroupChat = chatProto.isGroupChat,
                        lastMessage = chatProto.lastMessage?.let { msgProto ->
                            Message(
                                id = msgProto.id,
                                senderId = msgProto.senderId,
                                chatId = null,
                                content = MessageContent(
                                    text = msgProto.content.text.takeIf { it.isNotBlank() },
                                    attachments = msgProto.content.attachmentsList.map { att ->
                                        MessageAttachment(
                                            id = att.id,
                                            type = MessageAttachmentType.valueOf(att.type.name),
                                            fileId = att.fileId,
                                            previewUrl = att.previewUrl.takeIf { it.isNotBlank() },
                                            size = att.attachmentSize
                                        )
                                    }
                                ),
                                sentAt = Instant.ofEpochSecond(
                                    msgProto.sentAt.seconds,
                                    msgProto.sentAt.nanos.toLong()
                                ),
                                readBy = msgProto.readByList,
                                type = MessageType.valueOf(msgProto.type.name)
                            )
                        },
                        members = chatProto.membersList.map { member ->
                            ChatMember(
                                userId = member.userId,
                                joinedAt = Instant.ofEpochSecond(
                                    member.joinedAt.seconds,
                                    member.joinedAt.nanos.toLong()
                                )
                            )
                        },
                        unreadCount = chatProto.countUnread
                    )
                }

                Result.success(chats)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun getMessages(
        chatId: String,
        fromMessageId: String? = null,
        limit: Int = 50
    ): Result<List<Message>> = withContext(Dispatchers.IO) {
        try {
            val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

            val request = messages_api.MessagesApi.ListMessagesRequest.newBuilder()
                .setChatId(chatId)
                .setPage(
                    shared.Shared.PageRequest.newBuilder()
                        .setOffset(0)
                        .setSize(limit)
                        .build()
                )
                .apply {
                    fromMessageId?.let { setFromMessageId(it) }
                }
                .build()

            val response = stub.listMessages(request)

            val messages = response.messagesList.map { msgProto ->
                Message(
                    id = msgProto.id,
                    senderId = msgProto.senderId,
                    chatId = chatId,
                    content = MessageContent(
                        text = msgProto.content.text.takeIf { it.isNotBlank() },
                        attachments = msgProto.content.attachmentsList.map { att ->
                            MessageAttachment(
                                id = att.id,
                                type = MessageAttachmentType.valueOf(att.type.name),
                                fileId = att.fileId,
                                previewUrl = att.previewUrl.takeIf { it.isNotBlank() },
                                size = att.attachmentSize
                            )
                        }
                    ),
                    sentAt = Instant.ofEpochSecond(
                        msgProto.sentAt.seconds,
                        msgProto.sentAt.nanos.toLong()
                    ),
                    readBy = msgProto.readByList,
                    type = MessageType.valueOf(msgProto.type.name)
                )
            }

            Result.success(messages)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun sendMessage(
        recipientId: Long? = null,
        chatId: String? = null,
        text: String? = null,
        fileIds: List<String> = emptyList()
    ): Result<Message> = withContext(Dispatchers.IO) {
        try {
            val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

            val requestBuilder = messages_api.MessagesApi.SendMessageRequest.newBuilder()

            if (recipientId != null) {
                requestBuilder.setRecipientUserId(recipientId)
            } else if (chatId != null) {
                requestBuilder.setChatId(chatId)
            }

            val messageBuilder = messages_api.MessagesApi.OutgoingMessage.newBuilder()
            text?.let { messageBuilder.setText(it) }
            messageBuilder.addAllFilesIds(fileIds)

            val request = requestBuilder.setMessage(messageBuilder.build()).build()
            val response = stub.sendMessage(request)

            val message = Message(
                id = response.message.id,
                senderId = response.message.senderId,
                chatId = response.chatId,
                content = MessageContent(
                    text = response.message.content.text.takeIf { it.isNotBlank() },
                    attachments = response.message.content.attachmentsList.map { att ->
                        MessageAttachment(
                            id = att.id,
                            type = MessageAttachmentType.valueOf(att.type.name),
                            fileId = att.fileId,
                            previewUrl = att.previewUrl.takeIf { it.isNotBlank() },
                            size = att.attachmentSize
                        )
                    }
                ),
                sentAt = Instant.ofEpochSecond(
                    response.message.sentAt.seconds,
                    response.message.sentAt.nanos.toLong()
                ),
                readBy = response.message.readByList,
                type = MessageType.valueOf(response.message.type.name)
            )

            Result.success(message)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun markAsRead(messageIds: List<String>): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

            val request = messages_api.MessagesApi.MarkAsReadRequest.newBuilder()
                .addAllMessageIds(messageIds)
                .build()

            stub.markAsRead(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    fun subscribeToNewMessages(): Flow<Message> = flow {
        val stub = updates_api.UpdatesApiGrpc.newBlockingStub(updatesChannel)

        val request = updates_api.UpdatesApi.SubscribeNewMessagesRequest.newBuilder().build()
        val stream = stub.subscribeNewMessages(request)

        while (stream.hasNext()) {
            val event = stream.next()
            val msgProto = event.message

            val message = Message(
                id = msgProto.id,
                senderId = msgProto.senderId,
                chatId = event.chatId,
                content = MessageContent(
                    text = msgProto.content.text.takeIf { it.isNotBlank() },
                    attachments = msgProto.content.attachmentsList.map { att ->
                        MessageAttachment(
                            id = att.id,
                            type = MessageAttachmentType.valueOf(att.type.name),
                            fileId = att.fileId,
                            previewUrl = att.previewUrl.takeIf { it.isNotBlank() },
                            size = att.attachmentSize
                        )
                    }
                ),
                sentAt = Instant.ofEpochSecond(
                    msgProto.sentAt.seconds,
                    msgProto.sentAt.nanos.toLong()
                ),
                readBy = msgProto.readByList,
                type = MessageType.valueOf(msgProto.type.name)
            )

            emit(message)
        }
    }

    suspend fun createGroupChat(
        title: String,
        userIds: List<Long>
    ): Result<Chat> = withContext(Dispatchers.IO) {
        try {
            val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

            val request = messages_api.MessagesApi.CreateGroupChatRequest.newBuilder()
                .setTitle(title)
                .addAllUserIds(userIds)
                .build()

            val response = stub.createGroupChat(request)

            val chat = Chat(
                id = response.chat.id,
                title = response.chat.title,
                pictureUrl = response.chat.picture.takeIf { it.isNotBlank() },
                isGroupChat = response.chat.isGroupChat,
                lastMessage = null,
                members = response.chat.membersList.map { member ->
                    ChatMember(
                        userId = member.userId,
                        joinedAt = Instant.ofEpochSecond(
                            member.joinedAt.seconds,
                            member.joinedAt.nanos.toLong()
                        )
                    )
                },
                unreadCount = 0
            )

            Result.success(chat)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun getChatMembers(
        chatId: String,
        offset: Int = 0,
        limit: Int = 50
    ): Result<List<ChatMember>> = withContext(Dispatchers.IO) {
        try {
            val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

            val request = messages_api.MessagesApi.ListChatMembersRequest.newBuilder()
                .setChatId(chatId)
                .setPage(
                    shared.Shared.PageRequest.newBuilder()
                        .setOffset(offset)
                        .setSize(limit)
                        .build()
                )
                .build()

            val response = stub.listChatMembers(request)

            val members = response.membersList.map { member ->
                ChatMember(
                    userId = member.userId,
                    joinedAt = Instant.ofEpochSecond(
                        member.joinedAt.seconds,
                        member.joinedAt.nanos.toLong()
                    )
                )
            }

            Result.success(members)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun kickUser(chatId: String, userId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = messages_api.MessagesApiGrpc.newBlockingStub(messagesChannel)

            val request = messages_api.MessagesApi.KickUserRequest.newBuilder()
                .setChatId(chatId)
                .setUserId(userId)
                .build()

            stub.kickUser(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
