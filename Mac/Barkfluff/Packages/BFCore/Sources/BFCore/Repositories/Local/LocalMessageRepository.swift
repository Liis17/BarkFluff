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

// DTO для атачментов и форвардов вынесены в `Database/DTO/SerializationDTO.swift` —
// общие с `LocalChatRepository` (для сериализации lastMessage чата).
