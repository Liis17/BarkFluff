//
//  Message.swift
//  BFCore
//
//  Доменные модели сообщений
//

import Foundation

/// Состояние отправки сообщения
public enum MessageSendingState: Hashable, Sendable {
    case sent       // Подтверждено сервером (default)
    case sending    // Отправляется
    case failed(String) // Ошибка (с текстом)
}

/// Сообщение в чате
public struct Message: Identifiable, Hashable, Sendable {
    public var id: Int64
    public let chatID: String
    public let senderID: Int64
    public let senderName: String?
    public var content: MessageContent
    public var sentAt: Date
    public var readBy: [Int64]
    public let isSystem: Bool

    /// Состояние отправки (для optimistic UI)
    public var sendingState: MessageSendingState
    /// Локальный ID для pending-сообщений (стабильная SwiftUI identity)
    public var localID: String?
    /// Прогресс загрузки вложений (0.0...1.0)
    public var uploadProgress: Double?

    public init(
        id: Int64,
        chatID: String,
        senderID: Int64,
        senderName: String? = nil,
        content: MessageContent,
        sentAt: Date,
        readBy: [Int64] = [],
        isSystem: Bool = false,
        sendingState: MessageSendingState = .sent,
        localID: String? = nil,
        uploadProgress: Double? = nil
    ) {
        self.id = id
        self.chatID = chatID
        self.senderID = senderID
        self.senderName = senderName
        self.content = content
        self.sentAt = sentAt
        self.readBy = readBy
        self.isSystem = isSystem
        self.sendingState = sendingState
        self.localID = localID
        self.uploadProgress = uploadProgress
    }

    /// Проверить, прочитано ли сообщение пользователем
    public func isReadBy(userID: Int64) -> Bool {
        readBy.contains(userID)
    }
}

/// Контент сообщения
public struct MessageContent: Hashable, Sendable {
    public var text: String
    public var attachments: [MessageAttachment]

    public init(text: String = "", attachments: [MessageAttachment] = []) {
        self.text = text
        self.attachments = attachments
    }

    public var hasText: Bool { !text.isEmpty }
    public var hasAttachments: Bool { !attachments.isEmpty }
    public var isEmpty: Bool { text.isEmpty && attachments.isEmpty }
}

/// Вложение в сообщении
public struct MessageAttachment: Identifiable, Hashable, Sendable {
    public let id: Int64
    public let type: AttachmentType
    public let fileID: String
    public let fileName: String
    public let fileSize: Int64
    public var previewURL: String?
    public var previewFileID: String?

    public init(
        id: Int64,
        type: AttachmentType,
        fileID: String,
        fileName: String,
        fileSize: Int64,
        previewURL: String? = nil,
        previewFileID: String? = nil
    ) {
        self.id = id
        self.type = type
        self.fileID = fileID
        self.fileName = fileName
        self.fileSize = fileSize
        self.previewURL = previewURL
        self.previewFileID = previewFileID
    }

    /// Форматированный размер файла
    public var formattedSize: String {
        ByteCountFormatter.string(fromByteCount: fileSize, countStyle: .file)
    }
}

/// Тип вложения
public enum AttachmentType: String, Sendable, Codable, CaseIterable {
    case image
    case video
    case gif
    case document
    case audio
    case voice
    case sticker

    public var displayName: String {
        switch self {
        case .image: return "Изображение"
        case .video: return "Видео"
        case .gif: return "GIF"
        case .document: return "Документ"
        case .audio: return "Аудио"
        case .voice: return "Голосовое"
        case .sticker: return "Стикер"
        }
    }

    public var systemImage: String {
        switch self {
        case .image, .gif, .sticker: return "photo"
        case .video: return "video"
        case .document: return "doc"
        case .audio, .voice: return "waveform"
        }
    }

    public func previewText(fileName: String = "") -> String {
        switch self {
        case .image: return "\u{1F4F7} Фото"
        case .video: return "\u{1F4F9} Видео"
        case .gif: return "GIF"
        case .document: return "\u{1F4CE} \(fileName.isEmpty ? "Документ" : fileName)"
        case .audio: return "\u{1F3B5} Аудио"
        case .voice: return "\u{1F3A4} Голосовое"
        case .sticker: return "Стикер"
        }
    }
}
