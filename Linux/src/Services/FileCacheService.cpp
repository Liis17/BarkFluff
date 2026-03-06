/**
 * @file FileCacheService.cpp
 * @brief Реализация сервиса кэширования файлов
 */

#include "FileCacheService.h"
#include <QStandardPaths>
#include <QDir>
#include <QFile>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QMutexLocker>
#include <QDebug>

namespace BarkFluff {

FileCacheService* FileCacheService::instance_ = nullptr;

FileCacheService* FileCacheService::instance() {
    if (!instance_) {
        instance_ = new FileCacheService();
    }
    return instance_;
}

FileCacheService::FileCacheService(QObject* parent)
    : QObject(parent)
    , networkManager_(new QNetworkAccessManager(this))
{
    // Setup cache directory
    QString cacheRoot = QStandardPaths::writableLocation(QStandardPaths::CacheLocation);
    baseCacheDir_ = cacheRoot + "/barkfluff/files";
    
    ensureCacheDirectories();
    
    connect(networkManager_, &QNetworkAccessManager::finished,
            this, &FileCacheService::onDownloadFinished);
}

FileCacheService::~FileCacheService() {
    // Cancel all pending downloads
    for (auto it = pendingDownloads_.begin(); it != pendingDownloads_.end(); ++it) {
        it.key()->abort();
        it.key()->deleteLater();
    }
    pendingDownloads_.clear();
}

void FileCacheService::setServerBaseUrl(const QString& baseUrl) {
    serverBaseUrl_ = baseUrl;
    if (serverBaseUrl_.endsWith('/')) {
        serverBaseUrl_.chop(1);
    }
}

void FileCacheService::ensureCacheDirectories() {
    QDir dir(baseCacheDir_);
    if (!dir.exists()) {
        dir.mkpath(".");
    }
    
    // Create subdirectories for each file type
    QStringList subdirs = {"avatars", "images", "videos", "gifs", "documents"};
    for (const QString& subdir : subdirs) {
        QString path = baseCacheDir_ + "/" + subdir;
        if (!QDir(path).exists()) {
            dir.mkpath(subdir);
        }
    }
}

QString FileCacheService::cacheDirectory(CachedFileType type) const {
    QString subdir;
    switch (type) {
    case CachedFileType::Avatar:
        subdir = "avatars";
        break;
    case CachedFileType::Image:
        subdir = "images";
        break;
    case CachedFileType::Video:
        subdir = "videos";
        break;
    case CachedFileType::Gif:
        subdir = "gifs";
        break;
    case CachedFileType::Document:
        subdir = "documents";
        break;
    }
    return baseCacheDir_ + "/" + subdir;
}

QString FileCacheService::placeholderPath(CachedFileType type) const {
    // Return a placeholder image path based on file type
    // These should be bundled with the application
    QString placeholderName;
    switch (type) {
    case CachedFileType::Avatar:
        placeholderName = "user_placeholder.png";
        break;
    case CachedFileType::Image:
        placeholderName = "image_placeholder.png";
        break;
    case CachedFileType::Video:
        placeholderName = "video_placeholder.png";
        break;
    case CachedFileType::Gif:
        placeholderName = "gif_placeholder.png";
        break;
    case CachedFileType::Document:
        placeholderName = "document_placeholder.png";
        break;
    }
    
    // Check in application resources first
    QString resourcePath = ":/placeholders/" + placeholderName;
    if (QFile::exists(resourcePath)) {
        return resourcePath;
    }
    
    // Fallback to empty (will show a colored rectangle)
    return QString();
}

QString FileCacheService::defaultExtension(CachedFileType type) const {
    switch (type) {
    case CachedFileType::Avatar:
    case CachedFileType::Image:
        return "png";
    case CachedFileType::Video:
        return "mp4";
    case CachedFileType::Gif:
        return "gif";
    case CachedFileType::Document:
        return "bin";
    }
    return "bin";
}

QString FileCacheService::extractFileId(const QString& fileIdOrUrl) const {
    // Если это полный URL, извлекаем fileId из него
    if (fileIdOrUrl.startsWith("http://") || fileIdOrUrl.startsWith("https://")) {
        QUrl url(fileIdOrUrl);
        QString path = url.path();
        // Извлекаем последний сегмент пути (fileId)
        int lastSlash = path.lastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < path.length() - 1) {
            return path.mid(lastSlash + 1);
        }
    }
    // Иначе считаем что это уже fileId
    return fileIdOrUrl;
}

