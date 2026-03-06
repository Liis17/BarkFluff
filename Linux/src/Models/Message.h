#pragma once

#include <QString>
#include <QDateTime>
#include <QList>
#include <QVariant>
#include <QUuid>
#include "MessageContent.h"

namespace BarkFluff {

/**
 * @brief Состояние отправки сообщения
 */
enum class SendingState {
    None,       ///< Обычное сообщение (не pending)
    Sending,    ///< Отправляется на сервер
    Failed      ///< Ошибка отправки
};

/**
 * @brief Модель сообщения
 */
class Message {
public:
    qint64 id = 0;                  // Идентификатор сообщения (отрицательный для pending)
    QString localID;                // UUID для pending-сообщений (идентификатор на клиенте)
    QString chatId;                 // Идентификатор чата
    qint64 senderId = 0;            // Идентификатор отправителя
    QString senderName;             // Имя отправителя (может быть пустым)
    QList<qint64> readBy;           // Кто прочитал сообщение
    QDateTime sentAt;               // Время отправки
    MessageContent content;         // Содержимое
    
    // Состояние отправки
    SendingState sendingState = SendingState::None;
    QString sendError;              // Текст ошибки при Failed
    double uploadProgress = 1.0;    // 0.0 - 1.0 прогресс загрузки вложений
    
    // Deprecated (для совместимости)
    bool isPending = false;         
    bool sendFailed = false;        
    
    Message() = default;
    Message(qint64 id, const QString& chatId) : id(id), chatId(chatId) {}
    
    bool isOwn(qint64 currentUserId) const {
        return senderId == currentUserId;
    }
    
    bool isRead(qint64 currentUserId) const {
        // Для собственных сообщений - проверяем что кто-то другой прочитал
        if (senderId == currentUserId) {
            return readBy.size() > 1 || (readBy.size() == 1 && !readBy.contains(senderId));
        }
        // Для чужих - проверяем что текущий пользователь прочитал
        return readBy.contains(currentUserId);
    }
    
    bool isSystem() const {
        return content.type == MessageContentType::System;
    }
    
    // Serialization
    QVariant toVariant() const {
        QVariantMap map;
        map["id"] = id;
        map["chatId"] = chatId;
        map["senderId"] = senderId;
        
        QVariantList readByList;
        for (auto uid : readBy) {
            readByList.append(uid);
        }
        map["readBy"] = readByList;
        
        map["sentAt"] = sentAt.toMSecsSinceEpoch();
        map["content"] = content.toVariant();
        map["isPending"] = isPending;
        map["sendFailed"] = sendFailed;
        return map;
    }
    
    static Message fromVariant(const QVariant& variant) {
        Message msg;
        QVariantMap map = variant.toMap();
        msg.id = map["id"].toLongLong();
        msg.chatId = map["chatId"].toString();
        msg.senderId = map["senderId"].toLongLong();
        
        QVariantList readByList = map["readBy"].toList();
        for (const auto& uid : readByList) {
            msg.readBy.append(uid.toLongLong());
        }
        
        msg.sentAt = QDateTime::fromMSecsSinceEpoch(map["sentAt"].toLongLong());
        msg.content = MessageContent::fromVariant(map["content"]);
        msg.isPending = map["isPending"].toBool();
        msg.sendFailed = map["sendFailed"].toBool();
        return msg;
    }
    
    // Helper methods for sending state
    bool isSending() const {
        return sendingState == SendingState::Sending || isPending;
    }
    
    bool hasSendError() const {
        return sendingState == SendingState::Failed || sendFailed;
    }
    
    // Create pending message for optimistic UI
    static Message createPending(const QString& chatId, qint64 senderId, const QString& text) {
        Message msg;
        msg.localID = QUuid::createUuid().toString(QUuid::WithoutBraces);
        msg.id = -QDateTime::currentMSecsSinceEpoch(); // Negative ID for pending
        msg.chatId = chatId;
        msg.senderId = senderId;
        msg.sentAt = QDateTime::currentDateTime();
        msg.content.text = text;
        msg.readBy.append(senderId);
        msg.sendingState = SendingState::Sending;
        msg.uploadProgress = 0.0;
        // Для совместимости
        msg.isPending = true;
        return msg;
    }
    
    // Create pending message with attachments
    static Message createPendingWithAttachments(
        const QString& chatId, 
        qint64 senderId, 
        const QString& text,
        const QList<MessageAttachment>& attachments
    ) {
        Message msg = createPending(chatId, senderId, text);
        msg.content.attachments = attachments;
        msg.content.type = MessageContentType::Generic;
        return msg;
    }
    
    // Mark as sent (replace pending with confirmed)
    void markAsSent(qint64 confirmedId) {
        id = confirmedId;
        sendingState = SendingState::None;
        uploadProgress = 1.0;
        isPending = false;
        localID.clear();
    }
    
    // Mark as failed
    void markAsFailed(const QString& error) {
        sendingState = SendingState::Failed;
        sendError = error;
        uploadProgress = 1.0;
        isPending = false;
        sendFailed = true;
    }
    
    // Get unique identifier (localID for pending, id for confirmed)
    QString uniqueId() const {
        if (!localID.isEmpty() && id < 0) {
            return "local-" + localID;
        }
        return "msg-" + QString::number(id);
    }
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::Message)