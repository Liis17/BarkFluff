//
//  Events.swift
//  BFCore
//
//  События real-time обновлений
//

import Foundation

/// Событие нового сообщения
public struct NewMessageEvent: Hashable, Sendable {
    public let chatID: String
    public let message: Message

    public init(chatID: String, message: Message) {
        self.chatID = chatID
        self.message = message
    }
}

/// Событие прочтения сообщения
public struct MessageReadEvent: Hashable, Sendable {
    public let chatID: String
    public let messageID: Int64
    public let readBy: [Int64]

    public init(chatID: String, messageID: Int64, readBy: [Int64]) {
        self.chatID = chatID
        self.messageID = messageID
        self.readBy = readBy
    }
}

/// Событие подключения real-time стрима
public enum UpdatesConnectionEvent: Sendable {
    case connectionLost
    case reconnected
}