QString FileCacheService::checkCache(const QString& fileId) const {
    QString actualFileId = extractFileId(fileId);
    
    QMutexLocker locker(&mutex_);
    
    if (cacheInfo_.contains(actualFileId)) {
        QString path = cacheInfo_[actualFileId].localPath;
        if (QFile::exists(path)) {
            return path;
        }
        // File was deleted, remove from cache info
        cacheInfo_.remove(actualFileId);
    }
    
    // Search in all cache directories
    QStringList subdirs = {"avatars", "images", "videos", "gifs", "documents"};
    for (const QString& subdir : subdirs) {
        QDir dir(baseCacheDir_ + "/" + subdir);
        QStringList files = dir.entryList(QStringList() << actualFileId + ".*", QDir::Files);
        if (!files.isEmpty()) {
            QString path = dir.filePath(files.first());
            // Add to cache info
            locker.unlock();
            const_cast<FileCacheService*>(this)->cacheInfo_[actualFileId] = {
                actualFileId, path, CachedFileType::Image, false
            };
            return path;
        }
    }
    
    return QString();
}

bool FileCacheService::isFileCached(const QString& fileId) const {
    return !checkCache(fileId).isEmpty();
}

QString FileCacheService::getCachedFilePath(const QString& fileId, 
                                            CachedFileType type,
                                            const QString& providedUrl) {
    qDebug() << "FileCacheService::getCachedFilePath - fileId:" << fileId 
             << "providedUrl:" << providedUrl 
             << "serverBaseUrl:" << serverBaseUrl_;
    
    if (fileId.isEmpty()) {
        qDebug() << "FileCacheService::getCachedFilePath - fileId is empty, returning placeholder";
        return placeholderPath(type);
    }
    
    // Check cache first
    QString cachedPath = checkCache(fileId);
    if (!cachedPath.isEmpty()) {
        qDebug() << "FileCacheService::getCachedFilePath - found in cache:" << cachedPath;
        return cachedPath;
    }
    
    // Start async download
    QString url = providedUrl;
    if (url.isEmpty() && !serverBaseUrl_.isEmpty()) {
        // Build URL from server base + file ID
        // This depends on server API
        url = serverBaseUrl_ + "/files/" + fileId;
    }
    
    qDebug() << "FileCacheService::getCachedFilePath - download URL:" << url;
    
    if (!url.isEmpty()) {
        startDownload(fileId, type, url);
    } else {
        qWarning() << "FileCacheService::getCachedFilePath - no URL available for download";
    }
    
    return placeholderPath(type);
}

void FileCacheService::getCachedFilePathAsync(const QString& fileId,
                                               CachedFileType type,
                                               const QString& providedUrl,
                                               std::function<void(const QString&)> callback) {
    if (fileId.isEmpty()) {
        callback(placeholderPath(type));
        return;
    }
    
    // Извлекаем чистый fileId из URL если нужно
    QString actualFileId = extractFileId(fileId);
    
    // Check cache first
    QString cachedPath = checkCache(actualFileId);
    if (!cachedPath.isEmpty()) {
        callback(cachedPath);
        return;
    }
    
    // Connect to fileCached signal for one-time notification
    QMetaObject::Connection* connection = new QMetaObject::Connection();
    *connection = connect(this, &FileCacheService::fileCached,
        [actualFileId, callback, connection, this](const QString& cachedFileId, 
                                              const QString& localPath,
                                              CachedFileType) {
            if (cachedFileId == actualFileId) {
                disconnect(*connection);
                delete connection;
                callback(localPath);
            }
        });
    
    // Also connect to error signal
    QMetaObject::Connection* errorConnection = new QMetaObject::Connection();
    *errorConnection = connect(this, &FileCacheService::fileLoadError,
        [actualFileId, callback, errorConnection, type, this](const QString& errorFileId,
                                                          const QString&) {
            if (errorFileId == actualFileId) {
                disconnect(*errorConnection);
                delete errorConnection;
                callback(placeholderPath(type));
            }
        });
    
    // Start download - используем actualFileId как ключ, а providedUrl как URL
    QString url = providedUrl.isEmpty() ? fileId : providedUrl;
    if (url.isEmpty() && !serverBaseUrl_.isEmpty()) {
        url = serverBaseUrl_ + "/files/" + actualFileId;
    }
    
    if (!url.isEmpty()) {
        startDownload(actualFileId, type, url);
    } else {
        emit fileLoadError(actualFileId, "No URL provided");
    }
}

void FileCacheService::startDownload(const QString& fileId,
                                      CachedFileType type,
                                      const QString& url) {
    QMutexLocker locker(&mutex_);
    
    // Check if already downloading
    if (cacheInfo_.contains(fileId) && cacheInfo_[fileId].isLoading) {
        return;
    }
    
    // Mark as loading
    cacheInfo_[fileId] = {fileId, QString(), type, true};
    
    QUrl downloadUrl(url);
    QNetworkRequest request(downloadUrl);
    request.setAttribute(QNetworkRequest::User, QVariant::fromValue(fileId));
    
    QNetworkReply* reply = networkManager_->get(request);
    pendingDownloads_[reply] = fileId;
}

