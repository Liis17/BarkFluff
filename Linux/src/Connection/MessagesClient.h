#pragma once

#include "GrpcClient.h"
#include "Models/ServerConfiguration.h"
#include "Models/Chat.h"
#include "Models/Message.h"
#include "Models/SharedMediaItem.h"
#include "messages_api.grpc.pb.h"

#include <functional>

namespace BarkFluff {

/**
 * @brief Результат пагинации для чатов
 */
struct PagedChatsResult {
    QList<Chat> chats;
    qint32 totalCount = 0;
    bool hasMore = false;
};

/**
 * @brief Результат пагинации для сообщений
 */
struct PagedMessagesResult {
    QList<Message> messages;
    bool hasMore = false;
};

/**
 * @brief Результат пагинации для участников чата
 */
struct PagedChatMembersResult {
    QList<ChatMember> members;
    qint32 totalCount = 0;
    bool hasMore = false;
};

/**
 * @brief gRPC клиент для Messages API
 */
class MessagesClient : public GrpcClient {
    std::unique_ptr<barkfluff::messages::MessagesApi::Stub> stub_;
    
public:
    MessagesClient(const ServiceInfo& service);
    
    // List chats with pagination
    PagedChatsResult listChats(qint32 offset = 0, qint32 size = 30, const QString& accessToken = "");
    
    // List messages in chat with pagination
    PagedMessagesResult listMessages(
        const QString& chatId,
        qint64 fromMessageId = 0,
        qint32 offsetBefore = 0,
        qint32 offsetAfter = 50,
        const QString& accessToken = ""
    );
    
    // List chat members
    PagedChatMembersResult listChatMembers(
        const QString& chatId,
        qint32 offset = 0,
        qint32 size = 50,
        const QString& accessToken = ""
    );
    
    // Send text message to chat
    Message sendMessage(
        const QString& chatId,
        const QString& text,
        const QStringList& fileIds = {},
        const QString& accessToken = ""
    );
    
    // Send message to user (creates/gets personal chat)
    Message sendMessageToUser(
        qint64 userId,
        const QString& text,
        const QStringList& fileIds = {},
        const QString& accessToken = ""
    );
    
    // Mark messages as read
    void markAsRead(const QList<qint64>& messageIds, const QString& accessToken = "");
    
    // Get personal chat ID with user
    QString getPersonChatId(qint64 userId, const QString& accessToken = "");
    
    // Get chat info
    Chat getChatInfo(const QString& chatId, const QString& accessToken = "");
    
    // Create group chat
    Chat createGroupChat(
        const QList<qint64>& userIds,
        const QString& title,
        const QString& pictureFileId = "",
        const QString& accessToken = ""
    );
    
    // Kick user from group chat
    void kickUser(const QString& chatId, qint64 userId, const QString& accessToken = "");
    
    // List chat attachments (shared media)
    PagedSharedMediaResult listChatAttachments(
        const QString& chatId,
        qint32 offset = 0,
        qint32 size = 50,
        SharedMediaFilter filter = SharedMediaFilter::Media,
        const QString& accessToken = ""
    );

private:
    // Convert proto Chat to model Chat
    Chat convertChat(const barkfluff::messages::Chat& protoChat);
    
    // Convert proto Message to model Message
    Message convertMessage(const barkfluff::shared::Message& protoMsg);
    
    // Convert proto ChatMember to model ChatMember
    ChatMember convertChatMember(const barkfluff::messages::ChatMember& protoMember);
};

} // namespace BarkFluff