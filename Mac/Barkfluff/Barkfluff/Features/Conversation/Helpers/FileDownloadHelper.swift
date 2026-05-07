//
//  FileDownloadHelper.swift
//  Barkfluff
//
//  Утилита для скачивания файлов через NSSavePanel и во временную папку
//

import Foundation
import BFCore
import AppKit

/// Хелпер для скачивания файлов
@MainActor
enum FileDownloadHelper {

    /// Скачать файл через NSSavePanel (работает в sandbox)
    /// - Parameters:
    ///   - fileID: ID файла на сервере
    ///   - fileName: Имя файла для сохранения
    ///   - fileService: Сервис файлов (используется как fallback)
    ///   - mediaCacheManager: Кеш-менеджер; если есть — берём оригинал из локального кеша.
    static func downloadToDownloads(
        fileID: String,
        fileName: String,
        fileService: FileServiceProtocol,
        mediaCacheManager: MediaCacheManager? = nil,
        cacheType: CachedFileType = .document
    ) async throws {
        // Сначала скачиваем во временную папку
        let tempURL = try await downloadToTemp(
            fileID: fileID,
            fileName: fileName,
            fileService: fileService,
            mediaCacheManager: mediaCacheManager,
            cacheType: cacheType
        )

        // Показываем NSSavePanel
        let panel = NSSavePanel()
        panel.nameFieldStringValue = fileName
        panel.canCreateDirectories = true
        panel.isExtensionHidden = false

        // Если viewer открыт — понижаем его level, чтобы панель была видна
        let viewerWindow = FullScreenMediaWindowManager.shared.currentWindow
        let savedLevel = viewerWindow?.level
        if viewerWindow != nil {
            viewerWindow?.level = .floating
        }

        let result = panel.runModal()

        // Возвращаем level обратно
        if let viewerWindow, let savedLevel {
            viewerWindow.level = savedLevel
        }

        guard result == .OK, let destinationURL = panel.url else {
            // Пользователь отменил — удаляем temp файл
            try? FileManager.default.removeItem(at: tempURL)
            return
        }

        // Копируем из temp в выбранное место
        try? FileManager.default.removeItem(at: destinationURL)
        try FileManager.default.copyItem(at: tempURL, to: destinationURL)
        try? FileManager.default.removeItem(at: tempURL)

        // Показать в Finder
        NSWorkspace.shared.activateFileViewerSelecting([destinationURL])
    }

    /// Скачать файл во временную папку и вернуть URL (для шаринга/сохранения).
    /// Если передан mediaCacheManager — сначала пытается взять файл из кеша
    /// (или скачать его в кеш), затем копирует на правильное имя в temp.
    static func downloadToTemp(
        fileID: String,
        fileName: String,
        fileService: FileServiceProtocol,
        mediaCacheManager: MediaCacheManager? = nil,
        cacheType: CachedFileType = .document
    ) async throws -> URL {
        let tempDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("BarkfluffShare", isDirectory: true)
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        let destinationURL = tempDir.appendingPathComponent(fileName)
        try? FileManager.default.removeItem(at: destinationURL)

        if let cache = mediaCacheManager {
            // Кеш отдаёт URL вида .../{type}s/{fileID-with-slashes-replaced},
            // поэтому копируем в temp с человеко-читаемым именем.
            let cachedURL = try await cache.resolveURL(for: fileID, type: cacheType)
            try FileManager.default.copyItem(at: cachedURL, to: destinationURL)
            return destinationURL
        }

        // Fallback без кеша — прямое скачивание.
        let urlString = try await fileService.getDownloadURL(fileID: fileID)
        guard let url = URL(string: urlString) else {
            throw DownloadError.invalidURL
        }
        let (data, _) = try await URLSession.shared.data(from: url)
        try data.write(to: destinationURL)
        return destinationURL
    }

    enum DownloadError: LocalizedError {
        case invalidURL

        var errorDescription: String? {
            switch self {
            case .invalidURL:
                return "Некорректная ссылка для скачивания"
            }
        }
    }
}

extension AttachmentType {
    /// Тип кеша для оригинала вложения.
    var cacheType: CachedFileType {
        switch self {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        case .audio: return .audio
        case .voice: return .voice
        case .sticker: return .sticker
        case .forwardedMessage: return .document
        }
    }
}
