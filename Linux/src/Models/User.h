#pragma once

#include <QString>
#include <QDateTime>
#include <QVariant>
#include <QList>

namespace BarkFluff {

/**
 * @brief Модель баджа (тип баджа)
 */
struct Badge {
    qint32 id = 0;
    QString name;
    QString description;
    QString imageUrl;
    QDateTime createdDate;
    bool isActive = true;
    
    Badge() = default;
    
    QVariant toVariant() const {
        QVariantMap map;
        map["id"] = id;
        map["name"] = name;
        map["description"] = description;
        map["imageUrl"] = imageUrl;
        map["createdDate"] = createdDate.toMSecsSinceEpoch();
        map["isActive"] = isActive;
        return map;
    }
    
    static Badge fromVariant(const QVariant& variant) {
        Badge badge;
        QVariantMap map = variant.toMap();
        badge.id = map["id"].toInt();
        badge.name = map["name"].toString();
        badge.description = map["description"].toString();
        badge.imageUrl = map["imageUrl"].toString();
        badge.createdDate = QDateTime::fromMSecsSinceEpoch(map["createdDate"].toLongLong());
        badge.isActive = map["isActive"].toBool();
        return badge;
    }
};

/**
 * @brief Модель баджа пользователя с приоритетом
 */
struct UserBadge {
    Badge badge;
    qint32 priority = 1000;
    QDateTime assignedDate;
    
    UserBadge() = default;
    
    // Для сортировки по приоритету (меньше = выше приоритет)
    bool operator<(const UserBadge& other) const {
        return priority < other.priority;
    }
    
    QVariant toVariant() const {
        QVariantMap map;
        map["badge"] = badge.toVariant();
        map["priority"] = priority;
        map["assignedDate"] = assignedDate.toMSecsSinceEpoch();
        return map;
    }
    
    static UserBadge fromVariant(const QVariant& variant) {
        UserBadge userBadge;
        QVariantMap map = variant.toMap();
        userBadge.badge = Badge::fromVariant(map["badge"]);
        userBadge.priority = map["priority"].toInt();
        userBadge.assignedDate = QDateTime::fromMSecsSinceEpoch(map["assignedDate"].toLongLong());
        return userBadge;
    }
};

/**
 * @brief Модель пользователя
 */
class User {
public:
    qint64 id = 0;
    QString firstName;
    QString lastName;
    QString username;
    QDateTime registrationDate;
    QString profilePicture;        // URL полной аватарки
    QString profilePicturePreview; // URL превью аватарки
    QString bio;
    QList<UserBadge> badges;       // Бейджи пользователя
    qint32 storageLimitGb = 0;     // Лимит хранилища в ГБ
    
    User() = default;
    explicit User(qint64 uid) : id(uid) {}
    
    QString displayName() const {
        if (!firstName.isEmpty() || !lastName.isEmpty()) {
            return QString("%1 %2").arg(firstName, lastName).trimmed();
        }
        if (!username.isEmpty()) {
            return username;
        }
        return QString("Пользователь %1").arg(id);
    }
    
    QString avatarInitials() const {
        if (!firstName.isEmpty()) {
            return firstName.left(1).toUpper();
        }
        if (!username.isEmpty()) {
            return username.left(1).toUpper();
        }
        return "?";
    }
    
    // Serialization
    QVariant toVariant() const {
        QVariantMap map;
        map["id"] = id;
        map["firstName"] = firstName;
        map["lastName"] = lastName;
        map["username"] = username;
        map["registrationDate"] = registrationDate.toMSecsSinceEpoch();
        map["profilePicture"] = profilePicture;
        map["profilePicturePreview"] = profilePicturePreview;
        map["bio"] = bio;
        return map;
    }
    
    static User fromVariant(const QVariant& variant) {
        User user;
        QVariantMap map = variant.toMap();
        user.id = map["id"].toLongLong();
        user.firstName = map["firstName"].toString();
        user.lastName = map["lastName"].toString();
        user.username = map["username"].toString();
        user.registrationDate = QDateTime::fromMSecsSinceEpoch(map["registrationDate"].toLongLong());
        user.profilePicture = map["profilePicture"].toString();
        user.profilePicturePreview = map["profilePicturePreview"].toString();
        user.bio = map["bio"].toString();
        return user;
    }
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::User)