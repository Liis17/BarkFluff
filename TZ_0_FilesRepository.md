# Техническое задание: Реализация FilesRepository

## Этап 0: FilesRepository (ОБЯЗАТЕЛЬНЫЙ перед этапами 2 и 3)

---

## 1. Обзор

### 1.1 Проблема
`FilesRepository` в `BFNetworking` НЕ РЕАЛИЗОВАН - все методы выбрасывают `BFNetworkingError.unknown("Not implemented")`.

### 1.2 Цель
Реализовать полный `FilesRepository` для работы с файлами:
- Получение URL для загрузки файлов
- Получение временных URL для скачивания
- Проверка хеша файла (дедупликация)
- Получение информации о хранилище

---

## 2. Референс: WPF реализация

**ВАЖНО: Смотреть WPF клиент как референс!**

WPF клиент в `BarkFluff.Client.WPF` имеет рабочую реализацию в:
- `ClientComponents/BarkFluff.WebApi.Core/Managers/WebApiFileManager.cs`
- `BarkFluff.Client.WPF/Services/App/Caching/FileCacheService.cs`

### 2.1 Полный флоу загрузки файла (из WPF)

```csharp
// WebApiFileManager.UploadFileAsync()

// 1. Для изображений - оптимизация перед загрузкой
if (fileType == MessageAttachmentImage) {
    processedFilePath = await ImageProcessor.ProcessImageForUploadAsync(filePath);
}

// 2. Проверка лимита хранилища
var storageInfo = await GetUserStorageInfoAsync();
var availableSpace = storageInfo.totalSpace - storageInfo.totalUsedSpace;
if (fileSize > availableSpace) {
    return error("Недостаточно места в хранилище");
}

// 3. Вычисление SHA256 хеша файла
var fileHash = await ComputeFileHashAsync(fileToUpload);
// Используется SHA256, результат в lowercase hex string

// 4. Проверка дедупликации - ВАЖНО!
var hashCheckResponse = await FilesAC.CheckFileHashAsync(new CheckFileHashRequest {
    FileHash = fileHash
});
if (!string.IsNullOrEmpty(hashCheckResponse.FileId)) {
    // Файл уже есть на сервере - НЕ загружаем повторно!
    return hashCheckResponse.FileId;  // Сразу возвращаем fileID
}

// 5. Получение URL для загрузки
var getLinkUpload = await FilesAC.GetUploadUrlAsync(new GetUploadUrlRequest {
    FileType = fileType
});
// getLinkUpload.Url - куда загружать
// getLinkUpload.FileId - ID файла для использования в sendMessage

// 6. Загрузка файла через HTTP POST с MultipartFormData
using var formData = new MultipartFormDataContent();
using var streamContent = new StreamContent(fileStream);

// Определение Content-Type по расширению
var contentType = extension switch {
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".gif" => "image/gif",
    ".webp" => "image/webp",
    ".mp4" => "video/mp4",
    ".webm" => "video/webm",
    ".avi" => "video/x-msvideo",
    ".mov" => "video/quicktime",
    ".mkv" => "video/x-matroska",
    _ => "application/octet-stream"
};
streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

// Санитизация имени файла (удаление спецсимволов)
var sanitizedFileName = Regex.Replace(fileName, @"[^\w\.-]", "_");
formData.Add(streamContent, "file", sanitizedFileName);

// Загрузка
var response = await httpClient.PostAsync(getLinkUpload.Url, formData);

// 7. Возвращаем fileID из GetUploadUrlResponse (НЕ из ответа upload!)
return getLinkUpload.FileId;
```

### 2.2 Вычисление SHA256 хеша

```csharp
public static async Task<string> ComputeFileHashAsync(string filePath) {
    using var sha256 = SHA256.Create();
    await using var stream = File.OpenRead(filePath);
    var hashBytes = await sha256.ComputeHashAsync(stream);
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}
```

### 2.3 Получение URL для скачивания

```csharp
// Одиночный файл
var response = await FilesAC.GetTempDownloadUrlAsync(new GetTempDownloadUrlRequest {
    FileIds = { fileId }
});
return response.FileUrls[0].Url;

// Несколько файлов
var response = await FilesAC.GetTempDownloadUrlAsync(new GetTempDownloadUrlRequest {
    FileIds = { fileId1, fileId2, ... }
});
return response.FileUrls.Select(f => f.Url).ToList();
```

