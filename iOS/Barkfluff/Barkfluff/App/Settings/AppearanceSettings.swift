//
//  AppearanceSettings.swift
//  Barkfluff (iOS)
//
//  Локальные настройки внешнего вида (UserDefaults-backed).
//  Сейчас содержит только выбор темы: системная / светлая / тёмная.
//
//  iOS-адаптация: вместо NSApp.appearance используем SwiftUI .preferredColorScheme()
//  на корне RootView. AppearanceSettings просто хранит выбранную тему и отдаёт
//  соответствующий ColorScheme?.
//

import Foundation
import Observation
import SwiftUI

/// Тема приложения, выбранная пользователем.
enum AppTheme: String, CaseIterable, Identifiable {
    case system
    case light
    case dark

    var id: String { rawValue }

    /// Локализованное название для отображения в UI.
    /// `LocalizedStringKey` — реактивно к смене `environment(\.locale)`.
    var titleKey: LocalizedStringKey {
        switch self {
        case .system: return "enum.theme.system"
        case .light:  return "enum.theme.light"
        case .dark:   return "enum.theme.dark"
        }
    }

    /// SwiftUI ColorScheme для `.preferredColorScheme()`.
    /// `nil` — следовать системной теме.
    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light:  return .light
        case .dark:   return .dark
        }
    }
}

@MainActor
@Observable
final class AppearanceSettings {

    @ObservationIgnored
    private let defaults: UserDefaults

    private enum Keys {
        static let theme = "appearance.theme"
    }

    /// Текущая выбранная тема. Изменение автоматически сохраняется в UserDefaults.
    var theme: AppTheme {
        didSet {
            defaults.set(theme.rawValue, forKey: Keys.theme)
        }
    }

    /// Преобразование текущей темы в `ColorScheme?` для `.preferredColorScheme()`.
    var colorScheme: ColorScheme? { theme.colorScheme }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let raw = defaults.string(forKey: Keys.theme),
           let stored = AppTheme(rawValue: raw) {
            self.theme = stored
        } else {
            self.theme = .system
        }
    }

    /// Сброс к дефолтам и удаление значений из UserDefaults (вызывается при logout).
    func reset() {
        theme = .system
        defaults.removeObject(forKey: Keys.theme)
    }
}
