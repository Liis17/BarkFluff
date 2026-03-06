/**
 * @file SelectedAttachment.h
 * @brief Модель вложения, выбранного пользователем перед отправкой
 * 
 * Аналог SelectedAttachment.swift из macOS версии
 */

#pragma once

#include <QString>
#include <QUrl>
#include <QUuid>
#include <QByteArray>
#include <QFileInfo>
#include <QFile>
#include <QSize>
#include <QDebug>
#include <optional>

namespace BarkFluff {

/**
 * @brief Тип файла для загрузки на сервер (соответствует proto enum)
 */
enum class UploadFileType {
    Unknown = 0,
    UserAvatar = 1,
    MessageAttachmentImage = 2,
    MessageAttachmentVideo = 3,
    MessageAttachmentGif = 4,
    MessageAttachmentDocument = 5,
    ChatPicture = 6
};

/**
 * @brief Тип выбранного вложения
 */
enum class SelectedAttachmentType {
    FileUrl,    ///< Файл по URL (из file picker или drag & drop)
    ImageData   ///< Изображение из буфера обмена
};

/**
 * @brief Вложение, выбранное пользователем перед отправкой
 * 
 * Содержит информацию о файле и методы для определения типа загрузки
 */
class SelectedAttachment {
public:
    QUuid id;
    SelectedAttachmentType type = SelectedAttachmentType::FileUrl;
    
    // Для FileUrl
    QUrl url;
    bool forceAsDocument = false;  ///< Если true, отправить как документ без сжатия
    
    // Для ImageData
    QByteArray imageData;
    QString fileName;
    
    // Cache
    mutable std::optional<qint64> cachedSize_;
    
    SelectedAttachment() : id(QUuid::createUuid()) {}
    
    // Factory methods
    static SelectedAttachment fromUrl(const QUrl& url, bool forceAsDocument = false) {
        SelectedAttachment att;
        att.type = SelectedAttachmentType::FileUrl;
        att.url = url;
        att.forceAsDocument = forceAsDocument;
        att.fileName = url.fileName();
        return att;
    }
    
    static SelectedAttachment fromImageData(const QByteArray& data, const QString& name = "image.png") {
        SelectedAttachment att;
        att.type = SelectedAttachmentType::ImageData;
        att.imageData = data;
        att.fileName = name;
        return att;
    }
    
    // Properties
    
    /**
     * @brief Имя файла для отображения
     */
    QString displayName() const {
        return fileName;
    }
    
    /**
     * @brief Получить локальный путь из URL
     * @return Локальный путь или пустая строка при ошибке
     */
    QString toLocalPath() const {
        if (type != SelectedAttachmentType::FileUrl) {
            return QString();
        }
        
        QString localPath;
        
        // Способ 1: Стандартный toLocalFile()
        if (url.isLocalFile()) {
            localPath = url.toLocalFile();
        }
        
        // Способ 2: Для file:// схем
        if (localPath.isEmpty() && url.scheme() == "file") {
            localPath = url.path();
            // На Linux путь может начинаться с пустого сегмента
            if (localPath.startsWith("//")) {
                localPath = localPath.mid(1);
            }
        }
        
        // Способ 3: Fallback на path() если схема пустая
        if (localPath.isEmpty() && url.scheme().isEmpty()) {
            localPath = url.path();
        }
        
        // Способ 4: toString() с флагом PreferLocalFile
        if (localPath.isEmpty()) {
            localPath = url.toString(QUrl::PreferLocalFile);
        }
        
        // Декодируем URL-кодированные символы
        if (!localPath.isEmpty()) {
            localPath = QUrl::fromPercentEncoding(localPath.toUtf8());
        }
        
        return localPath;
    }
    
    /**
     * @brief Расширение файла (lowercase)
     */
    QString fileExtension() const {
        if (type == SelectedAttachmentType::FileUrl) {
            QString localPath = toLocalPath();
            if (localPath.isEmpty()) {
                // Fallback: пробуем получить из URL напрямую
                return QFileInfo(url.path()).suffix().toLower();
            }
            return QFileInfo(localPath).suffix().toLower();
        }
        return "png"; // ImageData всегда PNG
    }
    
    /**
     * @brief Это медиа файл (изображение или видео)?
     */
    bool isMedia() const {
        QString ext = fileExtension();
        static const QStringList mediaExts = {
            "jpg", "jpeg", "png", "heic", "webp", "gif", "bmp",
            "mp4", "mov", "avi", "mkv", "webm"
        };
        return mediaExts.contains(ext);
    }
    
    /**
     * @brief Это видео?
     */
    bool isVideo() const {
        static const QStringList videoExts = {"mp4", "mov", "avi", "mkv", "webm"};
        return videoExts.contains(fileExtension());
    }
    
    /**
     * @brief Это изображение (не GIF)?
     */
    bool isImage() const {
        static const QStringList imageExts = {"jpg", "jpeg", "png", "heic", "webp", "bmp"};
        return imageExts.contains(fileExtension());
    }
    
