//
//  NameValidator.swift
//  Barkfluff
//
//  Валидатор имени и фамилии (iOS версия)
//

import Foundation

/// Результат валидации имени
struct NameValidationResult {
    let isValid: Bool
    let error: String?

    static let valid = NameValidationResult(isValid: true, error: nil)
    static func invalid(_ error: String) -> NameValidationResult {
        NameValidationResult(isValid: false, error: error)
    }
}

/// Валидатор для имени и фамилии
enum NameValidator {
    /// Минимальная длина имени
    static let minFirstNameLength = 3
    /// Максимальная длина имени
    static let maxFirstNameLength = 40
    /// Максимальная длина фамилии (может быть пустой)
    static let maxLastNameLength = 40

    /// Валидация имени
    static func validateFirstName(_ firstName: String) -> NameValidationResult {
        let trimmed = firstName.trimmingCharacters(in: .whitespaces)

        if trimmed.isEmpty {
            return .invalid("Введите имя")
        }

        if trimmed.count < minFirstNameLength {
            return .invalid("Имя должно содержать минимум \(minFirstNameLength) символа")
        }

        if trimmed.count > maxFirstNameLength {
            return .invalid("Имя не должно превышать \(maxFirstNameLength) символов")
        }

        // Проверка на допустимые символы (буквы, пробелы, дефис)
        let allowedPattern = "^[a-zA-Zа-яА-ЯёЁ\\s\\-]+$"
        let predicate = NSPredicate(format: "SELF MATCHES %@", allowedPattern)
        if !predicate.evaluate(with: trimmed) {
            return .invalid("Имя может содержать только буквы")
        }

        return .valid
    }

    /// Валидация фамилии (опционально)
    static func validateLastName(_ lastName: String) -> NameValidationResult {
        let trimmed = lastName.trimmingCharacters(in: .whitespaces)

        // Фамилия может быть пустой
        if trimmed.isEmpty {
            return .valid
        }

        if trimmed.count > maxLastNameLength {
            return .invalid("Фамилия не должна превышать \(maxLastNameLength) символов")
        }

        // Проверка на допустимые символы
        let allowedPattern = "^[a-zA-Zа-яА-ЯёЁ\\s\\-]+$"
        let predicate = NSPredicate(format: "SELF MATCHES %@", allowedPattern)
        if !predicate.evaluate(with: trimmed) {
            return .invalid("Фамилия может содержать только буквы")
        }

        return .valid
    }
}
