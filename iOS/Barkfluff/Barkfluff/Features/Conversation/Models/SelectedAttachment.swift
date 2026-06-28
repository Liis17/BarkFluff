//
//  SelectedAttachment.swift
//  Barkfluff
//
//  Модель вложения, выбранного пользователем перед отправкой (iOS версия)
//

import Foundation
import UIKit
import PhotosUI
import BFNetworking

/// Вложение, выбранное пользователем перед отправкой
enum SelectedAttachment: Identifiable, Equatable, Sendable {
    /// Файл по URL (из fileImporter или Drag & Drop)
    /// - Parameters:
    ///   - url: URL файла
    ///   - forceAsDocument: Если true, отправить как документ (без сжатия/превью)
    ///   - id: Уникальный идентификатор
    case fileURL(url: URL, forceAsDocument: Bool = false, id: UUID = UUID())

    /// Изображение из буфера обмена или PhotosPicker
    case imageData(data: Data, fileName: String, id: UUID = UUID())

    /// Видео данные с превью
    case videoData(data: Data, preview: UIImage?, fileName: String, id: UUID = UUID())

    var id: UUID {
        switch self {
        case .fileURL(_, _, let id): return id
        case .imageData(_, _, let id): return id
        case .videoData(_, _, _, let id): return id
        }
    }

    /// Имя файла для отображения
    var displayName: String {
        switch self {
        case .fileURL(let url, _, _):
            return url.lastPathComponent
        case .imageData(_, let name, _):
            return name
        case .videoData(_, _, let name, _):
            return name
        }
    }

    /// Расширение файла
    var fileExtension: String {
        switch self {
        case .fileURL(let url, _, _):
            return url.pathExtension.lowercased()
        case .imageData:
            return "jpg"
        case .videoData:
            return "mp4"
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
        case .videoData(let data, _, _, _):
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
        case .videoData(let data, _, _, _):
            return data
        }
    }

    /// Превью изображение (для UI)
    var previewImage: UIImage? {
        switch self {
        case .fileURL(let url, _, _):
            if isVideo {
                // Для видео файлов генерируем превью
                let asset = AVURLAsset(url: url)
                let generator = AVAssetImageGenerator(asset: asset)
                generator.appliesPreferredTrackTransform = true
                if let cgImage = try? generator.copyCGImage(at: .zero, actualTime: nil) {
                    return UIImage(cgImage: cgImage)
                }
                return nil
            } else if isMedia {
                return UIImage(contentsOfFile: url.path)
            }
            return nil
        case .imageData(let data, _, _):
            return UIImage(data: data)
        case .videoData(_, let preview, _, _):
            return preview
        }
    }

    /// UploadFileType для сервера
    /// Если forceAsDocument = true, отправляем как документ (без сжатия)
    var uploadFileType: UploadFileType {
        switch self {
        case .fileURL(_, let forceAsDocument, _):
            if forceAsDocument {
                return .messageAttachmentDocument
            }
            return UploadFileType.from(extension: fileExtension)
        case .imageData:
            return .messageAttachmentImage
        case .videoData:
            return .messageAttachmentVideo
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
    case videoExportFailed

    var errorDescription: String? {
        switch self {
        case .accessDenied:
            return String(localized: "conversation.errors.file_access_denied")
        case .fileTooLarge(let max):
            let formatter = ByteCountFormatter()
            return String(localized: "conversation.errors.file_too_large \(formatter.string(fromByteCount: max))")
        case .imageConversionFailed:
            return String(localized: "conversation.errors.image_conversion_failed")
        case .videoExportFailed:
            return String(localized: "conversation.errors.video_export_failed")
        }
    }
}

// MARK: - AVFoundation Import

import AVFoundation
