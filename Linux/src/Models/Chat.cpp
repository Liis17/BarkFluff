#include "Chat.h"

namespace BarkFluff {

QString Chat::displayName() const {
    if (!title.isEmpty()) {
        return title;
    }
    
    // For personal chats, use member name
    if (!isGroupChat && !members.isEmpty()) {
        return members.first().displayName();
    }
    
    return QObject::tr("Чат");
}

QString Chat::avatarInitials() const {
    QString name = displayName();
    if (name.isEmpty()) {
        return "?";
    }
    
    // For group chats - first letter
    if (isGroupChat) {
        return name.left(1).toUpper();
    }
    
    // For personal chats - first letters of first and last name
    QStringList parts = name.split(' ', Qt::SkipEmptyParts);
    if (parts.size() >= 2) {
        return QString("%1%2").arg(parts[0].left(1), parts[1].left(1)).toUpper();
    }
    
    return name.left(2).toUpper();
}

QString Chat::lastMessagePreview(int maxLength) const {
    if (!lastMessageText.isEmpty()) {
        QString txt = lastMessageText;
        txt.replace('\n', ' ');
        if (txt.length() > maxLength) {
            return txt.left(maxLength) + "...";
        }
        return txt;
    }
    return "";
}

QVariant Chat::toVariant() const {
    QVariantMap map;
    map["id"] = id;
    map["title"] = title;
    map["picture"] = picture;
    map["picturePreview"] = picturePreview;
    map["isGroupChat"] = isGroupChat;
    map["peerId"] = peerId;
    map["countUnread"] = countUnread;
    map["firstUnreadMessageId"] = firstUnreadMessageId;
    map["lastMessageText"] = lastMessageText;
    map["lastMessageSenderId"] = lastMessageSenderId;
    map["lastMessageTime"] = lastMessageTime.toMSecsSinceEpoch();
    
    QVariantList membersList;
    for (const auto& member : members) {
        membersList.append(member.toVariant());
    }
    map["members"] = membersList;
    
    return map;
}

Chat Chat::fromVariant(const QVariant& variant) {
    Chat chat;
    QVariantMap map = variant.toMap();
    chat.id = map["id"].toString();
    chat.title = map["title"].toString();
    chat.picture = map["picture"].toString();
    chat.picturePreview = map["picturePreview"].toString();
    chat.isGroupChat = map["isGroupChat"].toBool();
    chat.peerId = map["peerId"].toLongLong();
    chat.countUnread = map["countUnread"].toLongLong();
    chat.firstUnreadMessageId = map["firstUnreadMessageId"].toLongLong();
    chat.lastMessageText = map["lastMessageText"].toString();
    chat.lastMessageSenderId = map["lastMessageSenderId"].toLongLong();
    chat.lastMessageTime = QDateTime::fromMSecsSinceEpoch(map["lastMessageTime"].toLongLong());
    
    QVariantList membersList = map["members"].toList();
    for (const auto& memberVar : membersList) {
        chat.members.append(ChatMember::fromVariant(memberVar));
    }
    
    return chat;
}

} // namespace BarkFluff