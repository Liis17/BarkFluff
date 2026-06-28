//
//  SelectedAttachment.swift
//  Barkfluff
//
//  Модель вложения, выбранного пользователем перед отправкой
//

import Foundation
import AppKit
import BFNetworking

/// Вложение, выбранное пользователем перед отправкой
enum SelectedAttachment: Identifiable, Equatable, Sendable {
    /// Файл по URL (из fileImporter или Drag & Drop)
    /// - Parameters:
    ///   - url: URL файла
    ///   - forceAsDocument: Если true, отправить как документ (без сжатия/превью)
    ///   - id: Уникальный идентификатор
    case fileURL(url: URL, forceAsDocument: Bool = false, id: UUID = UUID())

    /// Изображение из буфера обмена
    case imageData(data: Data, fileName: String, id: UUID = UUID())

    var id: UUID {
        switch self {
        case .fileURL(_, _, let id): return id
        case .imageData(_, _, let id): return id
        }
    }

    /// Имя файла для отображения
    var displayName: String {
        switch self {
        case .fileURL(let url, _, _):
            return url.lastPathComponent
        case .imageData(_, let name, _):
            return name
        }
    }

    /// Расширение файла
    var fileExtension: String {
        switch self {
        case .fileURL(let url, _, _):
            return url.pathExtension.lowercased()
        case .imageData:
            return "png"
        }
    }

    /// Тип вложения для UI (медиа или документ)
    var isMedia: Bool {
        let ext = fileExtension
        let mediaExtensions = ["jpg", "jpeg", "png", "heic", "webp", "gif", "bmp",
                                "mp4", "mov", "avi", "mkv", "webm"]
        return mediaExtensions.contains(ext)
    }

    /// Это видео?
    var isVideo: Bool {
        let videoExtensions = ["mp4", "mov", "avi", "mkv", "webm"]
        return videoExtensions.contains(fileExtension)
    }

    /// Размер файла в байтах
    var fileSize: Int64? {
        switch self {
        case .fileURL(let url, _, _):
            return (try? FileManager.default.attributesOfItem(atPath: url.path))?[.size] as? Int64
        case .imageData(let data, _, _):
            return Int64(data.count)
        }
    }

    /// Отформатированный размер для отображения
    var formattedSize: String {
        guard let size = fileSize else { return "" }
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        return formatter.string(fromByteCount: size)
    }

    /// Данные для загрузки
    func loadData() throws -> Data {
        switch self {
        case .fileURL(let url, _, _):
            guard url.startAccessingSecurityScopedResource() else {
                throw AttachmentError.accessDenied
            }
            defer { url.stopAccessingSecurityScopedResource() }
            return try Data(contentsOf: url)
        case .imageData(let data, _, _):
            return data
        }
    }

    /// UploadFileType для сервера
    /// Если forceAsDocument = true, отправляем как документ (без сжатия)
    var uploadFileType: UploadFileType {
        switch self {
        case .fileURL(_, let forceAsDocument, _):
            if forceAsDocument {
                print("📎 [SelectedAttachment] forceAsDocument=true, returning DOCUMENT")
                return .messageAttachmentDocument
            }
            let type = UploadFileType.from(extension: fileExtension)
            print("📎 [SelectedAttachment] forceAsDocument=false, ext=\(fileExtension), type=\(type)")
            return type
        case .imageData:
            return UploadFileType.from(extension: fileExtension)
        }
    }

    static func == (lhs: SelectedAttachment, rhs: SelectedAttachment) -> Bool {
        lhs.id == rhs.id
    }
}

// MARK: - AttachmentError

enum AttachmentError: LocalizedError, Sendable {
    case accessDenied
    case fileTooLarge(maxSize: Int64)
    case imageConversionFailed

    var errorDescription: String? {
        switch self {
        case .accessDenied:
            return String(localized: "conversation.errors.file_access_denied")
        case .fileTooLarge(let max):
            let formatter = ByteCountFormatter()
            return String(localized: "conversation.errors.file_too_large \(formatter.string(fromByteCount: max))")
        case .imageConversionFailed:
            return String(localized: "conversation.errors.image_conversion_failed")
        }
    }
}
