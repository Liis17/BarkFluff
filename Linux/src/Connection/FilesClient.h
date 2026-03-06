#pragma once

#include "GrpcClient.h"
#include "Models/ServerConfiguration.h"
#include "Models/SelectedAttachment.h"  // For UploadFileType enum
#include "files_api.grpc.pb.h"
#include <QCryptographicHash>
#include <functional>

namespace BarkFluff {

// Upload URL info
struct UploadUrlInfo {
    QString url;        // URL to upload file to
    QString fileId;     // File ID that will be assigned
};

// Hash check result
struct HashCheckResult {
    bool exists = false;        // File already exists on server
    QString fileId;             // Existing file ID (if exists)
};

// Storage info
struct UserStorageInfo {
    qint64 usedBytes = 0;
    qint64 totalBytes = 0;
    qint64 availableBytes = 0;
};

/**
 * @brief Progress callback for file uploads
 * @param bytesSent Bytes sent so far
 * @param bytesTotal Total bytes to send
 */
using UploadProgressCallback = std::function<void(qint64 bytesSent, qint64 bytesTotal)>;

class FilesClient : public GrpcClient {
    std::unique_ptr<barkfluff::files::FilesApi::Stub> stub_;
    
public:
    FilesClient(const ServiceInfo& service);
    
    // Get upload URL for a file type
    UploadUrlInfo getUploadUrl(UploadFileType fileType, const QString& accessToken);
    
    // Check if file with this hash already exists (deduplication)
    HashCheckResult checkFileHash(const QString& sha256Hash, const QString& accessToken);
    
    // Get user storage info
    UserStorageInfo getUserStorageInfo(const QString& accessToken);
    
    // Get download URL for a file
    QString getDownloadUrl(const QString& fileId, const QString& accessToken);
    
    // Upload file data to URL (HTTP PUT) with optional progress callback
    bool uploadFileToUrl(const QString& url, const QByteArray& data, 
                         const QString& filename, const QString& contentType,
                         UploadProgressCallback progressCallback = nullptr);
    
    // Combined: get URL and upload (without deduplication)
    QString uploadFile(UploadFileType fileType, const QByteArray& data, 
                       const QString& filename, const QString& accessToken,
                       UploadProgressCallback progressCallback = nullptr);
    
    /**
     * @brief Full upload flow with deduplication
     * 
     * 1. Calculate SHA256 hash
     * 2. Check if file exists (deduplication)
     * 3. If exists - return existing fileId
     * 4. If not - get upload URL and upload file
     * 
     * @param fileType Type of file for upload
     * @param data File data
     * @param filename Original filename
     * @param contentType MIME type
     * @param accessToken Auth token
     * @param progressCallback Optional progress callback
     * @return File ID
     */
    QString uploadFileWithDedup(
        UploadFileType fileType,
        const QByteArray& data,
        const QString& filename,
        const QString& contentType,
        const QString& accessToken,
        UploadProgressCallback progressCallback = nullptr
    );
    
    /**
     * @brief Calculate SHA256 hash of data
     */
    static QString calculateSha256(const QByteArray& data) {
        return QCryptographicHash::hash(data, QCryptographicHash::Sha256).toHex();
    }
    
    /**
     * @brief Sanitize filename - remove special characters
     * Allows only letters, digits, dash, underscore, dot
     */
    static QString sanitizeFileName(const QString& filename) {
        QString result;
        for (const QChar& c : filename) {
            if (c.isLetterOrNumber() || c == '-' || c == '_' || c == '.') {
                result.append(c);
            }
        }
        return result.isEmpty() ? QString("file") : result;
    }
};

} // namespace BarkFluff
