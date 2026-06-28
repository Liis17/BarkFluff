//
//  RegistrationStep.swift
//  Barkfluff
//
//  Enum для шагов регистрации (iOS версия)
//

import Foundation

/// Шаги регистрации
enum RegistrationStep: Int, CaseIterable, Identifiable {
    case personalInfo = 1    // Имя, Фамилия
    case username = 2        // Username
    case email = 3           // Email
    case confirmEmail = 4    // Код подтверждения
    case password = 5        // Пароль
    case avatar = 6          // Аватар (опционально)
    case bio = 7             // Био (опционально)
    case twoFA = 8           // 2FA (опционально)
    case completion = 9      // Завершение

    var id: Int { rawValue }

    /// Ключ заголовка шага (для локализации)
    var title: String {
        switch self {
        case .personalInfo:
            return "auth.register.step.personal_info.title"
        case .username:
            return "auth.register.step.username.title"
        case .email:
            return "auth.register.step.email.title"
        case .confirmEmail:
            return "auth.register.step.confirm_email.title"
        case .password:
            return "auth.register.step.password.title"
        case .avatar:
            return "auth.register.step.avatar.title"
        case .bio:
            return "auth.register.step.bio.title"
        case .twoFA:
            return "auth.register.step.two_fa.title"
        case .completion:
            return "auth.register.step.completion.header"
        }
    }

    /// Ключ подзаголовка шага (для локализации)
    var subtitle: String {
        switch self {
        case .personalInfo:
            return "auth.register.step.personal_info.subtitle"
        case .username:
            return "auth.register.step.username.subtitle"
        case .email:
            return "auth.register.step.email.subtitle"
        case .confirmEmail:
            return "auth.register.step.confirm_email.subtitle"
        case .password:
            return "auth.register.step.password.subtitle"
        case .avatar:
            return "auth.register.step.avatar.subtitle"
        case .bio:
            return "auth.register.step.bio.subtitle"
        case .twoFA:
            return "auth.register.step.two_fa.subtitle"
        case .completion:
            return "auth.register.step.completion.subtitle_header"
        }
    }

    /// Иконка шага
    var iconName: String {
        switch self {
        case .personalInfo:
            return "person.fill"
        case .username:
            return "at"
        case .email:
            return "envelope.fill"
        case .confirmEmail:
            return "number.circle.fill"
        case .password:
            return "lock.fill"
        case .avatar:
            return "camera.fill"
        case .bio:
            return "text.quote"
        case .twoFA:
            return "shield.checkered"
        case .completion:
            return "checkmark.circle.fill"
        }
    }

    /// Является ли шаг опциональным
    var isOptional: Bool {
        switch self {
        case .avatar, .bio, .twoFA:
            return true
        default:
            return false
        }
    }

    /// Прогресс (от 0 до 1)
    var progress: Double {
        Double(rawValue) / Double(RegistrationStep.allCases.count)
    }

    /// Следующий шаг или nil
    var next: RegistrationStep? {
        RegistrationStep(rawValue: rawValue + 1)
    }

    /// Предыдущий шаг или nil
    var previous: RegistrationStep? {
        RegistrationStep(rawValue: rawValue - 1)
    }
}