---

## 3. Proto API (files_api.proto)

```protobuf
service FilesApi {
  rpc GetUploadUrl(GetUploadUrlRequest) returns(GetUploadUrlResponse);
  rpc GetTempDownloadUrl(GetTempDownloadUrlRequest) returns(GetTempDownloadUrlResponse);
  rpc CheckFileHash(CheckFileHashRequest) returns(CheckFileHashResponse);
  rpc GetUserStorageInfo(GetUserStorageInfoRequest) returns(GetUserStorageInfoResponse);
}

message GetUploadUrlRequest {
  UploadFileType file_type = 1;
}

message GetUploadUrlResponse {
  string url = 1;        // URL для загрузки файла
  string file_id = 2;    // ID файла для использования в sendMessage
}

enum UploadFileType {
  UPLOAD_FILE_TYPE_UNKNOWN = 0;
  USER_AVATAR = 1;
  MESSAGE_ATTACHMENT_IMAGE = 2;
  MESSAGE_ATTACHMENT_VIDEO = 3;
  MESSAGE_ATTACHMENT_GIF = 4;
  MESSAGE_ATTACHMENT_DOCUMENT = 5;
  CHAT_PICTURE = 6;
}

message GetTempDownloadUrlRequest {
  repeated string file_ids = 1;
}

message GetTempDownloadUrlResponse {
  message DownloadFileData {
    string file_id = 1;
    string url = 2;
    string preview_url = 3;  // URL превью (для видео)
  }
  repeated DownloadFileData file_urls = 1;
}

message CheckFileHashRequest {
  string file_hash = 1;  // SHA256 hex string (lowercase!)
}

message CheckFileHashResponse {
  string file_id = 1;    // Пустая строка если файл не найден
}

message GetUserStorageInfoRequest {}

message GetUserStorageInfoResponse {
  int64 total_used_storage = 1;
  int64 storage_limit = 2;
}
```

---

## 4. Реализация FilesRepository

### 4.1 Путь к файлу
`Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Repositories/FilesRepository.swift`

### 4.2 Обновление FileType enum

**Важно:** Типы файлов должны точно соответствовать proto enum!

```swift
// В BFNetworking/DTOs.swift или отдельном файле

public enum UploadFileType: Sendable, Codable {
    case unknown
    case userAvatar
    case messageAttachmentImage
    case messageAttachmentVideo
    case messageAttachmentGif
    case messageAttachmentDocument
    case chatPicture

    /// Определить тип по расширению файла
    public static func from(extension ext: String) -> UploadFileType {
        let ext = ext.lowercased()
        switch ext {
        case "jpg", "jpeg", "png", "webp", "heic", "bmp":
            return .messageAttachmentImage
        case "gif":
            return .messageAttachmentGif
        case "mp4", "mov", "avi", "mkv", "webm":
            return .messageAttachmentVideo
        default:
            return .messageAttachmentDocument
        }
    }
}
```

### 4.3 Полный код FilesRepository

