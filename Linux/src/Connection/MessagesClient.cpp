/**
 * @file MessagesClient.cpp
 * @brief Реализация клиента для работы с сообщениями
 */

#include "MessagesClient.h"
#include "Utils/ErrorHandler.h"

#include <QDateTime>
#include <QDebug>

namespace BarkFluff {

MessagesClient::MessagesClient(const ServiceInfo& service)
    : GrpcClient(service.endpoint.host, service.endpoint.port, service.tlsEnabled)
{
    stub_ = barkfluff::messages::MessagesApi::NewStub(channel_);
}

PagedChatsResult MessagesClient::listChats(qint32 offset, qint32 size, const QString& accessToken) {
    PagedChatsResult result;
    
    auto context = createContext(accessToken);
    barkfluff::messages::ListChatsRequest request;
    
    auto* pagination = request.mutable_pagination();
    pagination->set_offset(offset);
    pagination->set_size(size);
    
    barkfluff::messages::ListChatsResponse response;
    grpc::Status status = stub_->ListChats(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::listChats error:" << QString::fromStdString(status.error_message());
        return result;
    }
    
    result.totalCount = response.total_count();
    result.hasMore = (offset + size) < result.totalCount;
    
    for (const auto& protoChat : response.chats()) {
        result.chats.append(convertChat(protoChat));
    }
    
    return result;
}

PagedMessagesResult MessagesClient::listMessages(
    const QString& chatId,
    qint64 fromMessageId,
    qint32 offsetBefore,
    qint32 offsetAfter,
    const QString& accessToken
) {
    PagedMessagesResult result;
    
    auto context = createContext(accessToken);
    barkfluff::messages::ListMessagesRequest request;
    
    request.set_chat_id(chatId.toStdString());
    request.set_from_message_id(fromMessageId);
    request.set_offset_before(offsetBefore);
    request.set_offset_after(offsetAfter);
    
    barkfluff::messages::ListMessagesResponse response;
    grpc::Status status = stub_->ListMessages(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::listMessages error:" << QString::fromStdString(status.error_message());
        return result;
    }
    
    // Check if there are more messages
    result.hasMore = (response.messages_size() >= offsetAfter);
    
    for (const auto& protoMsg : response.messages()) {
        result.messages.append(convertMessage(protoMsg));
    }
    
    return result;
}

PagedChatMembersResult MessagesClient::listChatMembers(
    const QString& chatId,
    qint32 offset,
    qint32 size,
    const QString& accessToken
) {
    PagedChatMembersResult result;
    
    auto context = createContext(accessToken);
    barkfluff::messages::ListChatMembersRequest request;
    
    request.set_chat_id(chatId.toStdString());
    auto* pagination = request.mutable_pagination();
    pagination->set_offset(offset);
    pagination->set_size(size);
    
    barkfluff::messages::ListChatMembersResponse response;
    grpc::Status status = stub_->ListChatMembers(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::listChatMembers error:" << QString::fromStdString(status.error_message());
        return result;
    }
    
    result.totalCount = response.total_count();
    result.hasMore = (offset + size) < result.totalCount;
    
    for (const auto& protoMember : response.chat_members()) {
        ChatMember member = convertChatMember(protoMember.general_info());
        member.firstName = QString::fromStdString(protoMember.first_name());
        member.lastName = QString::fromStdString(protoMember.last_name());
        result.members.append(member);
    }
    
    return result;
}

Message MessagesClient::sendMessage(
    const QString& chatId,
    const QString& text,
    const QStringList& fileIds,
    const QString& accessToken
) {
    auto context = createContext(accessToken);
    barkfluff::messages::SendMessageRequest request;
    
    request.set_chat_id(chatId.toStdString());
    auto* message = request.mutable_message();
    message->set_text(text.toStdString());
    for (const auto& fileId : fileIds) {
        message->add_files_ids(fileId.toStdString());
    }
    
    barkfluff::messages::SendMessageResponse response;
    grpc::Status status = stub_->SendMessage(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::sendMessage error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(status.error_message());
    }
    
    return convertMessage(response.message());
}

Message MessagesClient::sendMessageToUser(
    qint64 userId,
    const QString& text,
    const QStringList& fileIds,
    const QString& accessToken
) {
    auto context = createContext(accessToken);
    barkfluff::messages::SendMessageRequest request;
    
    request.set_user_id(userId);
    auto* message = request.mutable_message();
    message->set_text(text.toStdString());
    for (const auto& fileId : fileIds) {
        message->add_files_ids(fileId.toStdString());
    }
    
    barkfluff::messages::SendMessageResponse response;
    grpc::Status status = stub_->SendMessage(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::sendMessageToUser error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(status.error_message());
    }
    
    return convertMessage(response.message());
}

void MessagesClient::markAsRead(const QList<qint64>& messageIds, const QString& accessToken) {
    auto context = createContext(accessToken);
    barkfluff::messages::MarkAsReadRequest request;
    
    for (auto msgId : messageIds) {
        request.add_message_ids(msgId);
    }
    
    barkfluff::messages::MarkAsReadResponse response;
    grpc::Status status = stub_->MarkAsRead(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::markAsRead error:" << QString::fromStdString(status.error_message());
    }
}

QString MessagesClient::getPersonChatId(qint64 userId, const QString& accessToken) {
    auto context = createContext(accessToken);
    barkfluff::messages::GetPersonChatIdRequest request;
    request.set_user_id(userId);
    
    barkfluff::messages::GetPersonChatIdResponse response;
    grpc::Status status = stub_->GetPersonChatId(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::getPersonChatId error:" << QString::fromStdString(status.error_message());
        return "";
    }
    
    return QString::fromStdString(response.chat_id());
}

Chat MessagesClient::getChatInfo(const QString& chatId, const QString& accessToken) {
    auto context = createContext(accessToken);
    barkfluff::messages::GetChatInfoRequest request;
    request.set_chat_id(chatId.toStdString());
    
    barkfluff::messages::GetChatInfoResponse response;
    grpc::Status status = stub_->GetChatInfo(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::getChatInfo error:" << QString::fromStdString(status.error_message());
        return Chat();
    }
    
    Chat chat;
    chat.id = chatId;
    chat.title = QString::fromStdString(response.title());
    chat.picture = QString::fromStdString(response.picture());
    chat.isGroupChat = response.is_group_chat();
    chat.countUnread = response.count_unread();
    chat.firstUnreadMessageId = response.first_unread_message_id();
    
    return chat;
}

Chat MessagesClient::createGroupChat(
    const QList<qint64>& userIds,
    const QString& title,
    const QString& pictureFileId,
    const QString& accessToken
) {
    auto context = createContext(accessToken);
    barkfluff::messages::CreateGroupChatRequest request;
    
    for (auto uid : userIds) {
        request.add_user_ids(uid);
    }
    request.set_title(title.toStdString());
    if (!pictureFileId.isEmpty()) {
        request.set_picture_file_id(pictureFileId.toStdString());
    }
    
    barkfluff::messages::CreateGroupChatResponse response;
    grpc::Status status = stub_->CreateGroupChat(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::createGroupChat error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(status.error_message());
    }
    
    return convertChat(response.created_chat());
}

void MessagesClient::kickUser(const QString& chatId, qint64 userId, const QString& accessToken) {
    auto context = createContext(accessToken);
    barkfluff::messages::KickUserRequest request;
    
    request.set_chat_id(chatId.toStdString());
    request.set_user_id(userId);
    
    barkfluff::messages::KickUserResponse response;
    grpc::Status status = stub_->KickUser(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::kickUser error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(status.error_message());
    }
}

PagedSharedMediaResult MessagesClient::listChatAttachments(
    const QString& chatId,
    qint32 offset,
    qint32 size,
    SharedMediaFilter filter,
    const QString& accessToken
) {
    PagedSharedMediaResult result;
    
    auto context = createContext(accessToken);
    barkfluff::messages::ListChatAttachmentsRequest request;
    
    request.set_chat_id(chatId.toStdString());
    auto* pagination = request.mutable_pagination();
    pagination->set_offset(offset);
    pagination->set_size(size);
    
    // Set filter type: for Media we pass 0 (all) and filter on client,
    // for Documents we pass DOCUMENT type
    if (filter == SharedMediaFilter::Documents) {
        request.set_attachment_type(barkfluff::shared::DOCUMENT);
    }
    request.set_sort_descending(true);
    
    barkfluff::messages::ListChatAttachmentsResponse response;
    grpc::Status status = stub_->ListChatAttachments(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "MessagesClient::listChatAttachments error:" << QString::fromStdString(status.error_message());
        return result;
    }
    
    result.totalCount = response.total_count();
    result.hasMore = (offset + size) < result.totalCount;
    
    for (const auto& protoAtt : response.attachments()) {
        SharedMediaItem item;
        item.messageId = protoAtt.message_id();
        item.id = protoAtt.attachment_id();
        
        // Convert attachment
        const auto& att = protoAtt.attachment();
        item.type = static_cast<MessageAttachmentType>(att.type());
        item.fileId = QString::fromStdString(att.file_id());
        item.previewUrl = QString::fromStdString(att.preview_url());
        item.previewFileId = QString::fromStdString(att.preview_file_id());
        item.fileName = QString::fromStdString(att.file_name());
        item.fileSize = att.attachment_size();
        
        // Convert timestamp
        const auto& timestamp = protoAtt.sent_at();
        qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
        item.sentAt = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
        
        item.senderId = protoAtt.sender_id();
        
        // For Media filter, skip documents on client side
        if (filter == SharedMediaFilter::Media && !item.isMedia()) {
            continue;
        }
        
        result.items.append(item);
    }
    
    return result;
}

// Private conversion methods

Chat MessagesClient::convertChat(const barkfluff::messages::Chat& protoChat) {
    Chat chat;
    chat.id = QString::fromStdString(protoChat.id());
    chat.title = QString::fromStdString(protoChat.title());
    chat.picture = QString::fromStdString(protoChat.picture());
    // picturePreview используем тот же picture (сервер не отдаёт отдельное preview)
    chat.picturePreview = chat.picture;
    chat.isGroupChat = protoChat.is_group_chat();
    chat.countUnread = protoChat.count_unread();
    chat.firstUnreadMessageId = protoChat.first_unread_message_id();
    
    // Convert members (only for personal chats)
    for (const auto& protoMember : protoChat.members()) {
        chat.members.append(convertChatMember(protoMember));
    }
    
    // Для личных чатов извлекаем peerId из members (ID собеседника)
    if (!chat.isGroupChat && chat.members.size() > 0) {
        // peerId будет установлен позже при загрузке списка чатов
        // когда мы знаем ID текущего пользователя
    }
    
    // Convert last message
    if (protoChat.has_last_message()) {
        auto lastMsg = convertMessage(protoChat.last_message());
        chat.lastMessageText = lastMsg.content.text;
        chat.lastMessageSenderId = lastMsg.senderId;
        chat.lastMessageTime = lastMsg.sentAt;
    }
    
    return chat;
}

Message MessagesClient::convertMessage(const barkfluff::shared::Message& protoMsg) {
    Message msg;
    msg.id = protoMsg.id();
    msg.senderId = protoMsg.sender_id();
    
    // Convert timestamp from protobuf
    const auto& timestamp = protoMsg.sent_at();
    qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
    msg.sentAt = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
    
    // Convert read_by
    for (auto uid : protoMsg.read_by()) {
        msg.readBy.append(uid);
    }
    
    // Convert content
    const auto& protoContent = protoMsg.content();
    msg.content.text = QString::fromStdString(protoContent.text());
    
    for (const auto& protoAtt : protoContent.attachments()) {
        MessageAttachment att;
        att.id = protoAtt.id();
        att.type = static_cast<MessageAttachmentType>(protoAtt.type());
        att.fileId = QString::fromStdString(protoAtt.file_id());
        att.previewUrl = QString::fromStdString(protoAtt.preview_url());
        att.attachmentSize = protoAtt.attachment_size();
        att.previewFileId = QString::fromStdString(protoAtt.preview_file_id());
        att.fileName = QString::fromStdString(protoAtt.file_name());
        msg.content.attachments.append(att);
    }
    
    // Convert message type
    msg.content.type = static_cast<MessageContentType>(protoMsg.type());
    
    return msg;
}

ChatMember MessagesClient::convertChatMember(const barkfluff::messages::ChatMember& protoMember) {
    ChatMember member;
    member.userId = protoMember.user_id();
    
    // Convert timestamp from protobuf
    const auto& timestamp = protoMember.joined_at();
    qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
    member.joinedAt = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
    
    return member;
}

} // namespace BarkFluff