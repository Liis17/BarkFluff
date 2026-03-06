#pragma once

#include <QString>
#include <QList>
#include <QVariant>

namespace BarkFluff {

/**
 * @brief Тип вложения сообщения
 */
enum class MessageAttachmentType {
    Unknown = 0,
    Image = 1,
    Video = 2,
    Gif = 3,
    Document = 4
};

/**
 * @brief Модель вложения сообщения
 */
class MessageAttachment {
public:
    qint64 id = 0;                      // Идентификатор вложения
    MessageAttachmentType type = MessageAttachmentType::Unknown;
    QString fileId;                     // Идентификатор файла
    QString previewUrl;                 // Ссылка на превью (может быть пустой)
    qint64 attachmentSize = 0;          // Размер файла в байтах
    QString previewFileId;              // Идентификатор файла превью
    QString fileName;                   // Оригинальное имя файла
    
    MessageAttachment() = default;
    
    // Get human-readable file size
    QString formattedSize() const {
        if (attachmentSize < 1024) {
            return QString("%1 Б").arg(attachmentSize);
        } else if (attachmentSize < 1024 * 1024) {
            return QString("%1 КБ").arg(attachmentSize / 1024.0, 0, 'f', 1);
        } else if (attachmentSize < 1024 * 1024 * 1024) {
            return QString("%1 МБ").arg(attachmentSize / (1024.0 * 1024.0), 0, 'f', 1);
        } else {
            return QString("%1 ГБ").arg(attachmentSize / (1024.0 * 1024.0 * 1024.0), 0, 'f', 1);
        }
    }
    
    // Serialization
    QVariant toVariant() const {
        QVariantMap map;
        map["id"] = id;
        map["type"] = static_cast<int>(type);
        map["fileId"] = fileId;
        map["previewUrl"] = previewUrl;
        map["attachmentSize"] = attachmentSize;
        map["previewFileId"] = previewFileId;
        map["fileName"] = fileName;
        return map;
    }
    
    static MessageAttachment fromVariant(const QVariant& variant) {
        MessageAttachment att;
        QVariantMap map = variant.toMap();
        att.id = map["id"].toLongLong();
        att.type = static_cast<MessageAttachmentType>(map["type"].toInt());
        att.fileId = map["fileId"].toString();
        att.previewUrl = map["previewUrl"].toString();
        att.attachmentSize = map["attachmentSize"].toLongLong();
        att.previewFileId = map["previewFileId"].toString();
        att.fileName = map["fileName"].toString();
        return att;
    }
};

/**
 * @brief Тип содержимого сообщения
 */
enum class MessageContentType {
    Unknown = 0,
    Generic = 1,    // Обычное сообщение
    System = 2      // Системное сообщение
};

/**
 * @brief Содержимое сообщения
 */
class MessageContent {
public:
    QString text;                           // Текст сообщения
    QList<MessageAttachment> attachments;   // Вложения
    MessageContentType type = MessageContentType::Generic;
    
    MessageContent() = default;
    
    bool hasText() const { return !text.isEmpty(); }
    bool hasAttachments() const { return !attachments.isEmpty(); }
    bool isEmpty() const { return !hasText() && !hasAttachments(); }
    
    // Get preview text for chat list
    QString previewText(int maxLength = 50) const {
        if (hasText()) {
            QString txt = text;
            txt.replace('\n', ' ');
            if (txt.length() > maxLength) {
                return txt.left(maxLength) + "...";
            }
            return txt;
        }
        
        if (hasAttachments()) {
            const auto& att = attachments.first();
            switch (att.type) {
            case MessageAttachmentType::Image:
                return QObject::tr("🖼 Изображение");
            case MessageAttachmentType::Video:
                return QObject::tr("🎬 Видео");
            case MessageAttachmentType::Gif:
                return QObject::tr("GIF");
            case MessageAttachmentType::Document:
                return QObject::tr("📎 Документ");
            default:
                return QObject::tr("📎 Вложение");
            }
        }
        
        return "";
    }
    
    // Serialization
    QVariant toVariant() const {
        QVariantMap map;
        map["text"] = text;
        map["type"] = static_cast<int>(type);
        
        QVariantList attList;
        for (const auto& att : attachments) {
            attList.append(att.toVariant());
        }
        map["attachments"] = attList;
        return map;
    }
    
    static MessageContent fromVariant(const QVariant& variant) {
        MessageContent content;
        QVariantMap map = variant.toMap();
        content.text = map["text"].toString();
        content.type = static_cast<MessageContentType>(map["type"].toInt());
        
        QVariantList attList = map["attachments"].toList();
        for (const auto& attVar : attList) {
            content.attachments.append(MessageAttachment::fromVariant(attVar));
        }
        return content;
    }
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::MessageAttachment)
Q_DECLARE_METATYPE(BarkFluff::MessageContent)