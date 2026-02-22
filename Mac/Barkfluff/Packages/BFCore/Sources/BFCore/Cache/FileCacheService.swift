//
//  FileCacheService.swift
//  BFCore
//
//  Сервис кеширования файлов на диск
//

import Foundation

/// Тип кешируемого файла
public enum CachedFileType: String, Sendable {
    case avatar
    case image
    case video
    case gif
    case document
    case audio
    case preview
}

/// Сервис кеширования файлов на диск с поддержкой директорий по типам
public actor FileCacheService: Sendable {

    // MARK: - Properties

    /// Базовая директория кеша
    private let cacheDirectory: URL

    /// Файловый менеджер
    private let fileManager: FileManager

    /// Callback при кешировании файла
    public var onFileCached: ((String, URL, CachedFileType) -> Void)?

    // MARK: - Init

    public init() {
        self.fileManager = FileManager.default
        self.cacheDirectory = fileManager.urls(for: .cachesDirectory, in: .userDomainMask).first!
            .appendingPathComponent("BarkFluff", isDirectory: true)

        // Создаём поддиректории асинхронно
        Task {
            await createCacheDirectories()
        }
    }

    // MARK: - Public Methods

    /// Получить путь к закешированному файлу (синхронно, если файл существует)
    /// - Parameters:
    ///   - fileID: ID файла
    ///   - type: Тип файла
    ///   - providedURL: Опциональный URL (если уже известен)
    /// - Returns: URL закешированного файла или nil
    public func getCachedFilePath(fileID: String, type: CachedFileType, providedURL: String? = nil) -> URL? {
        let cachedURL = fileURL(for: fileID, type: type)
        if fileManager.fileExists(atPath: cachedURL.path) {
            return cachedURL
        }
        return nil
    }

    /// Получить путь к закешированному файлу (асинхронно, скачивает если нужно)
    /// - Parameters:
    ///   - fileID: ID файла
    ///   - type: Тип файла
    ///   - providedURL: Опциональный URL для скачивания
    ///   - fileService: Сервис для получения URL скачивания
    /// - Returns: URL закешированного файла
    public func getCachedFilePathAsync(
        fileID: String,
        type: CachedFileType,
        providedURL: String? = nil,
        fileService: FileServiceProtocol? = nil
    ) async throws -> URL {
        // Сначала проверяем кеш
        if let cachedURL = getCachedFilePath(fileID: fileID, type: type) {
            return cachedURL
        }

        // Получаем URL для скачивания
        guard let downloadURLString = providedURL else {
            guard let fileService = fileService else {
                throw FileCacheError.downloadURLNotProvided
            }
            let url = try await fileService.getDownloadURL(fileID: fileID)
            return try await downloadAndCache(urlString: url, fileID: fileID, type: type)
        }

        return try await downloadAndCache(urlString: downloadURLString, fileID: fileID, type: type)
    }

    /// Проверить, есть ли файл в кеше
    /// - Parameter fileID: ID файла
    /// - Returns: true если файл закеширован
    public func isFileCached(fileID: String, type: CachedFileType) -> Bool {
        let cachedURL = fileURL(for: fileID, type: type)
        return fileManager.fileExists(atPath: cachedURL.path)
    }

    /// Очистить кеш определённого типа
    /// - Parameter type: Тип файлов для очистки (nil = все)
    public func clearCache(type: CachedFileType? = nil) {
        if let type = type {
            let dir = directoryURL(for: type)
            try? fileManager.removeItem(at: dir)
            try? fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
        } else {
            for t in [CachedFileType.avatar, .image, .video, .gif, .document, .audio, .preview] {
                let dir = directoryURL(for: t)
                try? fileManager.removeItem(at: dir)
                try? fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
            }
        }
    }

    /// Получить размер кеша в байтах
    public func getCacheSize() -> Int64 {
        var totalSize: Int64 = 0

        for type in [CachedFileType.avatar, .image, .video, .gif, .document, .audio, .preview] {
            let dir = directoryURL(for: type)
            if let enumerator = fileManager.enumerator(at: dir, includingPropertiesForKeys: [.fileSizeKey]) {
                for case let fileURL as URL in enumerator {
                    if let attrs = try? fileURL.resourceValues(forKeys: [.fileSizeKey]),
                       let fileSize = attrs.fileSize {
                        totalSize += Int64(fileSize)
                    }
                }
            }
        }

        return totalSize
    }

    /// Скачать и закешировать файл
    public func downloadAndCache(urlString: String, fileID: String, type: CachedFileType) async throws -> URL {
        guard let url = URL(string: urlString) else {
            throw FileCacheError.invalidURL
        }

        let (data, response) = try await URLSession.shared.data(from: url)

        guard let httpResponse = response as? HTTPURLResponse,
              (200...299).contains(httpResponse.statusCode) else {
            throw FileCacheError.downloadFailed
        }

        let destinationURL = fileURL(for: fileID, type: type)
        try data.write(to: destinationURL, options: .atomic)

        // Уведомляем о кешировании
        onFileCached?(fileID, destinationURL, type)

        return destinationURL
    }

    /// Сохранить данные в кеш напрямую
    public func saveToCache(data: Data, fileID: String, type: CachedFileType) throws -> URL {
        let destinationURL = fileURL(for: fileID, type: type)
        try data.write(to: destinationURL, options: .atomic)
        onFileCached?(fileID, destinationURL, type)
        return destinationURL
    }

    // MARK: - Private Methods

    private func createCacheDirectories() {
        for type in [CachedFileType.avatar, .image, .video, .gif, .document, .audio, .preview] {
            let dir = directoryURL(for: type)
            try? fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
        }
    }

    private func directoryURL(for type: CachedFileType) -> URL {
        cacheDirectory.appendingPathComponent(type.rawValue + "s", isDirectory: true)
    }

    private func fileURL(for fileID: String, type: CachedFileType) -> URL {
        // Используем fileID как имя файла (предполагаем что fileID уже содержит расширение или оно уникально)
        directoryURL(for: type).appendingPathComponent(fileID)
    }
}

// MARK: - Errors

public enum FileCacheError: Error, LocalizedError {
    case downloadURLNotProvided
    case invalidURL
    case downloadFailed
    case fileNotFound

    public var errorDescription: String? {
        switch self {
        case .downloadURLNotProvided:
            return "URL для скачивания не предоставлен"
        case .invalidURL:
            return "Некорректный URL"
        case .downloadFailed:
            return "Ошибка скачивания файла"
        case .fileNotFound:
            return "Файл не найден"
        }
    }
}
