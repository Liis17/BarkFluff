//
//  FastAuthToken.swift
//  BFCore
//
//  Модели быстрой авторизации
//

import Foundation

/// Тип быстрой авторизации
public enum FastAuthType: String, Sendable, Codable, CaseIterable {
    case qr
    case code
}

/// Токен для быстрой авторизации
public struct FastAuthToken: Identifiable, Hashable, Sendable {
    public let id: String
    public let token: String
    public let expiresAt: Date

    public init(id: String, token: String, expiresAt: Date) {
        self.id = id
        self.token = token
        self.expiresAt = expiresAt
    }

    /// Проверка, истёк ли токен
    public var isExpired: Bool {
        Date() >= expiresAt
    }

    /// Оставшееся время в секундах
    public var remainingSeconds: Int64 {
        max(0, Int64(expiresAt.timeIntervalSince(Date())))
    }
}

/// Статус быстрой авторизации
public enum FastAuthStatus: String, Sendable, Codable {
    case pending
    case accepted
    case rejected
    case expired

    public var displayName: String {
        switch self {
        case .pending: return "Ожидание"
        case .accepted: return "Принято"
        case .rejected: return "Отклонено"
        case .expired: return "Истекло"
        }
    }
}

/// Результат быстрой авторизации
public struct FastAuthResult: Hashable, Sendable {
    public let fastAuthID: String
    public let status: FastAuthStatus
    public let accessToken: String?
    public let refreshToken: String?

    public init(fastAuthID: String, status: FastAuthStatus, accessToken: String? = nil, refreshToken: String? = nil) {
        self.fastAuthID = fastAuthID
        self.status = status
        self.accessToken = accessToken
        self.refreshToken = refreshToken
    }
}

/// Запрос на быструю авторизацию (для подтверждения на устройстве)
public struct FastAuthRequest: Identifiable, Hashable, Sendable {
    public let id: String
    public let deviceName: String
    public let deviceType: String
    public let requestedAt: Date

    public init(id: String, deviceName: String, deviceType: String, requestedAt: Date) {
        self.id = id
        self.deviceName = deviceName
        self.deviceType = deviceType
        self.requestedAt = requestedAt
    }
}

/// Статус привязки устройства
public struct ConnectDeviceStatus: Hashable, Sendable {
    public let deviceID: String
    public let status: String

    public init(deviceID: String, status: String) {
        self.deviceID = deviceID
        self.status = status
    }
}

/// Привязанное устройство
public struct ConnectedDevice: Identifiable, Hashable, Sendable {
    public let id: String
    public let deviceName: String
    public let deviceType: String
    public let connectedAt: Date

    public init(id: String, deviceName: String, deviceType: String, connectedAt: Date) {
        self.id = id
        self.deviceName = deviceName
        self.deviceType = deviceType
        self.connectedAt = connectedAt
    }
}
