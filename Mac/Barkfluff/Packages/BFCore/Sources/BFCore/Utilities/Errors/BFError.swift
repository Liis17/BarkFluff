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

    // MARK: - LocalizedError (системная локаль)

    public var errorDescription: String? { errorDescription(in: .current) }
    public var recoverySuggestion: String? { recoverySuggestion(in: .current) }

    // MARK: - Locale-aware версии

    /// Описание ошибки с явной локалью (для реактивного UI).
    public func errorDescription(in locale: Locale) -> String? {
        switch self {
        // Сетевые
        case .connectionFailed(let error):
            return String(localized: "bfcore.error.connection_failed \(error.localizedDescription)", bundle: .module, locale: locale)
        case .timeout:
            return String(localized: "bfcore.error.timeout", bundle: .module, locale: locale)
        case .serverUnavailable:
            return String(localized: "bfcore.error.server_unavailable", bundle: .module, locale: locale)
        case .networkError(let message):
            return String(localized: "bfcore.error.network \(message)", bundle: .module, locale: locale)

        // Аутентификация
        case .unauthorized:
            return String(localized: "bfcore.error.unauthorized", bundle: .module, locale: locale)
        case .sessionExpired:
            return String(localized: "bfcore.error.session_expired", bundle: .module, locale: locale)
        case .invalidCredentials:
            return String(localized: "bfcore.error.invalid_credentials", bundle: .module, locale: locale)
        case .accountNotConfirmed:
            return String(localized: "bfcore.error.account_not_confirmed", bundle: .module, locale: locale)
        case .otpRequired:
            return String(localized: "bfcore.error.otp_required", bundle: .module, locale: locale)
        case .invalidOTP:
            return String(localized: "bfcore.error.invalid_otp", bundle: .module, locale: locale)

        // Бизнес-логика
        case .chatNotFound(let chatID):
            return String(localized: "bfcore.error.chat_not_found \(chatID)", bundle: .module, locale: locale)
        case .userNotFound(let userID):
            return String(localized: "bfcore.error.user_not_found \(userID)", bundle: .module, locale: locale)
        case .fileNotSupported(let fileName):
            return String(localized: "bfcore.error.file_not_supported \(fileName)", bundle: .module, locale: locale)
        case .fileTooLarge(let fileName, let maxSize):
            let maxSizeFormatted = ByteCountFormatter.string(fromByteCount: maxSize, countStyle: .file)
            return String(localized: "bfcore.error.file_too_large \(fileName) \(maxSizeFormatted)", bundle: .module, locale: locale)
        case .noAccessToChat(let chatID):
            return String(localized: "bfcore.error.no_access_to_chat \(chatID)", bundle: .module, locale: locale)
        case .usernameAlreadyExists(let username):
            return String(localized: "bfcore.error.username_exists \(username)", bundle: .module, locale: locale)
        case .emailAlreadyExists(let email):
            return String(localized: "bfcore.error.email_exists \(email)", bundle: .module, locale: locale)
        case .invalidConfirmationCode:
            return String(localized: "bfcore.error.invalid_confirmation_code", bundle: .module, locale: locale)
        case .cancelled:
            return String(localized: "bfcore.error.cancelled", bundle: .module, locale: locale)

        // Общие
        case .unknown(let message):
            return String(localized: "bfcore.error.unknown_with_message \(message)", bundle: .module, locale: locale)
        case .underlying(let error, let message):
            return message ?? error.localizedDescription
        }
    }

    /// Подсказка по исправлению с явной локалью.
    public func recoverySuggestion(in locale: Locale) -> String? {
        switch self {
        case .connectionFailed, .serverUnavailable, .networkError:
            return String(localized: "bfcore.recovery.check_internet", bundle: .module, locale: locale)
        case .timeout:
            return String(localized: "bfcore.recovery.try_later", bundle: .module, locale: locale)
        case .unauthorized, .sessionExpired:
            return String(localized: "bfcore.recovery.sign_in_again", bundle: .module, locale: locale)
        case .invalidCredentials:
            return String(localized: "bfcore.recovery.check_credentials", bundle: .module, locale: locale)
        case .otpRequired:
            return String(localized: "bfcore.recovery.enter_otp", bundle: .module, locale: locale)
        case .usernameAlreadyExists:
            return String(localized: "bfcore.recovery.choose_different_username", bundle: .module, locale: locale)
        case .emailAlreadyExists:
            return String(localized: "bfcore.recovery.use_different_email", bundle: .module, locale: locale)
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
