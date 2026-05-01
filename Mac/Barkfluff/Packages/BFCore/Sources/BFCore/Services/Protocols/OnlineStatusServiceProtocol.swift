//
//  OnlineStatusServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса онлайн-статусов
//

import Foundation

/// Протокол сервиса онлайн-статусов.
/// Single source of truth для онлайн-статусов всех отслеживаемых пользователей.
///
/// Контракт работы консумера (View / ViewModel):
/// 1. `track(userID)` — гарантирует подписку на gRPC-стрим для этого пользователя и
///    свежий fetch текущего статуса. Каждый track ОБЯЗАТЕЛЬНО парный untrack.
/// 2. `currentStatus(for:)` — мгновенный snapshot из кеша.
/// 3. `statusStream(for:)` — поток только diff-изменений конкретного пользователя.
public protocol OnlineStatusServiceProtocol: Sendable {

    /// Запустить сервис: heartbeat + подписку на статусы. Делает warmup-fetch для
    /// initialUserIDs (помещает их статусы в кеш), но не активирует tracking refcount.
    func start(initialUserIDs: [Int64]) async

    /// Остановить сервис.
    func stop() async

    /// Snapshot статуса пользователя из кеша (без сети).
    func currentStatus(for userID: Int64) async -> OnlineStatus

    /// Per-user поток изменений статуса. Только diff'ы (initial значение
    /// консумер должен прочитать через `currentStatus(for:)` отдельно).
    /// Стрим завершается при `stop()` сервиса.
    func statusStream(for userID: Int64) async -> AsyncStream<OnlineStatus>

    /// Ref-counted tracking: добавить пользователя в gRPC-подписку и сделать
    /// authoritative fetch. Каждый `track` обязателен парный `untrack`.
    func track(_ userID: Int64) async

    /// Уменьшить refcount tracking'а. Когда refcount достигает 0 —
    /// пользователь удаляется из gRPC-подписки.
    func untrack(_ userID: Int64) async

    /// Bulk-track (каждый ID +1 к refcount).
    func track(_ userIDs: [Int64]) async

    /// Bulk-untrack.
    func untrack(_ userIDs: [Int64]) async

    /// Поток событий подключения (для UI: показать/скрыть индикатор офлайна).
    func getConnectionEventsStream() async -> AsyncStream<OnlineStatusConnectionEvent>

    /// Активен ли сервис.
    func isActive() async -> Bool
}
