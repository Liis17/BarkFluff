//
//  BFError.swift
//  BFCore
//
//  Унифицированные ошибки приложения
//

import Foundation

/// Унифицированный enum ошибок BarkFluff
public enum BFError: Error, LocalizedError, Sendable {
    // MARK: - Сетевые ошибки

    /// Ошибка соединения
    case connectionFailed(underlying: Error)

    /// Превышено время ожидания
    case timeout

    /// Сервер недоступен
    case serverUnavailable

    /// Ошибка сети
    case networkError(String)

    // MARK: - Ошибки аутентификации

    /// Не авторизован
    case unauthorized

    /// Сессия истекла
    case sessionExpired

    /// Неверные учетные данные
    case invalidCredentials

    /// Аккаунт не подтверждён
    case accountNotConfirmed

    /// Требуется OTP код
    case otpRequired

    /// Неверный OTP код
    case invalidOTP

    // MARK: - Ошибки бизнес-логики

    /// Чат не найден
    case chatNotFound(chatID: String)

    /// Пользователь не найден
    case userNotFound(userID: Int64)

    /// Файл не поддерживается
    case fileNotSupported(fileName: String)

    /// Файл слишком большой
    case fileTooLarge(fileName: String, maxSize: Int64)

    /// Нет доступа к чату
    case noAccessToChat(chatID: String)

    /// Имя пользователя уже существует
    case usernameAlreadyExists(String)

    /// Email уже существует
    case emailAlreadyExists(String)

    /// Неверный код подтверждения
    case invalidConfirmationCode

    /// Операция отменена
    case cancelled

    // MARK: - Общие ошибки

    /// Неизвестная ошибка
    case unknown(message: String)

    /// Ошибка с underlying error
    case underlying(Error, String?)

    // MARK: - LocalizedError

    public var errorDescription: String? {
        switch self {
        // Сетевые
        case .connectionFailed(let error):
            return "Ошибка соединения: \(error.localizedDescription)"
        case .timeout:
            return "Превышено время ожидания"
        case .serverUnavailable:
            return "Сервер недоступен"
        case .networkError(let message):
            return "Ошибка сети: \(message)"

        // Аутентификация
        case .unauthorized:
            return "Требуется авторизация"
        case .sessionExpired:
            return "Сессия истекла. Пожалуйста, войдите снова"
        case .invalidCredentials:
            return "Неверное имя пользователя или пароль"
        case .accountNotConfirmed:
            return "Аккаунт не подтверждён. Проверьте почту"
        case .otpRequired:
            return "Требуется код двухфакторной аутентификации"
        case .invalidOTP:
            return "Неверный код двухфакторной аутентификации"

        // Бизнес-логика
        case .chatNotFound(let chatID):
            return "Чат не найден: \(chatID)"
        case .userNotFound(let userID):
            return "Пользователь не найден: \(userID)"
        case .fileNotSupported(let fileName):
            return "Файл не поддерживается: \(fileName)"
        case .fileTooLarge(let fileName, let maxSize):
            let maxSizeFormatted = ByteCountFormatter.string(fromByteCount: maxSize, countStyle: .file)
            return "Файл слишком большой: \(fileName). Максимальный размер: \(maxSizeFormatted)"
        case .noAccessToChat(let chatID):
            return "Нет доступа к чату: \(chatID)"
        case .usernameAlreadyExists(let username):
            return "Имя пользователя уже занято: \(username)"
        case .emailAlreadyExists(let email):
            return "Email уже используется: \(email)"
        case .invalidConfirmationCode:
            return "Неверный код подтверждения"
        case .cancelled:
            return "Операция отменена"

        // Общие
        case .unknown(let message):
            return "Ошибка: \(message)"
        case .underlying(let error, let message):
            return message ?? error.localizedDescription
        }
    }

    public var recoverySuggestion: String? {
        switch self {
        case .connectionFailed, .serverUnavailable, .networkError:
            return "Проверьте подключение к интернету и попробуйте снова"
        case .timeout:
            return "Попробуйте позже"
        case .unauthorized, .sessionExpired:
            return "Войдите в аккаунт снова"
        case .invalidCredentials:
            return "Проверьте имя пользователя и пароль"
        case .otpRequired:
            return "Введите код из приложения-аутентификатора"
        case .usernameAlreadyExists:
            return "Выберите другое имя пользователя"
        case .emailAlreadyExists:
            return "Используйте другой email"
        default:
            return nil
        }
    }

    // MARK: - Helpers

    /// Является ли ошибка связанной с авторизацией
    public var isAuthError: Bool {
        switch self {
        case .unauthorized, .sessionExpired, .invalidCredentials, .accountNotConfirmed, .otpRequired, .invalidOTP:
            return true
        default:
            return false
        }
    }

    /// Требуется ли повторный вход
    public var requiresReauth: Bool {
        switch self {
        case .unauthorized, .sessionExpired:
            return true
        default:
            return false
        }
    }
}

// MARK: - Equatable (для underlying ошибок используем localizedDescription)

extension BFError: Equatable {
    public static func == (lhs: BFError, rhs: BFError) -> Bool {
        switch (lhs, rhs) {
        case (.connectionFailed, .connectionFailed):
            return lhs.errorDescription == rhs.errorDescription
        case (.timeout, .timeout):
            return true
        case (.serverUnavailable, .serverUnavailable):
            return true
        case (.networkError(let l), .networkError(let r)):
            return l == r
        case (.unauthorized, .unauthorized):
            return true
        case (.sessionExpired, .sessionExpired):
            return true
        case (.invalidCredentials, .invalidCredentials):
            return true
        case (.accountNotConfirmed, .accountNotConfirmed):
            return true
        case (.otpRequired, .otpRequired):
            return true
        case (.invalidOTP, .invalidOTP):
            return true
        case (.chatNotFound(let l), .chatNotFound(let r)):
            return l == r
        case (.userNotFound(let l), .userNotFound(let r)):
            return l == r
        case (.fileNotSupported(let l), .fileNotSupported(let r)):
            return l == r
        case (.fileTooLarge(let l1, let l2), .fileTooLarge(let r1, let r2)):
            return l1 == r1 && l2 == r2
        case (.noAccessToChat(let l), .noAccessToChat(let r)):
            return l == r
        case (.usernameAlreadyExists(let l), .usernameAlreadyExists(let r)):
            return l == r
        case (.emailAlreadyExists(let l), .emailAlreadyExists(let r)):
            return l == r
        case (.invalidConfirmationCode, .invalidConfirmationCode):
            return true
        case (.cancelled, .cancelled):
            return true
        case (.unknown(let l), .unknown(let r)):
            return l == r
        case (.underlying, .underlying):
            return lhs.errorDescription == rhs.errorDescription
        default:
            return false
        }
    }
}