```swift
//
//  FilesRepository.swift
//  BFNetworking
//
//  Реализация репозитория файлов
//  Референс: WPF WebApiFileManager.cs
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import CryptoKit

public actor FilesRepository: FilesRepositoryProtocol {
    private let connectionManager: ConnectionManager
    private let httpClient: URLSession

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
        self.httpClient = URLSession.shared
    }

    // MARK: - GetUploadURL

    public func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo {
        var request = Barkfluff_Files_GetUploadUrlRequest()
        request.type = mapFileType(fileType)
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getUploadUrl(req)

                return FileUploadInfo(
                    fileID: response.fileID,
                    uploadURL: response.url,
                    expiresIn: 3600  // По умолчанию 1 час
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - UploadFile (полный флоу с дедупликацией!)

    /// Загружает файл с проверкой дедупликации по хешу
    /// - Parameters:
    ///   - data: Данные файла
    ///   - fileName: Имя файла (для определения типа и Content-Type)
    ///   - fileType: Тип файла (если nil - определяется автоматически)
    /// - Returns: FileID для использования в sendMessage
    public func uploadFile(
        data: Data,
        fileName: String,
        fileType: UploadFileType? = nil
    ) async throws -> String {
        // 1. Определить тип файла
        let ext = (fileName as NSString).pathExtension
        let resolvedType = fileType ?? UploadFileType.from(extension: ext)

        // 2. Проверить лимит хранилища (опционально, можно пропустить при ошибке)
        do {
            let storageInfo = try await getUserStorageInfo()
            let availableSpace = storageInfo.limitBytes - storageInfo.usedBytes
            if Int64(data.count) > availableSpace {
                throw BFNetworkingError.storageLimitExceeded(
                    "Недостаточно места в хранилище. Удалите ненужные файлы."
                )
            }
        } catch {
            // Продолжаем даже если не удалось проверить лимит
            print("Storage check failed, proceeding with upload: \(error)")
        }

        // 3. Вычислить SHA256 хеш
        let fileHash = Self.computeSHA256(data: data)

        // 4. Проверить дедупликацию - ВАЖНО!
        do {
            let checkResult = try await checkFileHash(hash: fileHash)
            if checkResult.exists, let existingFileID = checkResult.fileID {
                // Файл уже есть на сервере - НЕ загружаем повторно!
                print("File already exists on server, reusing fileID: \(existingFileID)")
                return existingFileID
            }
        } catch {
            // Продолжаем даже если не удалось проверить хеш
            print("Hash check failed, proceeding with upload: \(error)")
        }

        // 5. Получить URL для загрузки
        let uploadInfo = try await getUploadURL(fileType: resolvedType)

        // 6. Загрузить файл через HTTP POST с MultipartFormData
        guard let uploadURL = URL(string: uploadInfo.uploadURL) else {
            throw BFNetworkingError.invalidURL("Invalid upload URL")
        }

        let sanitizedFileName = Self.sanitizeFileName(fileName)
        let contentType = Self.contentType(for: ext)

        var request = URLRequest(url: uploadURL)
        request.httpMethod = "POST"

        // Создаем multipart/form-data
        let boundary = "Boundary-\(UUID().uuidString)"
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")

        var body = Data()
        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"file\"; filename=\"\(sanitizedFileName)\"\r\n".data(using: .utf8)!)
        body.append("Content-Type: \(contentType)\r\n\r\n".data(using: .utf8)!)
        body.append(data)
        body.append("\r\n--\(boundary)--\r\n".data(using: .utf8)!)

        request.httpBody = body

        let (_, response) = try await httpClient.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse else {
            throw BFNetworkingError.networkError("Invalid response")
        }

        guard (200...299).contains(httpResponse.statusCode) else {
            let responseBody = String(data: body, encoding: .utf8) ?? "Unknown error"
            throw BFNetworkingError.networkError(
                "Upload failed with status \(httpResponse.statusCode): \(responseBody)"
            )
        }

        // 7. Возвращаем fileID из GetUploadUrlResponse
        return uploadInfo.fileID
    }

    // MARK: - GetTempDownloadURL

    public func getTempDownloadURL(fileID: String) async throws -> String {
        var request = Barkfluff_Files_GetTempDownloadUrlRequest()
        request.fileIds = [fileID]
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getTempDownloadUrl(req)

                guard let fileData = response.fileUrls.first else {
                    throw BFNetworkingError.notFound("File not found: \(fileID)")
                }

                return fileData.url
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - GetMultipleDownloadURLs

    public func getTempDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo] {
        guard !fileIDs.isEmpty else { return [] }

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

    // MARK: - CheckFileHash

    public func checkFileHash(hash: String) async throws -> FileCheckResult {
        var request = Barkfluff_Files_CheckFileHashRequest()
        request.fileHash = hash
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.checkFileHash(req)

                let exists = !response.fileID.isEmpty
                return FileCheckResult(
                    exists: exists,
                    fileID: exists ? response.fileID : nil
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - GetUserStorageInfo

    public func getUserStorageInfo() async throws -> StorageInfo {
        let request = Barkfluff_Files_GetUserStorageInfoRequest()
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .files) { client in
                let filesClient = Barkfluff_Files_FilesApi.Client(wrapping: client)
                let response = try await filesClient.getUserStorageInfo(req)

                return StorageInfo(
                    usedBytes: response.totalUsedStorage,
                    limitBytes: response.storageLimit
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Private Helpers

    private nonisolated func mapFileType(_ type: UploadFileType) -> Barkfluff_Files_UploadFileType {
        switch type {
        case .unknown:
            return .uploadFileTypeUnknown
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
        }
    }

    /// Вычисление SHA256 хеша данных (lowercase hex string)
    private nonisolated static func computeSHA256(data: Data) -> String {
        let hash = SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }

    /// Санитизация имени файла (удаление спецсимволов)
    private nonisolated static func sanitizeFileName(_ fileName: String) -> String {
        // Удаляем все кроме букв, цифр, точек и дефисов
        let regex = try? NSRegularExpression(pattern: "[^\\w\\.-]", options: [])
        let range = NSRange(fileName.startIndex..., in: fileName)
        return regex?.stringByReplacingMatches(in: fileName, options: [], range: range, withTemplate: "_") ?? fileName
    }

    /// Определение Content-Type по расширению
    private nonisolated static func contentType(for extension ext: String) -> String {
        switch ext.lowercased() {
        case "jpg", "jpeg":
            return "image/jpeg"
        case "png":
            return "image/png"
        case "gif":
            return "image/gif"
        case "webp":
            return "image/webp"
        case "mp4":
            return "video/mp4"
        case "webm":
            return "video/webm"
        case "avi":
            return "video/x-msvideo"
        case "mov":
            return "video/quicktime"
        case "mkv":
            return "video/x-matroska"
        default:
            return "application/octet-stream"
        }
    }
}

// MARK: - Additional DTOs

/// Информация о файле для скачивания
public struct FileDownloadInfo: Sendable {
    public let fileID: String
    public let url: String
    public let previewURL: String?

    public init(fileID: String, url: String, previewURL: String?) {
        self.fileID = fileID
        self.url = url
        self.previewURL = previewURL
    }
}
```

