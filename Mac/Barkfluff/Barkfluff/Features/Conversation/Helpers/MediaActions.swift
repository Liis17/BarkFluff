//
//  MediaActions.swift
//  Barkfluff
//
//  Хелперы для медиа-действий из контекстного меню сообщений:
//  копирование изображения в pasteboard, batch-сохранение в ~/Downloads/BarkFluff/.
//

import Foundation
import BFCore
import AppKit
import UniformTypeIdentifiers

@MainActor
enum MediaActions {

    // MARK: - Public API

    /// Скопировать изображение в системный буфер обмена. Если файл ещё не в локальном
    /// кеше — сначала скачает его через `mediaCacheManager`.
    static func copyImageToPasteboard(_ attachment: MessageAttachment, container: DependencyContainer) async {
        do {
            let cacheURL = try await container.mediaCacheManager.resolveURL(
                for: attachment.fileID,
                type: attachment.type.cacheType
            )
            guard let img = NSImage(contentsOf: cacheURL) else { return }
            NSPasteboard.general.clearContents()
            NSPasteboard.general.writeObjects([img])
        } catch {
            // Тихий фейл — не ломаем UI.
        }
    }

    /// Сохранить одно или несколько изображений в `~/Downloads/BarkFluff/`.
    static func saveImages(_ attachments: [MessageAttachment], container: DependencyContainer) async {
        await saveAll(
            attachments,
            container: container,
            defaultExtensionForUnnamed: "jpg"
        )
    }

    /// Сохранить документы / аудио в `~/Downloads/BarkFluff/`.
    static func saveDocuments(_ attachments: [MessageAttachment], container: DependencyContainer) async {
        await saveAll(
            attachments,
            container: container,
            defaultExtensionForUnnamed: nil
        )
    }

    // MARK: - Private

    private static func saveAll(
        _ attachments: [MessageAttachment],
        container: DependencyContainer,
        defaultExtensionForUnnamed: String?
    ) async {
        guard !attachments.isEmpty else { return }
        guard let folderURL = ensureDownloadsFolder() else { return }

        var savedURLs: [URL] = []
        for attachment in attachments {
            do {
                let cacheURL = try await container.mediaCacheManager.resolveURL(
                    for: attachment.fileID,
                    type: attachment.type.cacheType
                )
                let safeName = makeFileName(
                    original: attachment.fileName,
                    attachmentID: attachment.id,
                    defaultExt: defaultExtensionForUnnamed
                )
                let destination = uniqueURL(in: folderURL, name: safeName)
                try FileManager.default.copyItem(at: cacheURL, to: destination)
                savedURLs.append(destination)
            } catch {
                // Один битый файл не должен прерывать остальной batch.
                continue
            }
        }

        if !savedURLs.isEmpty {
            // Открыть Finder с выделением: одного файла или папки целиком.
            if savedURLs.count == 1, let url = savedURLs.first {
                NSWorkspace.shared.activateFileViewerSelecting([url])
            } else {
                NSWorkspace.shared.activateFileViewerSelecting([folderURL])
            }
        }
    }

    /// Вернуть `~/Downloads/BarkFluff/`, создав папку при необходимости.
    private static func ensureDownloadsFolder() -> URL? {
        guard let downloads = try? FileManager.default.url(
            for: .downloadsDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        ) else { return nil }
        let folder = downloads.appendingPathComponent("BarkFluff", isDirectory: true)
        if !FileManager.default.fileExists(atPath: folder.path) {
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        }
        return folder
    }

    /// Сформировать безопасное имя файла. Если у вложения нет имени — fallback с id.
    private static func makeFileName(original: String, attachmentID: Int64, defaultExt: String?) -> String {
        if !original.isEmpty { return original }
        let ext = defaultExt.map { ".\($0)" } ?? ""
        return "file_\(attachmentID)\(ext)"
    }

    /// Если файл с таким именем уже существует — добавить суффикс (2), (3), ...
    private static func uniqueURL(in folder: URL, name: String) -> URL {
        let candidate = folder.appendingPathComponent(name)
        if !FileManager.default.fileExists(atPath: candidate.path) {
            return candidate
        }
        let stem = (name as NSString).deletingPathExtension
        let ext = (name as NSString).pathExtension
        for i in 2...999 {
            let suffix = ext.isEmpty ? "\(stem) (\(i))" : "\(stem) (\(i)).\(ext)"
            let url = folder.appendingPathComponent(suffix)
            if !FileManager.default.fileExists(atPath: url.path) {
                return url
            }
        }
        return candidate // На случай >999 коллизий — перезапишем (теоретически).
    }
}
