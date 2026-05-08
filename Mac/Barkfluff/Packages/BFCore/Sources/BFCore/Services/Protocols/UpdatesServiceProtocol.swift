//
//  UpdatesServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса обновлений
//

import Foundation

/// Протокол сервиса real-time обновлений
public protocol UpdatesServiceProtocol: Sendable {
    /// Запустить прослушивание обновлений
    func start() async

    /// Остановить прослушивание обновлений
    func stop() async

    /// Поток новых сообщений
    func getNewMessagesStream() async -> AsyncStream<NewMessageEvent>

    /// Поток событий прочтения
    func getReadEventsStream() async -> AsyncStream<MessageReadEvent>

    /// Поток событий редактирования сообщений
    func getEditedMessagesStream() async -> AsyncStream<MessageEditedEvent>

    /// Поток событий удаления сообщений
    func getDeletedMessagesStream() async -> AsyncStream<MessageDeletedEvent>

    /// Поток событий подключения (connectionLost / reconnected)
    func getConnectionEventsStream() async -> AsyncStream<UpdatesConnectionEvent>

    /// Активен ли стрим
    func isActive() async -> Bool
}
