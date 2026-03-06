/**
 * @file FileCacheService.h
 * @brief Сервис кэширования файлов (аналог WPF FileCacheService)
 */

#pragma once

#include <QObject>
#include <QString>
#include <QUrl>
#include <QMap>
#include <QMutex>
#include <QNetworkAccessManager>
#include <memory>

namespace BarkFluff {

/**
 * @brief Тип кэшируемого файла
 */
enum class CachedFileType {
    Avatar,
    Image,
    Video,
    Gif,
    Document
};

/**
 * @brief Информация о закэшированном файле
 */
struct CachedFileInfo {
    QString fileId;
    QString localPath;
    CachedFileType type;
    bool isLoading = false;
};

/**
 * @brief Сервис кэширования файлов на диске
 * 
 * Реализует асинхронную загрузку файлов с сервера и их кэширование.
 * Аналог FileCacheService из WPF клиента.
 */
class FileCacheService : public QObject {
    Q_OBJECT
    
public:
    static FileCacheService* instance();
    
    /**
     * @brief Получить путь к закэшированному файлу
     * 
     * Если файл не в кэше, запускает асинхронную загрузку и возвращает placeholder.
     * 
     * @param fileId Идентификатор файла
     * @param type Тип файла
     * @param providedUrl Опциональный URL для загрузки
     * @return Локальный путь к файлу или placeholder
     */
    QString getCachedFilePath(const QString& fileId, 
                              CachedFileType type,
                              const QString& providedUrl = QString());
    
    /**
     * @brief Асинхронно получить путь к файлу (ждёт загрузки)
     */
    void getCachedFilePathAsync(const QString& fileId,
                                CachedFileType type,
                                const QString& providedUrl,
                                std::function<void(const QString&)> callback);
    
    /**
     * @brief Проверить, есть ли файл в кэше
     */
    bool isFileCached(const QString& fileId) const;
    
    /**
     * @brief Очистить кэш (метод экземпляра)
     */
    void clearCache(CachedFileType type = CachedFileType::Image);
    
    /**
     * @brief Получить размер кэша в байтах (статический метод)
     */
    static qint64 cacheSize();
    
    /**
     * @brief Очистить весь кэш (статический метод)
     */
    static void clearAllCache();
    
    /**
     * @brief Получить путь к placeholder для типа файла
     */
    QString placeholderPath(CachedFileType type) const;
    
    /**
     * @brief Получить директорию кэша для типа файла
     */
    QString cacheDirectory(CachedFileType type) const;
    
    /**
     * @brief Установить базовый URL сервера для загрузки файлов
     */
    void setServerBaseUrl(const QString& baseUrl);
    
signals:
    /**
     * @brief Сигнал вызывается когда файл закэширован
     */
    void fileCached(const QString& fileId, const QString& localPath, CachedFileType type);
    
    /**
     * @brief Ошибка загрузки файла
     */
    void fileLoadError(const QString& fileId, const QString& error);
    
private slots:
    void onDownloadFinished(QNetworkReply* reply);
    
private:
    explicit FileCacheService(QObject* parent = nullptr);
    ~FileCacheService();
    
    void ensureCacheDirectories();
    QString extractFileId(const QString& fileIdOrUrl) const;
    QString checkCache(const QString& fileId) const;
    void startDownload(const QString& fileId, 
                       CachedFileType type,
                       const QString& url);
    QString defaultExtension(CachedFileType type) const;
    
    static FileCacheService* instance_;
    
    QString baseCacheDir_;
    QString serverBaseUrl_;
    mutable QMutex mutex_;
    mutable QMap<QString, CachedFileInfo> cacheInfo_;
    QNetworkAccessManager* networkManager_;
    QMap<QNetworkReply*, QString> pendingDownloads_;
};

} // namespace BarkFluff