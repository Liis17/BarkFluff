//
//  CachedStickerPackRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_sticker_pack` — кеш метаданных стикерпаков.
struct CachedStickerPackRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_sticker_pack"

    var id: String
    var creatorUserId: Int64
    var coverStickerId: String
    var name: String
    var description: String
    var createdAt: Int64
    var stickerCount: Int
    var updatedAt: Int64

    enum Columns {
        static let id = Column(CodingKeys.id)
        static let createdAt = Column(CodingKeys.createdAt)
        static let updatedAt = Column(CodingKeys.updatedAt)
    }
}
