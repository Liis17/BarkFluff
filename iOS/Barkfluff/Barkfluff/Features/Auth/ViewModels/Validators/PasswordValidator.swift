//
//  PasswordValidator.swift
//  Barkfluff
//
//  Валидатор пароля с проверкой силы (iOS версия)
//

import Foundation

/// Уровень силы пароля
enum PasswordStrength: Int, Comparable {
    case veryWeak = 1
    case weak = 2
    case medium = 3
    case strong = 4
    case veryStrong = 5

    /// Локализуемый ярлык уровня силы пароля
    var label: LocalizedStringResource {
        switch self {
        case .veryWeak: return LocalizedStringResource("auth.validation.password.strength.very_weak")
        case .weak: return LocalizedStringResource("auth.validation.password.strength.weak")
        case .medium: return LocalizedStringResource("auth.validation.password.strength.medium")
        case .strong: return LocalizedStringResource("auth.validation.password.strength.strong")
        case .veryStrong: return LocalizedStringResource("auth.validation.password.strength.very_strong")
        }
    }

    var color: String {
        switch self {
        case .veryWeak, .weak: return "red"
        case .medium: return "yellow"
        case .strong: return "green"
        case .veryStrong: return "green"
        }
    }

    /// Минимальный приемлемый уровень (score >= 60)
    var isAcceptable: Bool {
        self >= .medium
    }

    static func < (lhs: PasswordStrength, rhs: PasswordStrength) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}

/// Результат валидации пароля
struct PasswordValidationResult {
    let isValid: Bool
    let error: LocalizedStringResource?
    let strength: PasswordStrength
    let score: Int // 0-100

    static func invalid(_ error: LocalizedStringResource, strength: PasswordStrength = .veryWeak, score: Int = 0) -> PasswordValidationResult {
        PasswordValidationResult(isValid: false, error: error, strength: strength, score: score)
    }

    static func valid(strength: PasswordStrength, score: Int) -> PasswordValidationResult {
        PasswordValidationResult(isValid: true, error: nil, strength: strength, score: score)
    }
}

/// Валидатор пароля
enum PasswordValidator {
    /// Минимальная длина пароля
    static let minLength = 8
    /// Максимальная длина пароля
    static let maxLength = 128
    /// Минимальный score для валидности (0-100)
    static let minScore = 60

    /// Вычисление силы пароля (0-100)
    static func calculateStrength(_ password: String) -> (strength: PasswordStrength, score: Int) {
        var score = 0

        // Длина
        if password.count >= 8 { score += 20 }
        if password.count >= 12 { score += 10 }
        if password.count >= 16 { score += 10 }

        // Разнообразие символов
        let hasLowercase = password.contains { $0.isLowercase }
        let hasUppercase = password.contains { $0.isUppercase }
        let hasDigits = password.contains { $0.isNumber }
        let hasSpecial = password.contains { "!@#$%^&*()_+-=[]{}|;':\",./<>?`~".contains($0) }

        if hasLowercase { score += 10 }
        if hasUppercase { score += 15 }
        if hasDigits { score += 15 }
        if hasSpecial { score += 20 }

        // Штрафы
        if hasRepeatingChars(password) { score -= 10 }
        if hasSequentialChars(password) { score -= 10 }

        // Ограничиваем диапазон
        score = max(0, min(100, score))

        // Определяем уровень
        let strength: PasswordStrength
        switch score {
        case 0..<20: strength = .veryWeak
        case 20..<40: strength = .weak
        case 40..<60: strength = .medium
        case 60..<80: strength = .strong
        default: strength = .veryStrong
        }

        return (strength, score)
    }

    /// Валидация пароля
    static func validate(_ password: String) -> PasswordValidationResult {
        if password.isEmpty {
            return .invalid(LocalizedStringResource("auth.validation.password.empty"))
        }

        if password.count < minLength {
            return .invalid(LocalizedStringResource("auth.validation.password.too_short \(minLength)"), score: 0)
        }

        if password.count > maxLength {
            return .invalid(LocalizedStringResource("auth.validation.password.too_long \(maxLength)"), score: 0)
        }

        let (strength, score) = calculateStrength(password)

        if score < minScore {
            return .invalid(LocalizedStringResource("auth.validation.password.too_weak \(score) \(minScore)"), strength: strength, score: score)
        }

        return .valid(strength: strength, score: score)
    }

    /// Валидация подтверждения пароля
    static func validateConfirmation(_ password: String, confirmation: String) -> PasswordValidationResult {
        if confirmation.isEmpty {
            return .invalid(LocalizedStringResource("auth.validation.password.confirm_empty"))
        }

        if password != confirmation {
            return .invalid(LocalizedStringResource("auth.validation.password.mismatch"))
        }

        return validate(password)
    }

    // MARK: - Private helpers

    private static func hasRepeatingChars(_ password: String) -> Bool {
        let chars = Array(password)
        for i in 0..<(chars.count - 2) {
            if chars[i] == chars[i + 1] && chars[i + 1] == chars[i + 2] {
                return true
            }
        }
        return false
    }

    private static func hasSequentialChars(_ password: String) -> Bool {
        let lowercased = password.lowercased()
        let sequences = [
            "abc", "bcd", "cde", "def", "efg", "fgh", "ghi", "hij", "ijk", "jkl",
            "klm", "lmn", "mno", "nop", "opq", "pqr", "qrs", "rst", "stu", "tuv",
            "uvw", "vwx", "wxy", "xyz",
            "012", "123", "234", "345", "456", "567", "678", "789", "890"
        ]

        for seq in sequences {
            if lowercased.contains(seq) {
                return true
            }
        }
        return false
    }
}
