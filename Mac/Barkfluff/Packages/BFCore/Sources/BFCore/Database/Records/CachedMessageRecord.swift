//
//  CachedMessageRecord.swift
//  BFCore
//

import Foundation
import GRDB

/// Запись таблицы `cached_message` — кеш истории сообщений по чатам.
struct CachedMessageRecord: Codable, FetchableRecord, PersistableRecord, Hashable {
    static let databaseTableName = "cached_message"

    var id: Int64
    var chatId: String
    var senderId: Int64
    var senderName: String?
    var text: String
    var attachmentsJson: Data?
    var sentAt: Int64
    var readByJson: Data?
    var isSystem: Bool
    var isEdited: Bool
    var editedAt: Int64?

    enum Columns {
        static let id = Column(CodingKeys.id)
        static let chatId = Column(CodingKeys.chatId)
        static let sentAt = Column(CodingKeys.sentAt)
    }
}
