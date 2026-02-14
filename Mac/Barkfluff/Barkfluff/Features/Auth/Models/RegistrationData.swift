//
//  RegistrationData.swift
//  Barkfluff
//
//  Модель данных для хранения состояния регистрации
//

import Foundation
import SwiftUI
import BFNetworking

/// Данные регистрации, передаваемые между шагами
@Observable
final class RegistrationData {
    // Шаг 1: Личные данные
    var firstName: String = ""
    var lastName: String = ""

    // Шаг 2: Username
    var username: String = ""

    // Шаг 3: Email
    var email: String = ""

    // Шаг 4: Подтверждение email
    var confirmationCode: String = ""
    var codeID: String?

    // Шаг 5: Пароль
    var password: String = ""
    var confirmPassword: String = ""

    // Шаг 6: Аватар
    var avatarImage: Image?
    var avatarData: Data?
    var avatarFileID: String?

    // Шаг 7: Био
    var bio: String = ""

    // Шаг 8: 2FA
    var otpSetupInfo: OTPSetupInfo?
    var otpCode: String = ""
    var is2FAEnabled: Bool = false

    init() {}

    /// Сброс всех данных
    func reset() {
        firstName = ""
        lastName = ""
        username = ""
        email = ""
        confirmationCode = ""
        codeID = nil
        password = ""
        confirmPassword = ""
        avatarImage = nil
        avatarData = nil
        avatarFileID = nil
        bio = ""
        otpSetupInfo = nil
        otpCode = ""
        is2FAEnabled = false
    }
}

// MARK: - Валидация данных

extension RegistrationData {
    /// Проверка валидности данных для шага
    func isValid(for step: RegistrationStep) -> Bool {
        switch step {
        case .personalInfo:
            return firstName.count >= 3 && lastName.count >= 3

        case .username:
            return username.count >= 3 && username.count <= 30

        case .email:
            return isValidEmail(email)

        case .confirmEmail:
            return confirmationCode.count == 6 && codeID != nil

        case .password:
            return password.count >= 8 && password == confirmPassword

        case .avatar, .bio, .twoFA:
            return true // Опциональные шаги всегда валидны

        case .completion:
            return true
        }
    }

    private func isValidEmail(_ email: String) -> Bool {
        let emailRegex = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,64}"
        return NSPredicate(format: "SELF MATCHES %@", emailRegex).evaluate(with: email)
    }
}
