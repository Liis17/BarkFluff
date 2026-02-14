//
//  RegistrationStep.swift
//  Barkfluff
//
//  Enum для шагов регистрации
//

import Foundation

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

    /// Заголовок шага
    var title: String {
        switch self {
        case .personalInfo:
            return "Личные данные"
        case .username:
            return "Имя пользователя"
        case .email:
            return "Email"
        case .confirmEmail:
            return "Подтверждение"
        case .password:
            return "Пароль"
        case .avatar:
            return "Аватар"
        case .bio:
            return "О себе"
        case .twoFA:
            return "Двухфакторная аутентификация"
        case .completion:
            return "Готово!"
        }
    }

    /// Подзаголовок шага
    var subtitle: String {
        switch self {
        case .personalInfo:
            return "Как вас зовут?"
        case .username:
            return "Выберите уникальное имя пользователя"
        case .email:
            return "Укажите ваш email"
        case .confirmEmail:
            return "Введите код из письма"
        case .password:
            return "Придумайте надёжный пароль"
        case .avatar:
            return "Загрузите фото профиля"
        case .bio:
            return "Расскажите о себе"
        case .twoFA:
            return "Добавьте дополнительную защиту"
        case .completion:
            return "Аккаунт создан"
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
