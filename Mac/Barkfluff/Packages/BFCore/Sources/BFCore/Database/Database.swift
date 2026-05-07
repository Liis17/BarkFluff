//
//  Database.swift
//  BFCore
//
//  Локальная SQLite-база (GRDB) для кеша файлов, чатов и сообщений.
//

import Foundation
import GRDB

/// Обёртка над `DatabasePool` с миграциями.
///
/// Файл хранится в `~/Library/Application Support/BarkFluff/cache.sqlite`,
/// чтобы система не очистила базу при нехватке диска.
public final class Database: @unchecked Sendable {

    public let dbPool: DatabasePool

    public init(fileURL: URL? = nil) throws {
        let url = try fileURL ?? Self.defaultFileURL()
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )

        var config = Configuration()
        config.prepareDatabase { db in
            try db.execute(sql: "PRAGMA foreign_keys = ON")
        }

        self.dbPool = try DatabasePool(path: url.path, configuration: config)
        try Migrations.migrator.migrate(dbPool)
    }

    /// Полностью очистить все таблицы кеша.
    public func truncateAll() async throws {
        try await dbPool.write { db in
            try db.execute(sql: "DELETE FROM cached_message")
            try db.execute(sql: "DELETE FROM cached_chat")
            try db.execute(sql: "DELETE FROM cached_file")
        }
    }

    // MARK: - Helpers

    private static func defaultFileURL() throws -> URL {
        let support = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        return support
            .appendingPathComponent("BarkFluff", isDirectory: true)
            .appendingPathComponent("cache.sqlite", isDirectory: false)
    }
}
