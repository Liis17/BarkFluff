/**
 * @file SharedMediaItem.h
 * @brief Модель элемента общего медиа в чате
 */

#pragma once

#include <QString>
#include <QDateTime>
#include <QList>
#include "Models/MessageContent.h"

namespace BarkFluff {

/**
 * @brief Фильтр для общих медиа
 */
enum class SharedMediaFilter {
    Media,      // Фото, видео, GIF
    Documents   // Документы
};

/**
 * @brief Элемент общего медиа (из вложений сообщений)
 */
struct SharedMediaItem {
    qint64 id = 0;                          // ID вложения (MessageAttachment.id)
    qint64 messageId = 0;                   // ID сообщения-источника
    MessageAttachmentType type = MessageAttachmentType::Unknown;
    QString fileId;                         // ID файла в Files сервисе
    QString previewUrl;                     // Прямая ссылка на превью (если есть)
    QString previewFileId;                  // ID файла превью
    QString fileName;                       // Оригинальное имя файла
    qint64 fileSize = 0;                    // Размер в байтах
    QDateTime sentAt;                       // Дата отправки сообщения
    qint64 senderId = 0;                    // ID отправителя
    
    SharedMediaItem() = default;
    
    /**
     * @brief Форматированный размер файла
     */
    QString formattedSize() const {
        if (fileSize < 1024) {
            return QString("%1 Б").arg(fileSize);
        } else if (fileSize < 1024 * 1024) {
            return QString("%1 КБ").arg(fileSize / 1024.0, 0, 'f', 1);
        } else if (fileSize < 1024 * 1024 * 1024) {
            return QString("%1 МБ").arg(fileSize / (1024.0 * 1024.0), 0, 'f', 1);
        } else {
            return QString("%1 ГБ").arg(fileSize / (1024.0 * 1024.0 * 1024.0), 0, 'f', 1);
        }
    }
    
    /**
     * @brief Проверка, является ли элемент медиа (изображение, видео, GIF)
     */
    bool isMedia() const {
        return type == MessageAttachmentType::Image ||
               type == MessageAttachmentType::Video ||
               type == MessageAttachmentType::Gif;
    }
    
    /**
     * @brief Проверка, является ли элемент документом
     */
    bool isDocument() const {
        return type == MessageAttachmentType::Document;
    }
};

/**
 * @brief Результат пагинации для общих медиа
 */
struct PagedSharedMediaResult {
    QList<SharedMediaItem> items;
    qint32 totalCount = 0;
    bool hasMore = false;
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::SharedMediaItem)
Q_DECLARE_METATYPE(BarkFluff::SharedMediaFilter)