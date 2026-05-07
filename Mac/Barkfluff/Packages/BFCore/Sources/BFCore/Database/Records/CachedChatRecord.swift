//
//  CachedChatRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_chat` — кеш списка чатов.
struct CachedChatRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_chat"

    var id: String
    var title: String
    var pictureUrl: String?
    var pictureFileId: String?
    var isGroup: Bool
    var lastMessageId: Int64?
    var unreadCount: Int64
    var membersJson: Data?
    var updatedAt: Int64

    enum Columns {
        static let id = Column(CodingKeys.id)
        static let updatedAt = Column(CodingKeys.updatedAt)
    }
}
