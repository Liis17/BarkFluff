//
//  RegistrationStep.swift
//  Barkfluff
//
//  Enum для шагов регистрации
//

import SwiftUI

/// Шаги регистрации (9 шагов)
enum RegistrationStep: Int, CaseIterable, Identifiable {
    case personalInfo = 1    // Имя, Фамилия
    case username = 2        // Username
    case email = 3           // Email
    case confirmEmail = 4    // Код подтверждения
    case password = 5        // Пароль (КРИТИЧНО)
    case avatar = 6          // Аватар (опционально)
    case bio = 7             // Био (опционально)
    case twoFA = 8           // 2FA (опционально)
    case completion = 9      // Завершение

    var id: Int { rawValue }

    /// Локализованный заголовок шага
    var title: LocalizedStringKey {
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

    /// Локализованный подзаголовок шага
    var subtitle: LocalizedStringKey {
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

    /// Является ли шаг опциональным (можно пропустить)
    var isOptional: Bool {
        switch self {
        case .avatar, .bio, .twoFA:
            return true
        default:
            return false
        }
    }

    /// Прогресс выполнения (от 0 до 1)
    var progress: Double {
        Double(rawValue) / Double(RegistrationStep.allCases.count)
    }

    /// Следующий шаг или nil если это последний
    var next: RegistrationStep? {
        RegistrationStep(rawValue: rawValue + 1)
    }

    /// Предыдущий шаг или nil если это первый
    var previous: RegistrationStep? {
        RegistrationStep(rawValue: rawValue - 1)
    }
}
