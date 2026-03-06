/**
 * @file FilesClient.cpp
 * @brief Реализация клиента для работы с файлами
 * 
 * Поддерживает полный флоу загрузки с дедупликацией по SHA256 хешу
 */

#include "FilesClient.h"
#include "Utils/ErrorHandler.h"
#include <QNetworkAccessManager>
#include <QNetworkRequest>
#include <QNetworkReply>
#include <QEventLoop>
#include <QHttpMultiPart>
#include <QUuid>
#include <QDebug>

namespace BarkFluff {

FilesClient::FilesClient(const ServiceInfo& service)
    : GrpcClient(service.endpoint.host, service.endpoint.port, service.tlsEnabled)
{
    stub_ = barkfluff::files::FilesApi::NewStub(channel_);
}

UploadUrlInfo FilesClient::getUploadUrl(UploadFileType fileType, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::files::GetUploadUrlRequest request;
    request.set_file_type(static_cast<barkfluff::files::UploadFileType>(static_cast<int>(fileType)));
    
    barkfluff::files::GetUploadUrlResponse response;
    grpc::Status status = stub_->GetUploadUrl(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    UploadUrlInfo info;
    info.url = QString::fromStdString(response.url());
    info.fileId = QString::fromStdString(response.file_id());
    
    return info;
}

HashCheckResult FilesClient::checkFileHash(const QString& sha256Hash, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::files::CheckFileHashRequest request;
    request.set_file_hash(sha256Hash.toStdString());
    
    barkfluff::files::CheckFileHashResponse response;
    grpc::Status status = stub_->CheckFileHash(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    HashCheckResult result;
    QString fileId = QString::fromStdString(response.file_id());
    result.exists = !fileId.isEmpty();
    result.fileId = fileId;
    
    return result;
}

UserStorageInfo FilesClient::getUserStorageInfo(const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::files::GetUserStorageInfoRequest request;
    barkfluff::files::GetUserStorageInfoResponse response;
    
    grpc::Status status = stub_->GetUserStorageInfo(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    UserStorageInfo info;
    info.usedBytes = response.total_used_storage();
    info.totalBytes = response.storage_limit();
    info.availableBytes = info.totalBytes - info.usedBytes;
    
    return info;
}

QString FilesClient::getDownloadUrl(const QString& fileId, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::files::GetTempDownloadUrlRequest request;
    request.add_file_ids(fileId.toStdString());
    
    barkfluff::files::GetTempDownloadUrlResponse response;
    grpc::Status status = stub_->GetTempDownloadUrl(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    // Response contains repeated file_urls
    if (response.file_urls_size() == 0) {
        return QString();
    }
    
    return QString::fromStdString(response.file_urls(0).url());
}

bool FilesClient::uploadFileToUrl(const QString& url, const QByteArray& data, 
                                   const QString& filename, const QString& contentType,
                                   UploadProgressCallback progressCallback) {
    qDebug() << "FilesClient::uploadFileToUrl - url:" << url;
    qDebug() << "  filename:" << filename << "contentType:" << contentType << "size:" << data.size();
    
    QNetworkAccessManager manager;
    QNetworkRequest request{QUrl(url)};
    
    // Используем multipart/form-data как в Mac клиенте
    QString boundary = QString("Boundary-%1").arg(QUuid::createUuid().toString(QUuid::WithoutBraces));
    QString contentTypeHeader = QString("multipart/form-data; boundary=%1").arg(boundary);
    request.setHeader(QNetworkRequest::ContentTypeHeader, contentTypeHeader);
    
    qDebug() << "  Using multipart/form-data with boundary:" << boundary;
    
    // Формируем multipart body как в Mac FilesRepository
    QByteArray body;
    QString sanitizedFilename = sanitizeFileName(filename);
    
    // --Boundary
    body.append(QString("--%1\r\n").arg(boundary).toUtf8());
    // Content-Disposition: form-data; name="file"; filename="..."
    body.append(QString("Content-Disposition: form-data; name=\"file\"; filename=\"%1\"\r\n")
                .arg(sanitizedFilename).toUtf8());
    // Content-Type: ...
    body.append(QString("Content-Type: %1\r\n\r\n").arg(contentType).toUtf8());
    // Данные файла
    body.append(data);
    // Конец boundary
    body.append(QString("\r\n--%1--\r\n").arg(boundary).toUtf8());
    
    qDebug() << "  Multipart body size:" << body.size() << "sanitized filename:" << sanitizedFilename;
    
    request.setHeader(QNetworkRequest::ContentLengthHeader, body.size());
    
    // Используем POST вместо PUT (как в Mac клиенте)
    QNetworkReply* reply = manager.post(request, body);
    
    // Connect progress if callback provided
    QEventLoop loop;
    QObject::connect(reply, &QNetworkReply::finished, &loop, &QEventLoop::quit);
    
    if (progressCallback) {
        QObject::connect(reply, &QNetworkReply::uploadProgress, 
            [progressCallback](qint64 bytesSent, qint64 bytesTotal) {
                progressCallback(bytesSent, bytesTotal);
            });
    }
    
    loop.exec();
    
    bool success = (reply->error() == QNetworkReply::NoError);
    
    if (!success) {
        qWarning() << "Upload failed:" << reply->errorString();
        qWarning() << "  HTTP status:" << reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        qWarning() << "  Response:" << QString::fromUtf8(reply->readAll());
    } else {
        qDebug() << "Upload success! Response:" << QString::fromUtf8(reply->readAll()).left(200);
    }
    
    reply->deleteLater();
    
    return success;
}

QString FilesClient::uploadFile(UploadFileType fileType, const QByteArray& data, 
                                 const QString& filename, const QString& accessToken,
                                 UploadProgressCallback progressCallback) {
    // Determine content type from filename
    QString contentType = "application/octet-stream";
    QString ext = filename.section('.', -1).toLower();
    
    static const QMap<QString, QString> contentTypes = {
        {"jpg", "image/jpeg"}, {"jpeg", "image/jpeg"}, {"png", "image/png"},
        {"gif", "image/gif"}, {"webp", "image/webp"}, {"bmp", "image/bmp"},
        {"mp4", "video/mp4"}, {"mov", "video/quicktime"}, {"webm", "video/webm"},
        {"pdf", "application/pdf"}, {"txt", "text/plain"},
        {"doc", "application/msword"}, {"docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
        {"mp3", "audio/mpeg"}, {"wav", "audio/wav"}
    };
    
    if (contentTypes.contains(ext)) {
        contentType = contentTypes[ext];
    }
    
    // Get upload URL
    UploadUrlInfo uploadInfo = getUploadUrl(fileType, accessToken);
    
    // Upload file to URL
    if (!uploadFileToUrl(uploadInfo.url, data, filename, contentType, progressCallback)) {
        throw std::runtime_error("Failed to upload file to storage");
    }
    
    return uploadInfo.fileId;
}

QString FilesClient::uploadFileWithDedup(
    UploadFileType fileType,
    const QByteArray& data,
    const QString& filename,
    const QString& contentType,
    const QString& accessToken,
    UploadProgressCallback progressCallback
) {
    // 1. Calculate SHA256 hash
    QString hash = calculateSha256(data);
    qDebug() << "File hash:" << hash << "size:" << data.size();
    
    // 2. Check if file already exists (deduplication)
    HashCheckResult hashResult = checkFileHash(hash, accessToken);
    
    if (hashResult.exists && !hashResult.fileId.isEmpty()) {
        qDebug() << "File already exists, using fileId:" << hashResult.fileId;
        // File already on server - don't re-upload!
        return hashResult.fileId;
    }
    
    // 3. Get upload URL
    UploadUrlInfo uploadInfo = getUploadUrl(fileType, accessToken);
    qDebug() << "Got upload URL, fileId will be:" << uploadInfo.fileId;
    
    // 4. Upload file
    QString ct = contentType.isEmpty() ? "application/octet-stream" : contentType;
    if (!uploadFileToUrl(uploadInfo.url, data, filename, ct, progressCallback)) {
        throw std::runtime_error("Failed to upload file to storage");
    }
    
    // 5. Return fileId from GetUploadUrlResponse (NOT from upload response)
    return uploadInfo.fileId;
}

} // namespace BarkFluff