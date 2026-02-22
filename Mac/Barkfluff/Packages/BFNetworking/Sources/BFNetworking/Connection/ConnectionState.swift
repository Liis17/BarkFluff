//
//  ConnectionState.swift
//  BFNetworking
//
//  Состояние соединения
//

import Foundation

/// Состояние соединения с сервером
public enum ConnectionState: Sendable, Equatable {
    /// Отключено
    case disconnected

    /// Подключение...
    case connecting

    /// Подключено
    case connected

    /// Ошибка соединения
    case error(String)

    /// Отключение...
    case disconnecting

    // MARK: - Helpers

    /// Можно ли использовать соединение
    public var isUsable: Bool {
        switch self {
        case .connected:
            return true
        default:
            return false
        }
    }

    /// Отображаемое название
    public var displayName: String {
        switch self {
        case .disconnected:
            return "Отключено"
        case .connecting:
            return "Подключение..."
        case .connected:
            return "Подключено"
        case .error(let message):
            return "Ошибка: \(message)"
        case .disconnecting:
            return "Отключение..."
        }
    }

    /// Название иконки SF Symbol
    public var systemImage: String {
        switch self {
        case .disconnected:
            return "wifi.slash"
        case .connecting:
            return "wifi"
        case .connected:
            return "wifi"
        case .error:
            return "wifi.exclamationmark"
        case .disconnecting:
            return "wifi"
        }
    }
}
