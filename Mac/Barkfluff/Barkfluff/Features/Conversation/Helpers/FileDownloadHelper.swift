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
    ///   - fileService: Сервис файлов
    static func downloadToDownloads(
        fileID: String,
        fileName: String,
        fileService: FileServiceProtocol
    ) async throws {
        // Сначала скачиваем во временную папку
        let tempURL = try await downloadToTemp(
            fileID: fileID,
            fileName: fileName,
            fileService: fileService
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

    /// Скачать файл во временную папку и вернуть URL (для шаринга)
    /// - Parameters:
    ///   - fileID: ID файла на сервере
    ///   - fileName: Имя файла
    ///   - fileService: Сервис файлов
    /// - Returns: URL скачанного файла
    static func downloadToTemp(
        fileID: String,
        fileName: String,
        fileService: FileServiceProtocol
    ) async throws -> URL {
        let urlString = try await fileService.getDownloadURL(fileID: fileID)
        guard let url = URL(string: urlString) else {
            throw DownloadError.invalidURL
        }

        let (data, _) = try await URLSession.shared.data(from: url)

        let tempDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("BarkfluffShare", isDirectory: true)
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        let destinationURL = tempDir.appendingPathComponent(fileName)

        // Удалить старый если есть
        try? FileManager.default.removeItem(at: destinationURL)
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
