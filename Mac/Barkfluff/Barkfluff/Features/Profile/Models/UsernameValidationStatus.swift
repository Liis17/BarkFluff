//
//  UsernameValidationStatus.swift
//  Barkfluff
//
//  Статус валидации имени пользователя
//

import SwiftUI

/// Статус валидации имени пользователя
enum UsernameValidationStatus: Equatable {
    case checking
    case available
    case taken
    case tooShort      // < 3 символов
    case invalid       // недопустимые символы
    case unchanged     // совпадает с текущим

    var messageKey: LocalizedStringKey? {
        switch self {
        case .checking: "profile.validation.username.checking"
        case .available: "profile.validation.username.available"
        case .taken: "profile.validation.username.taken"
        case .tooShort: "profile.validation.username.too_short"
        case .invalid: "profile.validation.username.invalid"
        case .unchanged: nil
        }
    }

    var icon: String {
        switch self {
        case .checking: "arrow.trianglehead.2.clockwise"
        case .available, .unchanged: "checkmark.circle.fill"
        case .taken: "xmark.circle.fill"
        case .tooShort, .invalid: "exclamationmark.triangle.fill"
        }
    }

    var color: Color {
        switch self {
        case .checking: .secondary
        case .available, .unchanged: .green
        case .taken, .tooShort, .invalid: .red
        }
    }
}
