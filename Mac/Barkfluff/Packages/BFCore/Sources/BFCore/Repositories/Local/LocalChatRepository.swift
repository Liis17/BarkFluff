//
//  LocalChatRepository.swift
//  BFCore
//
//  Кеш списка чатов в SQLite.
//

import Foundation
import GRDB

public actor LocalChatRepository {

    private let database: Database
    nonisolated private let encoder: JSONEncoder
    nonisolated private let decoder: JSONDecoder

    public init(database: Database) {
        self.database = database
        self.encoder = JSONEncoder()
        self.decoder = JSONDecoder()
    }

    /// Загрузить кешированные чаты, отсортированные по `updatedAt DESC`.
    public func loadCachedChats() async throws -> [Chat] {
        let records = try await database.dbPool.read { db in
            try CachedChatRecord
                .order(CachedChatRecord.Columns.updatedAt.desc)
                .fetchAll(db)
        }
        return records.compactMap { try? makeChat(from: $0) }
    }

    /// Полностью заменить кеш (для cold-load из чат-листа).
    public func replaceAll(_ chats: [Chat]) async throws {
        try await database.dbPool.write { db in
            try CachedChatRecord.deleteAll(db)
            let now = Int64(Date().timeIntervalSince1970)
            for chat in chats {
                try makeRecord(from: chat, updatedAt: now).insert(db)
            }
        }
    }

    /// Upsert набора чатов (для инкрементальных обновлений).
    public func upsertChats(_ chats: [Chat]) async throws {
        try await database.dbPool.write { db in
            let now = Int64(Date().timeIntervalSince1970)
            for chat in chats {
                try makeRecord(from: chat, updatedAt: now).upsert(db)
            }
        }
    }

    public func clear() async throws {
        try await database.dbPool.write { db in
            try CachedChatRecord.deleteAll(db)
        }
    }

    // MARK: - Mapping

    nonisolated private func makeRecord(from chat: Chat, updatedAt: Int64) throws -> CachedChatRecord {
        let membersDTO = chat.members.map(ChatMemberDTO.init(member:))
        let membersJson = try encoder.encode(membersDTO)
        let pictureFileId = S3URLParser.fileID(from: chat.pictureURL)
        return CachedChatRecord(
            id: chat.id,
            title: chat.title,
            pictureUrl: chat.pictureURL,
            pictureFileId: pictureFileId,
            isGroup: chat.isGroupChat,
            lastMessageId: chat.lastMessage?.id,
            unreadCount: chat.unreadCount,
            membersJson: membersJson,
            updatedAt: updatedAt
        )
    }

    nonisolated private func makeChat(from record: CachedChatRecord) throws -> Chat {
        let members: [ChatMember]
        if let data = record.membersJson {
            let dtos = (try? decoder.decode([ChatMemberDTO].self, from: data)) ?? []
            members = dtos.map { $0.toMember() }
        } else {
            members = []
        }
        return Chat(
            id: record.id,
            title: record.title,
            pictureURL: record.pictureUrl,
            isGroupChat: record.isGroup,
            lastMessage: nil,
            unreadCount: record.unreadCount,
            members: members
        )
    }
}

// MARK: - DTO

private struct ChatMemberDTO: Codable {
    let userID: Int64
    let username: String
    let firstName: String
    let lastName: String
    let profilePictureURL: String?
    let roleRaw: String

    init(member: ChatMember) {
        self.userID = member.userID
        self.username = member.username
        self.firstName = member.firstName
        self.lastName = member.lastName
        self.profilePictureURL = member.profilePictureURL
        self.roleRaw = member.role.rawValue
    }

    func toMember() -> ChatMember {
        ChatMember(
            userID: userID,
            username: username,
            firstName: firstName,
            lastName: lastName,
            profilePictureURL: profilePictureURL,
            role: ChatMemberRole(rawValue: roleRaw) ?? .member
        )
    }
}
