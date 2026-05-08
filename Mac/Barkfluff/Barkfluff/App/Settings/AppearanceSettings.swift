//
//  AppearanceSettings.swift
//  Barkfluff
//
//  Локальные настройки внешнего вида (UserDefaults-backed).
//  Сейчас содержит только выбор темы: системная / светлая / тёмная.
//

import AppKit
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
    var title: String {
        switch self {
        case .system: return "Системная"
        case .light:  return "Светлая"
        case .dark:   return "Тёмная"
        }
    }

    /// `NSAppearance` для `NSApplication.shared.appearance`.
    /// `nil` — следовать системной теме.
    var nsAppearance: NSAppearance? {
        switch self {
        case .system: return nil
        case .light:  return NSAppearance(named: .aqua)
        case .dark:   return NSAppearance(named: .darkAqua)
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

    /// Текущая выбранная тема. Изменение автоматически сохраняется в UserDefaults
    /// и применяется ко всему процессу через `NSApp.appearance`.
    var theme: AppTheme {
        didSet {
            defaults.set(theme.rawValue, forKey: Keys.theme)
            apply()
        }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let raw = defaults.string(forKey: Keys.theme),
           let stored = AppTheme(rawValue: raw) {
            self.theme = stored
        } else {
            self.theme = .system
        }
        apply()
    }

    /// Источник истины для темы — `NSApplication.shared.appearance`.
    /// SwiftUI-модификатор `.preferredColorScheme(nil)` на macOS не сбрасывает форсированную
    /// тему на части AppKit-бэкэнда (NSWindow chrome, sidebar, NSVisualEffectView), поэтому
    /// после dark → system половина UI остаётся тёмной. Прямое присвоение `NSApp.appearance`
    /// каскадно прокидывается во все окна процесса, а `nil` корректно возвращает «следовать системе».
    private func apply() {
        NSApplication.shared.appearance = theme.nsAppearance
    }
}
