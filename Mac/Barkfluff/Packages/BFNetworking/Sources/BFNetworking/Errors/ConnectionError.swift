//
//  ConnectionError.swift
//  BFNetworking
//
//  Ошибки соединения
//

import Foundation

public enum ConnectionError: Error, LocalizedError {
    case serviceNotConfigured(ServiceKind)
    case connectionFailed(String)
    case notBootstrapped

    public var errorDescription: String? {
        switch self {
        case .serviceNotConfigured(let kind):
            return "Сервис \(kind.rawValue) не настроен"
        case .connectionFailed(let reason):
            return "Ошибка соединения: \(reason)"
        case .notBootstrapped:
            return "Соединение не инициализировано"
        }
    }
}
