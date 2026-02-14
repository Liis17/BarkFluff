//
//  OTPCodeValidator.swift
//  Barkfluff
//
//  Валидатор OTP кода
//

import Foundation

/// Результат валидации OTP кода
struct OTPCodeValidationResult {
    let isValid: Bool
    let error: String?

    static let valid = OTPCodeValidationResult(isValid: true, error: nil)
    static func invalid(_ error: String) -> OTPCodeValidationResult {
        OTPCodeValidationResult(isValid: false, error: error)
    }
}

/// Валидатор для OTP кода (6 цифр)
enum OTPCodeValidator {
    /// Длина OTP кода
    static let codeLength = 6

    /// Валидация OTP кода
    static func validate(_ code: String) -> OTPCodeValidationResult {
        let trimmed = code.trimmingCharacters(in: .whitespaces)

        if trimmed.isEmpty {
            return .invalid("Введите код")
        }

        // Удаляем все нецифровые символы
        let digitsOnly = trimmed.filter { $0.isNumber }

        if digitsOnly.count != codeLength {
            return .invalid("Код должен содержать \(codeLength) цифр")
        }

        return .valid
    }

    /// Форматирование кода с разделителями (XXX XXX)
    static func format(_ code: String) -> String {
        let digits = code.filter { $0.isNumber }
        var result = ""
        for (index, char) in digits.enumerated() {
            if index == 3 {
                result += " "
            }
            result.append(char)
        }
        return String(result.prefix(7)) // "123 456"
    }

    /// Проверка что строка содержит только цифры
    static func isNumeric(_ code: String) -> Bool {
        return code.allSatisfy { $0.isNumber }
    }
}
