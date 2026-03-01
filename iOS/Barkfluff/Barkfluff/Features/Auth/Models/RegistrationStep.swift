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

    /// Заголовок шага
    var title: String {
        switch self {
        case .personalInfo:
            return "Создание аккаунта"
        case .username:
            return "Выберите имя"
        case .email:
            return "Ваш email"
        case .confirmEmail:
            return "Подтверждение"
        case .password:
            return "Пароль"
        case .avatar:
            return "Фото профиля"
        case .bio:
            return "О себе"
        case .twoFA:
            return "Безопасность"
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
            return "Уникальное имя для вашего аккаунта"
        case .email:
            return "Для подтверждения и восстановления"
        case .confirmEmail:
            return "Введите код из письма"
        case .password:
            return "Надёжный пароль для защиты"
        case .avatar:
            return "Добавьте фото (необязательно)"
        case .bio:
            return "Расскажите о себе (необязательно)"
        case .twoFA:
            return "Двухфакторная аутентификация"
        case .completion:
            return "Добро пожаловать!"
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
