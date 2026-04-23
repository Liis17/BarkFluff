//
//  OnlineStatusServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса онлайн-статусов
//

import Foundation

/// Протокол сервиса онлайн-статусов
public protocol OnlineStatusServiceProtocol: Sendable {

    /// Запустить сервис (heartbeat + подписка на статусы видимых пользователей)
    func start(initialUserIDs: [Int64]) async

    /// Остановить сервис
    func stop() async

    /// Получить текущий кешированный статус пользователя
    func getStatus(for userID: Int64) async -> OnlineStatus

    /// Получить статусы для нескольких пользователей (batch, с запросом на сервер если нет в кеше)
    func fetchStatuses(for userIDs: [Int64]) async

    /// Добавить пользователей к отслеживаемым (инкрементально)
    func addToTracking(_ userIDs: [Int64]) async

    /// Удалить пользователей из отслеживаемых (инкрементально)
    func removeFromTracking(_ userIDs: [Int64]) async

    /// Полная замена списка отслеживаемых пользователей
    func replaceTrackedUsers(_ userIDs: [Int64]) async

    /// Получить текущий список отслеживаемых пользователей
    func getTrackedUserIDs() async -> Set<Int64>

    /// Поток событий изменения статусов (для UI подписки)
    func getStatusEventsStream() async -> AsyncStream<OnlineStatusEvent>

    /// Поток событий подключения
    func getConnectionEventsStream() async -> AsyncStream<OnlineStatusConnectionEvent>

    /// Активен ли сервис
    func isActive() async -> Bool
}
