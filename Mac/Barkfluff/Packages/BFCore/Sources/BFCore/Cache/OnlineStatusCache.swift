//
//  OnlineStatusCache.swift
//  BFCore
//
//  Потокобезопасный кеш онлайн-статусов
//

import Foundation

/// Потокобезопасный кеш онлайн-статусов.
/// Хранит последний известный статус каждого пользователя.
public actor OnlineStatusCache {

    private var statuses: [Int64: OnlineStatus] = [:]

    public init() {}

    /// Получить статус пользователя
    public func getStatus(for userID: Int64) -> OnlineStatus {
        statuses[userID] ?? .unknown
    }

    /// Обновить статус пользователя
    public func updateStatus(userID: Int64, status: OnlineStatus) {
        statuses[userID] = status
    }

    /// Обновить статусы пакетом
    public func updateStatuses(_ events: [OnlineStatusEvent]) {
        for event in events {
            statuses[event.userID] = event.status
        }
    }

    /// Получить все закешированные статусы
    public func getAllStatuses() -> [Int64: OnlineStatus] {
        statuses
    }

    /// Очистить кеш
    public func removeAll() {
        statuses.removeAll()
    }
}
