#pragma once

#include <QString>
#include <QDateTime>
#include <QVariant>

namespace BarkFluff {

/**
 * @brief Модель участника чата
 */
class ChatMember {
public:
    qint64 userId = 0;          // Идентификатор пользователя
    QDateTime joinedAt;         // Когда присоединился
    
    // Additional info (from DetailedChatMemberInfo)
    QString firstName;
    QString lastName;
    
    ChatMember() = default;
    explicit ChatMember(qint64 uid) : userId(uid) {}
    
    QString displayName() const {
        if (!firstName.isEmpty() || !lastName.isEmpty()) {
            return QString("%1 %2").arg(firstName, lastName).trimmed();
        }
        return QString("Пользователь %1").arg(userId);
    }
    
    // Serialization
    QVariant toVariant() const {
        QVariantMap map;
        map["userId"] = userId;
        map["joinedAt"] = joinedAt.toMSecsSinceEpoch();
        map["firstName"] = firstName;
        map["lastName"] = lastName;
        return map;
    }
    
    static ChatMember fromVariant(const QVariant& variant) {
        ChatMember member;
        QVariantMap map = variant.toMap();
        member.userId = map["userId"].toLongLong();
        member.joinedAt = QDateTime::fromMSecsSinceEpoch(map["joinedAt"].toLongLong());
        member.firstName = map["firstName"].toString();
        member.lastName = map["lastName"].toString();
        return member;
    }
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::ChatMember)