//
//  Theme.swift
//  Barkfluff
//
//  Тема приложения (iOS версия)
//

import SwiftUI

/// Тема приложения BarkFluff
enum Theme {
    // MARK: - Colors

    /// Основные цвета
    enum Colors {
        static let accent = Color.accentColor
        static let primary = Color.primary
        static let secondary = Color.secondary
        static let background = Color(uiColor: .systemBackground)
    }

    // MARK: - Spacing

    /// Отступы
    enum Spacing {
        static let xxs: CGFloat = 2
        static let xs: CGFloat = 4
        static let sm: CGFloat = 8
        static let md: CGFloat = 12
        static let lg: CGFloat = 16
        static let xl: CGFloat = 24
        static let xxl: CGFloat = 32
    }

    // MARK: - Radius

    /// Радиусы скругления
    enum Radius {
        static let sm: CGFloat = 4
        static let md: CGFloat = 8
        static let lg: CGFloat = 12
        static let xl: CGFloat = 16
        static let full: CGFloat = .infinity
    }

    // MARK: - Animation

    /// Анимации
    enum Animation {
        static let `default` = SwiftUI.Animation.smooth
        static let spring = SwiftUI.Animation.spring
        static let press = SwiftUI.Animation.easeOut(duration: 0.12)
        static let stateChange = SwiftUI.Animation.smooth(duration: 0.2)
    }
}