---

## 5. Обновление DTOs.swift

Добавить в `BFNetworking/Sources/BFNetworking/DTOs.swift`:

```swift
// MARK: - Files

public struct FileUploadInfo: Sendable {
    public let fileID: String
    public let uploadURL: String
    public let expiresIn: Int64

    public init(fileID: String, uploadURL: String, expiresIn: Int64) {
        self.fileID = fileID
        self.uploadURL = uploadURL
        self.expiresIn = expiresIn
    }
}

public struct FileCheckResult: Sendable {
    public let exists: Bool
    public let fileID: String?

    public init(exists: Bool, fileID: String?) {
        self.exists = exists
        self.fileID = fileID
    }
}

public struct StorageInfo: Sendable {
    public let usedBytes: Int64
    public let limitBytes: Int64

    public init(usedBytes: Int64, limitBytes: Int64) {
        self.usedBytes = usedBytes
        self.limitBytes = limitBytes
    }

    public var availableBytes: Int64 {
        max(0, limitBytes - usedBytes)
    }
}

/// Типы файлов для загрузки (соответствуют proto enum)
public enum UploadFileType: Sendable, Codable {
    case unknown
    case userAvatar
    case messageAttachmentImage
    case messageAttachmentVideo
    case messageAttachmentGif
    case messageAttachmentDocument
    case chatPicture

    /// Определить тип по расширению файла
    public static func from(extension ext: String) -> UploadFileType {
        switch ext.lowercased() {
        case "jpg", "jpeg", "png", "webp", "heic", "bmp":
            return .messageAttachmentImage
        case "gif":
            return .messageAttachmentGif
        case "mp4", "mov", "avi", "mkv", "webm":
            return .messageAttachmentVideo
        default:
            return .messageAttachmentDocument
        }
    }
}
```

---

## 6. Обновление RepositoryProtocols.swift

```swift
// В FilesRepositoryProtocol:
public protocol FilesRepositoryProtocol: Sendable {
    /// Получить URL для загрузки файла
    func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo

    /// Загрузить файл с проверкой дедупликации по хешу
    /// - Returns: fileID для использования в sendMessage
    func uploadFile(data: Data, fileName: String, fileType: UploadFileType?) async throws -> String

    /// Получить временный URL для скачивания файла
    func getTempDownloadURL(fileID: String) async throws -> String

    /// Получить URL для нескольких файлов одновременно
    func getTempDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo]

    /// Проверить, существует ли файл с таким хешем
    func checkFileHash(hash: String) async throws -> FileCheckResult

    /// Получить информацию о хранилище пользователя
    func getUserStorageInfo() async throws -> StorageInfo
}
```

