//
//  CachedFileRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_file` — индекс файлов, лежащих на диске.
struct CachedFileRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_file"

    var fileId: String
    var type: String
    var sizeBytes: Int64
    var mimeType: String?
    var originalName: String?
    var addedAt: Int64

    enum Columns {
        static let fileId = Column(CodingKeys.fileId)
        static let type = Column(CodingKeys.type)
        static let sizeBytes = Column(CodingKeys.sizeBytes)
        static let addedAt = Column(CodingKeys.addedAt)
    }
}
