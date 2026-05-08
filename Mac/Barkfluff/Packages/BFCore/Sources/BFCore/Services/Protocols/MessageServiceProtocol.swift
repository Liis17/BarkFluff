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

    /// Редактировать сообщение.
    /// `chatID` нужен потому, что backend не возвращает chat_id в shared.Message —
    /// сервис подменяет его в возвращаемом доменном Message.
    func editMessage(
        chatID: String,
        messageID: Int64,
        text: String,
        fileIDs: [String]
    ) async throws -> Message

    /// Удалить сообщение.
    func deleteMessage(messageID: Int64) async throws

    /// Подписаться на новые сообщения
    func subscribeToNewMessages() async throws -> AsyncThrowingStream<NewMessageEvent, Error>

    /// Подписаться на события прочтения
    func subscribeToReadEvents() async throws -> AsyncThrowingStream<MessageReadEvent, Error>

    /// Подписаться на события редактирования сообщений
    func subscribeToEditedEvents() async throws -> AsyncThrowingStream<MessageEditedEvent, Error>

    /// Подписаться на события удаления сообщений
    func subscribeToDeletedEvents() async throws -> AsyncThrowingStream<MessageDeletedEvent, Error>
}
