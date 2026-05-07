//
//  FilesRepository.swift
//  BFNetworking
//
//  Репозиторий для работы с файлами
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf
import CryptoKit

public actor FilesRepository: FilesRepositoryProtocol {
    private let connectionManager: ConnectionManager
    private let httpClient: URLSession

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
        self.httpClient = URLSession.shared
    }

    // MARK: - Public Methods

    public func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo {
        var request = Barkfluff_Files_GetUploadUrlRequest()
        request.fileType = mapFileType(fileType)
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getUploadUrl(req)
                return FileUploadInfo(
                    fileID: response.fileID,
                    uploadURL: response.url,
                    expiresIn: 3600 // Default 1 hour expiration
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    /// Загружает файл с проверкой лимита, дедупликацией по SHA256 и multipart/form-data
    public func uploadFile(data: Data, fileName: String, fileType: UploadFileType?) async throws -> String {
        // 1. Определяем тип файла по расширению если не указан
        let resolvedFileType: UploadFileType
        if let fileType = fileType {
            resolvedFileType = fileType
        } else {
            let ext = (fileName as NSString).pathExtension
            resolvedFileType = UploadFileType.from(extension: ext)
        }

        // DEBUG: Логируем тип файла
        print("📁 [FilesRepository] Uploading: \(fileName), fileType: \(fileType ?? .unknown), resolved: \(resolvedFileType)")

        // 2. Проверяем лимит хранилища (soft check - не блокируем загрузку)
        do {
            let storageInfo = try await getUserStorageInfo()
            if !storageInfo.canFit(fileSize: Int64(data.count)) {
                throw BFNetworkingError.storageLimitExceeded(
                    "Необходимо \(data.count) байт, доступно \(storageInfo.availableBytes)"
                )
            }
        } catch let error as BFNetworkingError {
            // Пробрасываем ошибку лимита
            throw error
        } catch {
            // Игнорируем другие ошибки проверки лимита
        }

        // 3. Вычисляем SHA256 хеш
        let fileHash = Self.computeSHA256(data: data)

        // 4. Проверяем дедупликацию
        let checkResult = try await checkFileHash(hash: fileHash)
        if checkResult.exists, let existingFileID = checkResult.fileID {
            // Файл уже существует - возвращаем его ID без загрузки
            return existingFileID
        }

        // 5. Получаем URL для загрузки
        let uploadInfo = try await getUploadURL(fileType: resolvedFileType)

        // 6. Санитизируем имя файла
        let sanitizedFileName = Self.sanitizeFileName(fileName)

        // 7. Загружаем через HTTP POST multipart/form-data
        guard let uploadURL = URL(string: uploadInfo.uploadURL) else {
            throw BFNetworkingError.invalidURL(uploadInfo.uploadURL)
        }

        let boundary = "Boundary-\(UUID().uuidString)"
        var request = URLRequest(url: uploadURL)
        request.httpMethod = "POST"
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")

        var body = Data()

        // Добавляем файл в multipart body
        // Content-Type по расширению (сервер определяет тип по fileType в gRPC, не по MIME)
        let contentType = Self.contentType(for: sanitizedFileName)
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"file\"; filename=\"\(sanitizedFileName)\"\r\n".data(using: .utf8)!)
        body.append("Content-Type: \(contentType)\r\n\r\n".data(using: .utf8)!)
        body.append(data)
        body.append("\r\n--\(boundary)--\r\n".data(using: .utf8)!)

        request.httpBody = body

        // Выполняем запрос
        let (_, response) = try await httpClient.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse else {
            throw BFNetworkingError.networkError("Invalid response")
        }

        guard (200...299).contains(httpResponse.statusCode) else {
            throw BFNetworkingError.fileUploadFailed("HTTP \(httpResponse.statusCode)")
        }

        // 8. Возвращаем fileID из GetUploadUrlResponse
        return uploadInfo.fileID
    }

    public func getTempDownloadURL(fileID: String) async throws -> String {
        var request = Barkfluff_Files_GetTempDownloadUrlRequest()
        request.fileIds = [fileID]
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getTempDownloadUrl(req)
                guard let fileData = response.fileUrls.first else {
                    throw BFNetworkingError.unknown("No download URL returned")
                }
                return fileData.url
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getTempDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo] {
        guard !fileIDs.isEmpty else {
            return []
        }

        var request = Barkfluff_Files_GetTempDownloadUrlRequest()
        request.fileIds = fileIDs
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getTempDownloadUrl(req)

                return response.fileUrls.map { fileData in
                    FileDownloadInfo(
                        fileID: fileData.fileID,
                        url: fileData.url,
                        previewURL: fileData.previewURL.isEmpty ? nil : fileData.previewURL
                    )
                }
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func checkFileHash(hash: String) async throws -> FileCheckResult {
        var request = Barkfluff_Files_CheckFileHashRequest()
        request.fileHash = hash
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.checkFileHash(req)
                let fileID = response.fileID
                return FileCheckResult(
                    exists: !fileID.isEmpty,
                    fileID: fileID.isEmpty ? nil : fileID
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getUserStorageInfo() async throws -> StorageInfo {
        let request = Barkfluff_Files_GetUserStorageInfoRequest()
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { [self] client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getUserStorageInfo(req)
                var usedByType: [UploadFileType: Int64] = [:]
                for entry in response.storageByTypes {
                    let type = mapProtoFileType(entry.fileType)
                    usedByType[type, default: 0] += entry.usedStorage
                }
                return StorageInfo(
                    usedBytes: response.totalUsedStorage,
                    limitBytes: response.storageLimit,
                    usedByType: usedByType
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Mapping Helpers

    private nonisolated func mapFileType(_ fileType: UploadFileType) -> Barkfluff_Files_UploadFileType {
        switch fileType {
        case .unknown:
            return .unknown
        case .userAvatar:
            return .userAvatar
        case .messageAttachmentImage:
            return .messageAttachmentImage
        case .messageAttachmentVideo:
            return .messageAttachmentVideo
        case .messageAttachmentGif:
            return .messageAttachmentGif
        case .messageAttachmentDocument:
            return .messageAttachmentDocument
        case .chatPicture:
            return .chatPicture
        case .messageAttachmentAudio:
            return .messageAttachmentAudio
        case .messageAttachmentVoice:
            return .messageAttachmentVoice
        case .messageAttachmentSticker:
            return .messageAttachmentSticker
        case .userProfilePoster:
            return .userProfilePoster
        }
    }

    private nonisolated func mapProtoFileType(_ proto: Barkfluff_Files_UploadFileType) -> UploadFileType {
        switch proto {
        case .unknown, .UNRECOGNIZED:
            return .unknown
        case .userAvatar:
            return .userAvatar
        case .messageAttachmentImage:
            return .messageAttachmentImage
        case .messageAttachmentVideo:
            return .messageAttachmentVideo
        case .messageAttachmentGif:
            return .messageAttachmentGif
        case .messageAttachmentDocument:
            return .messageAttachmentDocument
        case .chatPicture:
            return .chatPicture
        case .messageAttachmentAudio:
            return .messageAttachmentAudio
        case .messageAttachmentVoice:
            return .messageAttachmentVoice
        case .messageAttachmentSticker:
            return .messageAttachmentSticker
        case .userProfilePoster:
            return .userProfilePoster
        }
    }

    // MARK: - Static Helpers

    /// Вычисляет SHA256 хеш данных (lowercase hex)
    public static func computeSHA256(data: Data) -> String {
        let hash = SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }

    /// Санитизирует имя файла - удаляет спецсимволы
    public static func sanitizeFileName(_ fileName: String) -> String {
        // Разрешаем буквы, цифры, дефис, подчеркивание, точку
        let allowedCharacters = CharacterSet.alphanumerics
            .union(CharacterSet(charactersIn: "-_."))
        let sanitized = fileName.unicodeScalars
            .filter { allowedCharacters.contains($0) }
            .map { Character($0) }
        return String(sanitized)
    }

    /// Возвращает MIME тип по расширению файла
    public static func contentType(for fileName: String) -> String {
        let ext = (fileName as NSString).pathExtension.lowercased()
        switch ext {
        case "jpg", "jpeg":
            return "image/jpeg"
        case "png":
            return "image/png"
        case "gif":
            return "image/gif"
        case "webp":
            return "image/webp"
        case "heic":
            return "image/heic"
        case "bmp":
            return "image/bmp"
        case "mp4":
            return "video/mp4"
        case "mov":
            return "video/quicktime"
        case "avi":
            return "video/x-msvideo"
        case "mkv":
            return "video/x-matroska"
        case "webm":
            return "video/webm"
        case "pdf":
            return "application/pdf"
        case "doc":
            return "application/msword"
        case "docx":
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        case "xls":
            return "application/vnd.ms-excel"
        case "xlsx":
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        case "txt":
            return "text/plain"
        case "json":
            return "application/json"
        case "xml":
            return "application/xml"
        case "zip":
            return "application/zip"
        default:
            return "application/octet-stream"
        }
    }
}
