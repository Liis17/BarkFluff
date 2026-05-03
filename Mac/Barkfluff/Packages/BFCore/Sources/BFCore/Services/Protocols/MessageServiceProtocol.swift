//
//  MessageServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса сообщений
//

import Foundation

/// Протокол сервиса сообщений
public protocol MessageServiceProtocol: Sendable {
    /// Получить список сообщений чата
    func listMessages(
        chatID: String,
        fromMessageID: Int64,
        offsetBefore: Int32,
        offsetAfter: Int32
    ) async throws -> [Message]

    /// Отправить сообщение
    /// - Parameter forwardedMessageID: если задан — сервер прикрепит снимок оригинала как FORWARDED_MESSAGE attachment
    func sendMessage(
        chatID: String?,
        userID: Int64?,
        text: String,
        fileIDs: [String],
        forwardedMessageID: Int64?
    ) async throws -> Message

    /// Пометить сообщения как прочитанные
    func markAsRead(messageIDs: [Int64]) async throws

    /// Подписаться на новые сообщения
    func subscribeToNewMessages() async throws -> AsyncThrowingStream<NewMessageEvent, Error>

    /// Подписаться на события прочтения
    func subscribeToReadEvents() async throws -> AsyncThrowingStream<MessageReadEvent, Error>
}
