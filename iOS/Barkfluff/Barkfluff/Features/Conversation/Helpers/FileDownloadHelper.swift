//
//  FileDownloadHelper.swift
//  Barkfluff (iOS)
//
//  Утилита для скачивания файлов и шеринга через UIActivityViewController.
//  iOS-замена для NSSavePanel — пользователь выбирает «Save to Files» или «Share».
//

import Foundation
import UIKit
import BFCore

/// Хелпер для скачивания файлов
@MainActor
enum FileDownloadHelper {

    /// Скачать файл и показать share sheet (iOS-замена NSSavePanel).
    /// Пользователь выберет «Сохранить в Файлы» / «Поделиться» / etc.
    static func downloadAndShare(
        fileID: String,
        fileName: String,
        fileService: FileServiceProtocol,
        mediaCacheManager: MediaCacheManager? = nil,
        cacheType: CachedFileType = .document
    ) async throws {
        let tempURL = try await downloadToTemp(
            fileID: fileID,
            fileName: fileName,
            fileService: fileService,
            mediaCacheManager: mediaCacheManager,
            cacheType: cacheType
        )

        await MediaActions.presentShareSheet(items: [tempURL])
    }

    /// Скачать файл во временную папку и вернуть URL (для шаринга/сохранения).
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
            let cachedURL = try await cache.resolveURL(for: fileID, type: cacheType)
            try FileManager.default.copyItem(at: cachedURL, to: destinationURL)
            return destinationURL
        }

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
