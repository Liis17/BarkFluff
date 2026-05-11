//
//  CachedChatFolderRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_chat_folder` — кеш папок чатов.
struct CachedChatFolderRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_chat_folder"

    var id: String
    var name: String
    var icon: String
    var chatIdsJson: Data
    var sortOrder: Int32
    var updatedAt: Int64

    enum Columns {
        static let id = Column(CodingKeys.id)
        static let sortOrder = Column(CodingKeys.sortOrder)
        static let updatedAt = Column(CodingKeys.updatedAt)
    }
}
