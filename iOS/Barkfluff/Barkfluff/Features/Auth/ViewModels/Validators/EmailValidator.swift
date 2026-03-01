//
//  EmailValidator.swift
//  Barkfluff
//
//  Валидатор email (iOS версия)
//

import Foundation

/// Результат валидации email
struct EmailValidationResult {
    let isValid: Bool
    let error: String?

    static let valid = EmailValidationResult(isValid: true, error: nil)
    static func invalid(_ error: String) -> EmailValidationResult {
        EmailValidationResult(isValid: false, error: error)
    }
}

/// Валидатор для email адреса
enum EmailValidator {
    /// Валидация формата email
    static func validate(_ email: String) -> EmailValidationResult {
        let trimmed = email.trimmingCharacters(in: .whitespaces)

        if trimmed.isEmpty {
            return .invalid("Введите email")
        }

        // Базовая проверка формата
        let emailPattern = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,64}"
        let predicate = NSPredicate(format: "SELF MATCHES %@", emailPattern)
        if !predicate.evaluate(with: trimmed) {
            return .invalid("Некорректный формат email")
        }

        // Дополнительные проверки
        let parts = trimmed.split(separator: "@")
        if parts.count != 2 {
            return .invalid("Некорректный формат email")
        }

        let localPart = String(parts[0])
        let domainPart = String(parts[1])

        // Локальная часть не должна быть пустой
        if localPart.isEmpty {
            return .invalid("Email не может начинаться с @")
        }

        // Домен должен содержать точку
        if !domainPart.contains(".") {
            return .invalid("Некорректный домен")
        }

        // Домен не должен заканчиваться точкой
        if domainPart.hasSuffix(".") {
            return .invalid("Некорректный домен")
        }

        return .valid
    }
}
