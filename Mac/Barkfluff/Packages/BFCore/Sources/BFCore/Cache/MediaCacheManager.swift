//
//  MediaCacheManager.swift
//  BFCore
//
//  Единая точка доступа к файлам по fileID. Локальный диск + индекс в SQLite.
//

import Foundation
import GRDB

public enum MediaCacheError: Error, LocalizedError {
    case invalidURL
    case downloadFailed
    case fileMissing

    public var errorDescription: String? { errorDescription(in: .current) }

    public func errorDescription(in locale: Locale) -> String? {
        switch self {
        case .invalidURL:     return String(localized: "bfcore.cache.error.invalid_url", bundle: .module, locale: locale)
        case .downloadFailed: return String(localized: "bfcore.cache.error.download_failed", bundle: .module, locale: locale)
        case .fileMissing:    return String(localized: "bfcore.cache.error.file_missing", bundle: .module, locale: locale)
        }
    }
}

public actor MediaCacheManager {

    // MARK: - Dependencies

    private let database: Database
    private let fileService: FileServiceProtocol
    private let urlSession: URLSession
    private let fileManager: FileManager
    private let cacheRoot: URL

    // MARK: - State

    /// Дедуп параллельных запросов за одним и тем же fileID.
    private var inflight: [String: Task<URL, Error>] = [:]

    // MARK: - Init

    public init(
        database: Database,
        fileService: FileServiceProtocol,
        urlSession: URLSession = .shared,
        fileManager: FileManager = .default,
        cacheRoot: URL? = nil
    ) {
        self.database = database
        self.fileService = fileService
        self.urlSession = urlSession
        self.fileManager = fileManager
        self.cacheRoot = cacheRoot ?? Self.defaultCacheRoot(fileManager: fileManager)
        Task { await self.ensureDirectories() }
    }

    // MARK: - Public API

    /// Локальный URL файла, если он уже на диске. Никаких сетевых запросов.
    public func localURL(for fileID: String, type: CachedFileType) -> URL? {
        let url = fileURL(for: fileID, type: type)
        return fileManager.fileExists(atPath: url.path) ? url : nil
    }

    /// Вернуть локальный URL файла. Если файла нет — скачать и закешировать.
    /// - Parameter presignedURLHint: уже известный presigned URL (например, для аватаров,
    ///   где fileID получен парсингом самого URL и getDownloadURL не сработает).
    public func resolveURL(
        for fileID: String,
        type: CachedFileType,
        presignedURLHint: String? = nil
    ) async throws -> URL {
        if let cached = localURL(for: fileID, type: type) {
            return cached
        }

        if let existing = inflight[fileID] {
            return try await existing.value
        }

        let task = Task<URL, Error> { [fileID, type, presignedURLHint] in
            try await self.download(fileID: fileID, type: type, presignedURLHint: presignedURLHint)
        }
        inflight[fileID] = task
        defer { inflight[fileID] = nil }
        return try await task.value
    }

    /// Прогреть кеш: скачать недостающие файлы пакетом, ошибки игнорируются.
    public func prefetch(fileIDs: [String], type: CachedFileType) async {
        await withTaskGroup(of: Void.self) { group in
            for id in fileIDs where localURL(for: id, type: type) == nil {
                group.addTask {
                    _ = try? await self.resolveURL(for: id, type: type)
                }
            }
        }
    }

    /// Сохранить произвольные данные напрямую (например, после ручной загрузки).
    public func saveToCache(
        data: Data,
        fileID: String,
        type: CachedFileType,
        mimeType: String? = nil,
        originalName: String? = nil
    ) throws -> URL {
        let url = fileURL(for: fileID, type: type)
        try data.write(to: url, options: .atomic)
        try? upsertFileRecord(
            fileID: fileID,
            type: type,
            sizeBytes: Int64(data.count),
            mimeType: mimeType,
            originalName: originalName
        )
        return url
    }

    /// Снимок размеров кеша по типам.
    public func stats() -> CacheStats {
        var bytes: [CachedFileType: Int64] = [:]
        for type in CachedFileType.allCases {
            bytes[type] = directorySize(directoryURL(for: type))
        }
        return CacheStats(bytesByType: bytes)
    }

    /// Очистить кеш указанных типов: удаляет файлы с диска и записи из БД.
    public func clear(types: Set<CachedFileType>) async {
        for type in types {
            let dir = directoryURL(for: type)
            try? fileManager.removeItem(at: dir)
            try? fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
            try? await database.dbPool.write { db in
                try CachedFileRecord
                    .filter(CachedFileRecord.Columns.type == type.rawValue)
                    .deleteAll(db)
            }
        }
    }

    /// Очистить кеш всех типов.
    public func clearAll() async {
        await clear(types: Set(CachedFileType.allCases))
    }

    // MARK: - Private

    private func download(
        fileID: String,
        type: CachedFileType,
        presignedURLHint: String?
    ) async throws -> URL {
        let urlString: String
        if let hint = presignedURLHint, !hint.isEmpty {
            urlString = hint
        } else {
            urlString = try await fileService.getDownloadURL(fileID: fileID)
        }

        guard let remoteURL = URL(string: urlString) else {
            throw MediaCacheError.invalidURL
        }

        let (data, response) = try await urlSession.data(from: remoteURL)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw MediaCacheError.downloadFailed
        }

        let destination = fileURL(for: fileID, type: type)
        try data.write(to: destination, options: .atomic)
        try? upsertFileRecord(
            fileID: fileID,
            type: type,
            sizeBytes: Int64(data.count),
            mimeType: http.mimeType,
            originalName: nil
        )
        return destination
    }

    private func upsertFileRecord(
        fileID: String,
        type: CachedFileType,
        sizeBytes: Int64,
        mimeType: String?,
        originalName: String?
    ) throws {
        let record = CachedFileRecord(
            fileId: fileID,
            type: type.rawValue,
            sizeBytes: sizeBytes,
            mimeType: mimeType,
            originalName: originalName,
            addedAt: Int64(Date().timeIntervalSince1970)
        )
        try database.dbPool.write { db in
            try record.upsert(db)
        }
    }

    private func ensureDirectories() {
        for type in CachedFileType.allCases {
            try? fileManager.createDirectory(
                at: directoryURL(for: type),
                withIntermediateDirectories: true
            )
        }
    }

    private func directoryURL(for type: CachedFileType) -> URL {
        cacheRoot.appendingPathComponent(type.rawValue + "s", isDirectory: true)
    }

    private func fileURL(for fileID: String, type: CachedFileType) -> URL {
        directoryURL(for: type)
            .appendingPathComponent(safeFileName(fileID), isDirectory: false)
    }

    /// fileID может содержать `/` (S3-ключи) — заменяем на безопасный разделитель.
    private func safeFileName(_ fileID: String) -> String {
        fileID
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "\\", with: "_")
    }

    private func directorySize(_ url: URL) -> Int64 {
        guard let enumerator = fileManager.enumerator(
            at: url,
            includingPropertiesForKeys: [.fileSizeKey]
        ) else { return 0 }
        var total: Int64 = 0
        for case let fileURL as URL in enumerator {
            if let size = try? fileURL.resourceValues(forKeys: [.fileSizeKey]).fileSize {
                total += Int64(size)
            }
        }
        return total
    }

    private static func defaultCacheRoot(fileManager: FileManager) -> URL {
        let caches = fileManager.urls(for: .cachesDirectory, in: .userDomainMask).first!
        return caches.appendingPathComponent("BarkFluff", isDirectory: true)
    }
}
