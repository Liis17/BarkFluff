#pragma once

#include <QString>
#include <QDateTime>
#include <QList>
#include <QVariant>
#include "ChatMember.h"

namespace BarkFluff {

// Forward declaration
class Message;

/**
 * @brief Модель чата
 */
class Chat {
public:
    QString id;                     // Идентификатор чата
    QString title;                  // Название чата
    QString picture;                // URL картинки чата
    QString picturePreview;         // URL превью картинки чата
    bool isGroupChat = false;       // Признак группового чата
    qint64 peerId = 0;              // ID пользователя-собеседника (для личных чатов)
    qint64 countUnread = 0;         // Количество непрочитанных сообщений
    qint64 firstUnreadMessageId = 0;// ID первого непрочитанного сообщения
    QList<ChatMember> members;      // Участники (для личных чатов)
    
    // Last message info (simplified)
    QString lastMessageText;
    qint64 lastMessageSenderId = 0;
    QDateTime lastMessageTime;
    
    Chat() = default;
    explicit Chat(const QString& id) : id(id) {}
    
    // Get display name for chat
    QString displayName() const;
    
    // Get avatar initials
    QString avatarInitials() const;
    
    // Get last message preview text
    QString lastMessagePreview(int maxLength = 30) const;
    
    // Serialization for caching
    QVariant toVariant() const;
    static Chat fromVariant(const QVariant& variant);
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::Chat)