    /**
     * @brief Это GIF?
     */
    bool isGif() const {
        return fileExtension() == "gif";
    }
    
    /**
     * @brief Размер файла в байтах
     */
    qint64 fileSize() const {
        if (cachedSize_.has_value()) {
            return cachedSize_.value();
        }
        
        if (type == SelectedAttachmentType::FileUrl) {
            QString localPath = toLocalPath();
            if (!localPath.isEmpty()) {
                QFileInfo info(localPath);
                cachedSize_ = info.size();
            } else {
                // Fallback - пробуем через url напрямую
                QFileInfo info(url.toLocalFile());
                cachedSize_ = info.size();
            }
        } else {
            cachedSize_ = imageData.size();
        }
        
        return cachedSize_.value_or(0);
    }
    
    /**
     * @brief Отформатированный размер для отображения
     */
    QString formattedSize() const {
        qint64 size = fileSize();
        if (size <= 0) return "";
        
        const qint64 KB = 1024;
        const qint64 MB = KB * 1024;
        const qint64 GB = MB * 1024;
        
        if (size >= GB) {
            return QString("%1 GB").arg(size / (double)GB, 0, 'f', 1);
        } else if (size >= MB) {
            return QString("%1 MB").arg(size / (double)MB, 0, 'f', 1);
        } else if (size >= KB) {
            return QString("%1 KB").arg(size / (double)KB, 0, 'f', 1);
        } else {
            return QString("%1 B").arg(size);
        }
    }
    
    /**
     * @brief Загрузить данные файла
     * @return Данные файла или пустой QByteArray при ошибке
     */
    QByteArray loadData() const {
        if (type == SelectedAttachmentType::ImageData) {
            qDebug() << "SelectedAttachment::loadData - ImageData, size:" << imageData.size();
            return imageData;
        }
        
        // Используем унифицированный метод для получения локального пути
        QString localPath = toLocalPath();
        
        qDebug() << "SelectedAttachment::loadData - url:" << url.toString()
                 << "localPath:" << localPath;
        
        if (localPath.isEmpty()) {
            qWarning() << "SelectedAttachment::loadData - failed to get local path from URL";
            return {};
        }
        
        QFile file(localPath);
        if (!file.open(QIODevice::ReadOnly)) {
            qWarning() << "SelectedAttachment::loadData - failed to open file:" << localPath
                       << "error:" << file.errorString();
            return {};
        }
        
        QByteArray data = file.readAll();
        file.close();
        
        qDebug() << "SelectedAttachment::loadData - loaded" << data.size() << "bytes from" << localPath;
        return data;
    }
    
    /**
     * @brief Тип файла для загрузки на сервер
     * 
     * Если forceAsDocument = true, всегда возвращаем Document
     */
    UploadFileType uploadFileType() const {
        if (forceAsDocument) {
            return UploadFileType::MessageAttachmentDocument;
        }
        
        QString ext = fileExtension();
        
        // Изображения
        static const QStringList imageExts = {"jpg", "jpeg", "png", "heic", "webp", "bmp"};
        if (imageExts.contains(ext)) {
            return UploadFileType::MessageAttachmentImage;
        }
        
        // GIF
        if (ext == "gif") {
            return UploadFileType::MessageAttachmentGif;
        }
        
        // Видео
        static const QStringList videoExts = {"mp4", "mov", "avi", "mkv", "webm"};
        if (videoExts.contains(ext)) {
            return UploadFileType::MessageAttachmentVideo;
        }
        
        // Всё остальное - документы
        return UploadFileType::MessageAttachmentDocument;
    }
    
    /**
     * @brief Content-Type для HTTP загрузки
     */
    QString contentType() const {
        QString ext = fileExtension();
        
        static const QMap<QString, QString> contentTypes = {
            {"jpg", "image/jpeg"},
            {"jpeg", "image/jpeg"},
            {"png", "image/png"},
            {"gif", "image/gif"},
            {"webp", "image/webp"},
            {"heic", "image/heic"},
            {"bmp", "image/bmp"},
            {"mp4", "video/mp4"},
            {"mov", "video/quicktime"},
            {"avi", "video/x-msvideo"},
            {"mkv", "video/x-matroska"},
            {"webm", "video/webm"},
            {"pdf", "application/pdf"},
            {"doc", "application/msword"},
            {"docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
            {"xls", "application/vnd.ms-excel"},
            {"xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
            {"zip", "application/zip"},
            {"rar", "application/vnd.rar"},
            {"7z", "application/x-7z-compressed"},
            {"mp3", "audio/mpeg"},
            {"wav", "audio/wav"},
            {"ogg", "audio/ogg"},
            {"txt", "text/plain"}
        };
        
        if (contentTypes.contains(ext)) {
            return contentTypes[ext];
        }
        
        return "application/octet-stream";
    }
    
    bool operator==(const SelectedAttachment& other) const {
        return id == other.id;
    }
    
    bool operator!=(const SelectedAttachment& other) const {
        return !(*this == other);
    }
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::SelectedAttachment)