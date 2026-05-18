//
//  LocalizationSettings.swift
//  Barkfluff
//
//  Локальные настройки языка интерфейса (UserDefaults-backed).
//  Выбор: системный / русский / английский.
//

import Foundation
import Observation
import SwiftUI

/// Язык интерфейса, выбранный пользователем.
enum AppLanguage: String, CaseIterable, Identifiable {
    case system
    case ru
    case en
    case es
    case zhHans = "zh-Hans"
    case de

    var id: String { rawValue }

    /// Локализованное название для отображения в UI.
    /// Используется как `LocalizedStringKey`, чтобы значение было реактивно к смене языка.
    var titleKey: LocalizedStringKey {
        switch self {
        case .system: return "language.system"
        case .ru:     return "language.russian"
        case .en:     return "language.english"
        case .es:     return "language.spanish"
        case .zhHans: return "language.chinese"
        case .de:     return "language.german"
        }
    }

    /// Флаг страны, отображаемый слева от названия языка в Settings.
    var flagEmoji: String {
        switch self {
        case .system: return "🌐"
        case .ru:     return "🇷🇺"
        case .en:     return "🇺🇸"
        case .es:     return "🇪🇸"
        case .zhHans: return "🇨🇳"
        case .de:     return "🇩🇪"
        }
    }

    /// Явная `Locale` для не-системных вариантов; `nil` для `.system`.
    var explicitLocale: Locale? {
        switch self {
        case .system: return nil
        case .ru:     return Locale(identifier: "ru")
        case .en:     return Locale(identifier: "en")
        case .es:     return Locale(identifier: "es")
        case .zhHans: return Locale(identifier: "zh-Hans")
        case .de:     return Locale(identifier: "de")
        }
    }
}

@MainActor
@Observable
final class LocalizationSettings {

    @ObservationIgnored
    private let defaults: UserDefaults

    private enum Keys {
        static let language = "localization.language"
    }

    /// Выбранный пользователем язык. Изменение автоматически сохраняется в UserDefaults.
    /// Применяется в UI через `.environment(\.locale, container.localizationSettings.appliedLocale)`
    /// на корневой `Scene` — SwiftUI перерисует дерево, потому что `appliedLocale` читается
    /// внутри `body` и класс `@Observable`.
    var language: AppLanguage {
        didSet {
            defaults.set(language.rawValue, forKey: Keys.language)
        }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let raw = defaults.string(forKey: Keys.language),
           let stored = AppLanguage(rawValue: raw) {
            self.language = stored
        } else {
            self.language = .system
        }
    }

    /// Эффективная `Locale` для инъекции в SwiftUI environment.
    /// - `.system` → язык устройства, если поддерживаемый; иначе fallback на `en`.
    /// - явный язык → соответствующая `Locale`.
    var appliedLocale: Locale {
        if let explicit = language.explicitLocale {
            return explicit
        }
        let code = Locale.current.language.languageCode?.identifier ?? "en"
        switch code {
        case "ru": return Locale(identifier: "ru")
        case "es": return Locale(identifier: "es")
        case "de": return Locale(identifier: "de")
        case "zh": return Locale(identifier: "zh-Hans")
        default:   return Locale(identifier: "en")
        }
    }

    /// Сброс к дефолту и удаление значения из UserDefaults (вызывается при logout).
    func reset() {
        language = .system
        defaults.removeObject(forKey: Keys.language)
    }
}