void FileCacheService::onDownloadFinished(QNetworkReply* reply) {
    QString originalId = pendingDownloads_.take(reply);
    QString fileId = extractFileId(originalId);  // Извлекаем чистый fileId из URL
    reply->deleteLater();
    
    qDebug() << "FileCacheService::onDownloadFinished - originalId:" << originalId << "fileId:" << fileId;
    
    if (reply->error() != QNetworkReply::NoError) {
        qWarning() << "Failed to download file" << fileId << ":" << reply->errorString();
        
        QMutexLocker locker(&mutex_);
        cacheInfo_.remove(fileId);
        cacheInfo_.remove(originalId);
        
        emit fileLoadError(fileId, reply->errorString());
        return;
    }
    
    // Determine file type from cache info
    CachedFileType type = CachedFileType::Image;
    {
        QMutexLocker locker(&mutex_);
        if (cacheInfo_.contains(fileId)) {
            type = cacheInfo_[fileId].type;
        } else if (cacheInfo_.contains(originalId)) {
            type = cacheInfo_[originalId].type;
        }
    }
    
    // Get content type to determine extension
    QString extension;
    QVariant contentType = reply->header(QNetworkRequest::ContentTypeHeader);
    if (contentType.isValid()) {
        QString ct = contentType.toString();
        if (ct.contains("image/jpeg") || ct.contains("image/jpg")) {
            extension = "jpg";
        } else if (ct.contains("image/png")) {
            extension = "png";
        } else if (ct.contains("image/gif")) {
            extension = "gif";
        } else if (ct.contains("image/webp")) {
            extension = "webp";
        } else if (ct.contains("video/mp4")) {
            extension = "mp4";
        } else if (ct.contains("video/webm")) {
            extension = "webm";
        }
    }
    
    if (extension.isEmpty()) {
        // Try to get extension from URL
        QUrl url = reply->url();
        QString path = url.path();
        int dotPos = path.lastIndexOf('.');
        if (dotPos > 0) {
            extension = path.mid(dotPos + 1);
        }
    }
    
    if (extension.isEmpty()) {
        extension = defaultExtension(type);
    }
    
    // Save file to cache
    QString cacheDir = cacheDirectory(type);
    QString filePath = cacheDir + "/" + fileId + "." + extension;
    
    QFile file(filePath);
    if (!file.open(QIODevice::WriteOnly)) {
        qWarning() << "Failed to open file for writing:" << filePath;
        emit fileLoadError(fileId, "Cannot write to cache");
        return;
    }
    
    file.write(reply->readAll());
    file.close();
    
    // Update cache info
    {
        QMutexLocker locker(&mutex_);
        cacheInfo_[fileId] = {fileId, filePath, type, false};
    }
    
    qDebug() << "Cached file:" << fileId << "to" << filePath;
    emit fileCached(fileId, filePath, type);
}

void FileCacheService::clearCache(CachedFileType type) {
    QString dir = cacheDirectory(type);
    QDir cacheDir(dir);
    
    QStringList files = cacheDir.entryList(QDir::Files);
    for (const QString& file : files) {
        cacheDir.remove(file);
    }
    
    // Clear cache info for this type
    QMutexLocker locker(&mutex_);
    QStringList keysToRemove;
    for (auto it = cacheInfo_.begin(); it != cacheInfo_.end(); ++it) {
        if (it->type == type) {
            keysToRemove.append(it.key());
        }
    }
    for (const QString& key : keysToRemove) {
        cacheInfo_.remove(key);
    }
}

qint64 FileCacheService::cacheSize() {
    QString cacheRoot = QStandardPaths::writableLocation(QStandardPaths::CacheLocation);
    QString baseCacheDir = cacheRoot + "/barkfluff/files";
    
    qint64 totalSize = 0;
    QDir dir(baseCacheDir);
    
    if (!dir.exists()) {
        return 0;
    }
    
    QStringList subdirs = {"avatars", "images", "videos", "gifs", "documents"};
    for (const QString& subdir : subdirs) {
        QDir subDir(baseCacheDir + "/" + subdir);
        if (subDir.exists()) {
            QStringList files = subDir.entryList(QDir::Files);
            for (const QString& file : files) {
                QFileInfo fileInfo(subDir.filePath(file));
                totalSize += fileInfo.size();
            }
        }
    }
    
    return totalSize;
}

void FileCacheService::clearAllCache() {
    QString cacheRoot = QStandardPaths::writableLocation(QStandardPaths::CacheLocation);
    QString baseCacheDir = cacheRoot + "/barkfluff/files";
    
    QDir dir(baseCacheDir);
    
    if (!dir.exists()) {
        return;
    }
    
    QStringList subdirs = {"avatars", "images", "videos", "gifs", "documents"};
    for (const QString& subdir : subdirs) {
        QDir subDir(baseCacheDir + "/" + subdir);
        if (subDir.exists()) {
            QStringList files = subDir.entryList(QDir::Files);
            for (const QString& file : files) {
                subDir.remove(file);
            }
        }
    }
    
    // Clear all cache info
    if (instance_) {
        QMutexLocker locker(&instance_->mutex_);
        instance_->cacheInfo_.clear();
    }
}

} // namespace BarkFluff
