//
//  CachedStickerRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_sticker` — кеш стикеров внутри стикерпаков.
struct CachedStickerRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_sticker"

    var id: String
    var packId: String
    var fileId: String
    var previewFileId: String
    var emoji: String
    var addedAt: Int64
    var fileUrl: String?
    var previewUrl: String?

    enum Columns {
        static let id = Column(CodingKeys.id)
        static let packId = Column(CodingKeys.packId)
        static let addedAt = Column(CodingKeys.addedAt)
    }
}
