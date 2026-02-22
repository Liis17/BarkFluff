//
//  BFNetworkingError.swift
//  BFNetworking
//
//  Ошибки сетевого слоя
//

import Foundation

public enum BFNetworkingError: Error, LocalizedError, Sendable {
    // Аутентификация
    case unauthorized
    case sessionExpired
    case invalidCredentials
    case otpRequired
    case invalidOtpCode

    // Регистрация
    case usernameAlreadyExists
    case emailAlreadyExists
    case invalidConfirmationCode

    // Сеть
    case serverUnavailable
    case timeout
    case networkError(String)
    case invalidURL(String)

    // Файлы
    case storageLimitExceeded(String)
    case fileUploadFailed(String)
    case invalidFileHash

    // Общие
    case invalidArgument(String)
    case alreadyExists(String)
    case notFound(String)
    case unknown(String)

    public var errorDescription: String? {
        switch self {
        case .unauthorized: return "Требуется авторизация"
        case .sessionExpired: return "Сессия истекла"
        case .invalidCredentials: return "Неверный логин или пароль"
        case .otpRequired: return "Требуется код двухфакторной аутентификации"
        case .invalidOtpCode: return "Неверный код подтверждения"
        case .usernameAlreadyExists: return "Имя пользователя уже занято"
        case .emailAlreadyExists: return "Email уже используется"
        case .invalidConfirmationCode: return "Неверный код подтверждения"
        case .serverUnavailable: return "Сервер недоступен"
        case .timeout: return "Превышено время ожидания"
        case .networkError(let msg): return "Сетевая ошибка: \(msg)"
        case .invalidURL(let msg): return "Неверный URL: \(msg)"
        case .storageLimitExceeded(let msg): return "Превышен лимит хранилища: \(msg)"
        case .fileUploadFailed(let msg): return "Ошибка загрузки файла: \(msg)"
        case .invalidFileHash: return "Неверный хеш файла"
        case .invalidArgument(let msg): return "Неверные аргументы: \(msg)"
        case .alreadyExists(let msg): return "Ресурс уже существует: \(msg)"
        case .notFound(let msg): return "Не найдено: \(msg)"
        case .unknown(let msg): return "Ошибка: \(msg)"
        }
    }
}
