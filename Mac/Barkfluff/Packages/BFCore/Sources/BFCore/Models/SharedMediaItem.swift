//
//  SharedMediaItem.swift
//  BFCore
//
//  Модель элемента общего медиа в чате
//

import Foundation

/// Элемент общего медиа (из вложений сообщений)
public struct SharedMediaItem: Identifiable, Hashable, Sendable {
    public let id: Int64                    // ID вложения (MessageAttachment.id)
    public let messageID: Int64             // ID сообщения-источника
    public let type: AttachmentType         // image, video, gif, document
    public let fileID: String               // ID файла в Files сервисе
    public var previewURL: String?          // Прямая ссылка на превью (если есть)
    public let previewFileID: String?       // ID файла превью
    public let fileName: String             // Оригинальное имя файла
    public let fileSize: Int64              // Размер в байтах
    public let sentAt: Date                 // Дата отправки сообщения
    public let senderID: Int64              // ID отправителя

    public init(
        id: Int64,
        messageID: Int64,
        type: AttachmentType,
        fileID: String,
        previewURL: String?,
        previewFileID: String?,
        fileName: String,
        fileSize: Int64,
        sentAt: Date,
        senderID: Int64
    ) {
        self.id = id
        self.messageID = messageID
        self.type = type
        self.fileID = fileID
        self.previewURL = previewURL
        self.previewFileID = previewFileID
        self.fileName = fileName
        self.fileSize = fileSize
        self.sentAt = sentAt
        self.senderID = senderID
    }

    /// Форматированный размер файла
    public var formattedSize: String {
        ByteCountFormatter.string(fromByteCount: fileSize, countStyle: .file)
    }
}
