//
//  ChatFolder.swift
//  BFCore
//
//  Доменная модель папки чатов
//

import Foundation

/// Папка чатов пользователя
public struct ChatFolder: Identifiable, Hashable, Sendable, Codable {
    public let id: String
    public var name: String
    public var icon: String
    public var chatIDs: [String]
    public var sortOrder: Int32

    public init(
        id: String,
        name: String,
        icon: String = "",
        chatIDs: [String] = [],
        sortOrder: Int32 = 0
    ) {
        self.id = id
        self.name = name
        self.icon = icon
        self.chatIDs = chatIDs
        self.sortOrder = sortOrder
    }
}
