//
//  LocalChatFolderRepository.swift
//  BFCore
//
//  Кеш папок чатов в SQLite (GRDB).
//

import Foundation
import GRDB

public actor LocalChatFolderRepository {

    private let database: Database
    nonisolated private let encoder: JSONEncoder
    nonisolated private let decoder: JSONDecoder

    public init(database: Database) {
        self.database = database
        self.encoder = JSONEncoder()
        self.decoder = JSONDecoder()
    }

    /// Загрузить кешированные папки, отсортированные по `sortOrder ASC`.
    public func loadCachedFolders() async throws -> [ChatFolder] {
        let records = try await database.dbPool.read { db in
            try CachedChatFolderRecord
                .order(CachedChatFolderRecord.Columns.sortOrder.asc)
                .fetchAll(db)
        }
        return records.compactMap { try? makeFolder(from: $0) }
    }

    /// Полностью заменить кеш на свежий снапшот с сервера.
    public func replaceAll(_ folders: [ChatFolder]) async throws {
        try await database.dbPool.write { db in
            try CachedChatFolderRecord.deleteAll(db)
            let now = Int64(Date().timeIntervalSince1970)
            for folder in folders {
                try makeRecord(from: folder, updatedAt: now).insert(db)
            }
        }
    }

    /// Вставить/обновить одну папку.
    public func upsert(_ folder: ChatFolder) async throws {
        try await database.dbPool.write { db in
            let now = Int64(Date().timeIntervalSince1970)
            try makeRecord(from: folder, updatedAt: now).upsert(db)
        }
    }

    /// Удалить папку из кеша.
    public func delete(id: String) async throws {
        _ = try await database.dbPool.write { db in
            try CachedChatFolderRecord.deleteOne(db, key: id)
        }
    }

    public func clear() async throws {
        try await database.dbPool.write { db in
            try CachedChatFolderRecord.deleteAll(db)
        }
    }

    // MARK: - Mapping

    nonisolated private func makeRecord(from folder: ChatFolder, updatedAt: Int64) throws -> CachedChatFolderRecord {
        let chatIDsData = try encoder.encode(folder.chatIDs)
        return CachedChatFolderRecord(
            id: folder.id,
            name: folder.name,
            icon: folder.icon,
            chatIdsJson: chatIDsData,
            sortOrder: folder.sortOrder,
            updatedAt: updatedAt
        )
    }

    nonisolated private func makeFolder(from record: CachedChatFolderRecord) throws -> ChatFolder {
        let chatIDs = (try? decoder.decode([String].self, from: record.chatIdsJson)) ?? []
        return ChatFolder(
            id: record.id,
            name: record.name,
            icon: record.icon,
            chatIDs: chatIDs,
            sortOrder: record.sortOrder
        )
    }
}