---

## 7. Обновление FileService (BFCore)

Обновить `BFCore/Sources/BFCore/Services/Implementations/FileService.swift`:

```swift
//
//  FileService.swift
//  BFCore
//
//  Реализация сервиса файлов
//  Референс: WPF WebApiFileManager.cs
//

import Foundation
import BFNetworking

/// Реализация сервиса файлов
public actor FileService: FileServiceProtocol {

    private let filesRepository: FilesRepositoryProtocol

    public init(filesRepository: FilesRepositoryProtocol) {
        self.filesRepository = filesRepository
    }

    // MARK: - FileServiceProtocol

    public func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo {
        try await filesRepository.getUploadURL(fileType: fileType)
    }

    /// Загрузка файла с автоматической дедупликацией
    /// - Parameters:
    ///   - url: URL для загрузки (полученный через getUploadURL)
    ///   - data: Данные файла
    ///   - fileName: Имя файла
    ///   - fileType: Тип файла
    /// - Returns: fileID для использования в sendMessage
    public func uploadFile(
        data: Data,
        fileName: String,
        fileType: UploadFileType? = nil
    ) async throws -> String {
        try await filesRepository.uploadFile(data: data, fileName: fileName, fileType: fileType)
    }

    public func getDownloadURL(fileID: String) async throws -> String {
        try await filesRepository.getTempDownloadURL(fileID: fileID)
    }

    public func getDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo] {
        try await filesRepository.getTempDownloadURLs(fileIDs: fileIDs)
    }

    public func checkFileHash(hash: String) async throws -> FileCheckResult {
        try await filesRepository.checkFileHash(hash: hash)
    }

    public func getStorageInfo() async throws -> StorageInfo {
        try await filesRepository.getUserStorageInfo()
    }

    /// Вычислить SHA256 хеш данных (для проверки дедупликации вручную)
    public nonisolated static func computeHash(data: Data) -> String {
        let hash = CryptoKit.SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }
}
```

---

## 8. Обновление FileServiceProtocol

```swift
// BFCore/Sources/BFCore/Services/Protocols/FileServiceProtocol.swift

public protocol FileServiceProtocol: Sendable {
    func getUploadURL(fileType: UploadFileType) async throws -> FileUploadInfo

    /// Загрузить файл с автоматической дедупликацией по хешу
    func uploadFile(data: Data, fileName: String, fileType: UploadFileType?) async throws -> String

    func getDownloadURL(fileID: String) async throws -> String
    func getDownloadURLs(fileIDs: [String]) async throws -> [FileDownloadInfo]
    func checkFileHash(hash: String) async throws -> FileCheckResult
    func getStorageInfo() async throws -> StorageInfo
}
```

---

## 9. Критерии приёмки

- [ ] `getUploadURL` возвращает корректный URL и fileID
- [ ] `uploadFile` проверяет лимит хранилища
- [ ] `uploadFile` вычисляет SHA256 хеш
- [ ] `uploadFile` проверяет CheckFileHash - если файл есть, НЕ загружает повторно
- [ ] `uploadFile` использует правильный Content-Type по расширению
- [ ] `uploadFile` использует multipart/form-data
- [ ] `uploadFile` санитизирует имя файла
- [ ] `uploadFile` возвращает fileID из GetUploadUrlResponse
- [ ] `getDownloadURL` возвращает временный URL для скачивания
- [ ] `getDownloadURLs` работает для нескольких файлов
- [ ] `checkFileHash` корректно проверяет хеш
- [ ] `getStorageInfo` возвращает информацию о хранилище
- [ ] Все ошибки корректно мапятся в BFNetworkingError

---

## 10. Зависимости

Этот этап должен быть выполнен **ПЕРЕД**:
- Этап 2: Отображение вложений
- Этап 3: Отправка сообщений с вложениями

---

## 11. Тестирование

### Ручное тестирование:
1. Загрузить изображение через FileService
2. Проверить что повторная загрузка того же файла не отправляет данные (дедупликация)
3. Получить URL для скачивания
4. Проверить что файл доступен по URL

### Unit тесты:
- Мокирование gRPC клиента
- Проверка вычисления SHA256 хеша
- Проверка маппинга ошибок
- Проверка дедупликации
