//
//  UsernameValidationStatus.swift
//  Barkfluff (iOS)
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

    var message: String {
        switch self {
        case .checking: "Проверка..."
        case .available: "Имя доступно"
        case .taken: "Имя уже занято"
        case .tooShort: "Минимум 3 символа"
        case .invalid: "Только буквы, цифры и подчёркивание"
        case .unchanged: ""
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
