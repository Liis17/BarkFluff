//
//  CachedCurrentUserRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_current_user` — persistent-кеш текущего пользователя.
/// Single-row семантика: всегда максимум одна строка (INSERT OR REPLACE по id).
struct CachedCurrentUserRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_current_user"

    var id: Int64
    var firstName: String
    var lastName: String
    var username: String
    var email: String?
    var bio: String?
    var registrationDate: Int64
    var profilePictureUrl: String?
    var profilePicturePreviewUrl: String?
    var profilePosterFileId: String?
    var badgesJson: Data?
    var storageLimitBytes: Int64
    var updatedAt: Int64

    enum Columns {
        static let id = Column(CodingKeys.id)
        static let updatedAt = Column(CodingKeys.updatedAt)
    }
}
