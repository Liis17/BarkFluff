//
//  UsernameValidator.swift
//  Barkfluff
//
//  Валидатор имени пользователя
//

import Foundation

/// Результат валидации username
struct UsernameValidationResult {
    let isValid: Bool
    let error: String?
    let isAvailable: Bool? // nil = не проверено, true = доступен, false = занят

    static let valid = UsernameValidationResult(isValid: true, error: nil, isAvailable: nil)
    static func invalid(_ error: String) -> UsernameValidationResult {
        UsernameValidationResult(isValid: false, error: error, isAvailable: nil)
    }
    static let unavailable = UsernameValidationResult(isValid: false, error: "Это имя уже занято", isAvailable: false)
    static let available = UsernameValidationResult(isValid: true, error: nil, isAvailable: true)
}

/// Валидатор для имени пользователя (username)
enum UsernameValidator {
    /// Минимальная длина
    static let minLength = 3
    /// Максимальная длина
    static let maxLength = 30

    /// Валидация формата username (без проверки на сервере)
    static func validate(_ username: String) -> UsernameValidationResult {
        let trimmed = username.trimmingCharacters(in: .whitespaces)

        if trimmed.isEmpty {
            return .invalid("Введите имя пользователя")
        }

        if trimmed.count < minLength {
            return .invalid("Минимум \(minLength) символа")
        }

        if trimmed.count > maxLength {
            return .invalid("Максимум \(maxLength) символов")
        }

        // Проверка на допустимые символы: a-zA-Z0-9_-
        let allowedPattern = "^[a-zA-Z0-9_\\-]+$"
        let predicate = NSPredicate(format: "SELF MATCHES %@", allowedPattern)
        if !predicate.evaluate(with: trimmed) {
            return .invalid("Только буквы, цифры, _ и -")
        }

        // Проверка что не начинается с цифры
        if trimmed.first?.isNumber == true {
            return .invalid("Не должен начинаться с цифры")
        }

        return .valid
    }

    /// Проверка username целиком (формат + доступность)
    static func validateWithAvailability(
        _ username: String,
        checkAvailable: @escaping (String) async throws -> Bool
    ) async -> UsernameValidationResult {
        // Сначала проверяем формат
        let formatResult = validate(username)
        if !formatResult.isValid {
            return formatResult
        }

        // Затем проверяем доступность на сервере
        do {
            let exists = try await checkAvailable(username)
            if exists {
                return .unavailable
            } else {
                return .available
            }
        } catch {
            // Если сервер недоступен, возвращаем только валидацию формата
            return formatResult
        }
    }
}
