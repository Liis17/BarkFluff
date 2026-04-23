//
//  OnlineStatusEvent.swift
//  BFCore
//
//  Событие изменения онлайн-статуса
//

import Foundation

/// Событие изменения онлайн-статуса (из gRPC stream)
public struct OnlineStatusEvent: Sendable, Hashable {
    public let userID: Int64
    public let status: OnlineStatus

    public init(userID: Int64, status: OnlineStatus) {
        self.userID = userID
        self.status = status
    }
}

/// Событие подключения real-time стрима онлайн-статусов
public enum OnlineStatusConnectionEvent: Sendable {
    case connectionLost
    case reconnected
}
