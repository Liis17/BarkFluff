//
//  LocalMessageRepository.swift
//  BFCore
//
//  Кеш истории сообщений по чатам в SQLite.
//

import Foundation
import GRDB

public actor LocalMessageRepository {

    private let database: Database
    nonisolated private let encoder: JSONEncoder
    nonisolated private let decoder: JSONDecoder

    public init(database: Database) {
        self.database = database
        self.encoder = JSONEncoder()
        self.decoder = JSONDecoder()
    }

    /// Загрузить последние сообщения чата, упорядоченные хронологически (старые → новые).
    public func loadCachedMessages(chatID: String, limit: Int = 200) async throws -> [Message] {
        let records = try await database.dbPool.read { db in
            try CachedMessageRecord
                .filter(CachedMessageRecord.Columns.chatId == chatID)
                .order(CachedMessageRecord.Columns.sentAt.desc)
                .limit(limit)
                .fetchAll(db)
        }
        return records
            .reversed()
            .compactMap { try? makeMessage(from: $0) }
    }

    /// Upsert набора сообщений.
    public func upsertMessages(_ messages: [Message], chatID: String) async throws {
        try await database.dbPool.write { db in
            for message in messages where message.id > 0 {
                try makeRecord(from: message, chatID: chatID).upsert(db)
            }
        }
    }

    /// Записать одно сообщение (для стрима новых сообщений).
    public func append(_ message: Message) async throws {
        guard message.id > 0 else { return }
        try await database.dbPool.write { db in
            try makeRecord(from: message, chatID: message.chatID).upsert(db)
        }
    }

    public func clear(chatID: String) async throws {
        try await database.dbPool.write { db in
            try CachedMessageRecord
                .filter(CachedMessageRecord.Columns.chatId == chatID)
                .deleteAll(db)
        }
    }

    public func clearAll() async throws {
        try await database.dbPool.write { db in
            try CachedMessageRecord.deleteAll(db)
        }
    }

    // MARK: - Mapping

    nonisolated private func makeRecord(from message: Message, chatID: String) throws -> CachedMessageRecord {
        let attachmentsDTO = message.content.attachments.map(MessageAttachmentDTO.init(attachment:))
        let attachmentsData = try encoder.encode(attachmentsDTO)
        let readByData = try encoder.encode(message.readBy)
        return CachedMessageRecord(
            id: message.id,
            chatId: chatID,
            senderId: message.senderID,
            senderName: message.senderName,
            text: message.content.text,
            attachmentsJson: attachmentsData,
            sentAt: Int64(message.sentAt.timeIntervalSince1970 * 1000),
            readByJson: readByData,
            isSystem: message.isSystem
        )
    }

    nonisolated private func makeMessage(from record: CachedMessageRecord) throws -> Message {
        let attachments: [MessageAttachment]
        if let data = record.attachmentsJson {
            let dtos = (try? decoder.decode([MessageAttachmentDTO].self, from: data)) ?? []
            attachments = dtos.map { $0.toAttachment() }
        } else {
            attachments = []
        }
        let readBy: [Int64]
        if let data = record.readByJson {
            readBy = (try? decoder.decode([Int64].self, from: data)) ?? []
        } else {
            readBy = []
        }
        return Message(
            id: record.id,
            chatID: record.chatId,
            senderID: record.senderId,
            senderName: record.senderName,
            content: MessageContent(text: record.text, attachments: attachments),
            sentAt: Date(timeIntervalSince1970: TimeInterval(record.sentAt) / 1000),
            readBy: readBy,
            isSystem: record.isSystem,
            sendingState: .sent
        )
    }
}

// MARK: - DTO

private struct MessageAttachmentDTO: Codable {
    let id: Int64
    let typeRaw: String
    let fileID: String
    let fileName: String
    let fileSize: Int64
    let previewURL: String?
    let previewFileID: String?
    let forwarded: ForwardedMessagePayloadDTO?

    init(attachment: MessageAttachment) {
        self.id = attachment.id
        self.typeRaw = attachment.type.rawValue
        self.fileID = attachment.fileID
        self.fileName = attachment.fileName
        self.fileSize = attachment.fileSize
        self.previewURL = attachment.previewURL
        self.previewFileID = attachment.previewFileID
        self.forwarded = attachment.forwarded.map(ForwardedMessagePayloadDTO.init(payload:))
    }

    func toAttachment() -> MessageAttachment {
        MessageAttachment(
            id: id,
            type: AttachmentType(rawValue: typeRaw) ?? .document,
            fileID: fileID,
            fileName: fileName,
            fileSize: fileSize,
            previewURL: previewURL,
            previewFileID: previewFileID,
            forwarded: forwarded?.toPayload()
        )
    }
}

private struct ForwardedMessagePayloadDTO: Codable {
    let authorName: String
    let originalMessageID: Int64
    let text: String
    let attachments: [MessageAttachmentDTO]

    init(payload: ForwardedMessagePayload) {
        self.authorName = payload.authorName
        self.originalMessageID = payload.originalMessageID
        self.text = payload.text
        self.attachments = payload.attachments.map(MessageAttachmentDTO.init(attachment:))
    }

    func toPayload() -> ForwardedMessagePayload {
        ForwardedMessagePayload(
            authorName: authorName,
            originalMessageID: originalMessageID,
            text: text,
            attachments: attachments.map { $0.toAttachment() }
        )
    }
}
