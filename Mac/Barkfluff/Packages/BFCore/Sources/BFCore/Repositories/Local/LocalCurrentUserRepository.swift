//
//  LocalCurrentUserRepository.swift
//  BFCore
//
//  Persistent-кеш текущего пользователя в SQLite (single-row).
//

import Foundation
import GRDB

public actor LocalCurrentUserRepository {

    private let database: Database
    nonisolated private let encoder: JSONEncoder
    nonisolated private let decoder: JSONDecoder

    public init(database: Database) {
        self.database = database
        self.encoder = JSONEncoder()
        self.decoder = JSONDecoder()
    }

    /// Загрузить кешированного текущего пользователя (или nil, если ничего не сохранено).
    public func loadCachedCurrentUser() async throws -> User? {
        let record = try await database.dbPool.read { db in
            try CachedCurrentUserRecord.fetchOne(db)
        }
        guard let record else { return nil }
        return makeUser(from: record)
    }

    /// Записать пользователя (INSERT OR REPLACE — single-row семантика).
    public func save(_ user: User) async throws {
        let record = try makeRecord(from: user)
        try await database.dbPool.write { db in
            try CachedCurrentUserRecord.deleteAll(db)
            try record.insert(db)
        }
    }

    /// Очистить кеш (вызывается при logout).
    public func clear() async throws {
        try await database.dbPool.write { db in
            try CachedCurrentUserRecord.deleteAll(db)
        }
    }

    // MARK: - Mapping

    nonisolated private func makeRecord(from user: User) throws -> CachedCurrentUserRecord {
        let badgesDTO = user.badges.map(UserBadgeDTO.init(badge:))
        let badgesJson = try encoder.encode(badgesDTO)
        return CachedCurrentUserRecord(
            id: user.id,
            firstName: user.firstName,
            lastName: user.lastName,
            username: user.username,
            email: user.email,
            bio: user.bio,
            registrationDate: Int64(user.registrationDate.timeIntervalSince1970),
            profilePictureUrl: user.profilePictureURL,
            profilePicturePreviewUrl: user.profilePicturePreviewURL,
            profilePosterFileId: user.profilePosterFileID,
            badgesJson: badgesJson,
            storageLimitBytes: user.storageLimitBytes,
            updatedAt: Int64(Date().timeIntervalSince1970)
        )
    }

    nonisolated private func makeUser(from record: CachedCurrentUserRecord) -> User {
        let badges: [UserBadge]
        if let data = record.badgesJson,
           let dtos = try? decoder.decode([UserBadgeDTO].self, from: data) {
            badges = dtos.map { $0.toBadge() }
        } else {
            badges = []
        }
        return User(
            id: record.id,
            firstName: record.firstName,
            lastName: record.lastName,
            username: record.username,
            email: record.email,
            bio: record.bio,
            registrationDate: Date(timeIntervalSince1970: TimeInterval(record.registrationDate)),
            profilePictureURL: record.profilePictureUrl,
            profilePicturePreviewURL: record.profilePicturePreviewUrl,
            profilePosterFileID: record.profilePosterFileId,
            badges: badges,
            storageLimitBytes: record.storageLimitBytes
        )
    }
}

// MARK: - DTO

private struct UserBadgeDTO: Codable {
    let id: String
    let name: String
    let description: String
    let iconURL: String?

    init(badge: UserBadge) {
        self.id = badge.id
        self.name = badge.name
        self.description = badge.description
        self.iconURL = badge.iconURL
    }

    func toBadge() -> UserBadge {
        UserBadge(id: id, name: name, description: description, iconURL: iconURL)
    }
}
