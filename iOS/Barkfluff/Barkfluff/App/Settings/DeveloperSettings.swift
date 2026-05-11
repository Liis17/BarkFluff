//
//  DeveloperSettings.swift
//  Barkfluff (iOS)
//
//  Локальные настройки для отладки (UserDefaults-backed).
//  Сейчас содержит флаги «показывать ID пользователей/чатов» в профиле собеседника.
//

import Foundation
import Observation

@MainActor
@Observable
final class DeveloperSettings {

    @ObservationIgnored
    private let defaults: UserDefaults

    private enum Keys {
        static let showUserIDs = "developer.showUserIDs"
        static let showChatIDs = "developer.showChatIDs"
    }

    /// Показывать ID пользователей в профиле собеседника.
    var showUserIDs: Bool {
        didSet { defaults.set(showUserIDs, forKey: Keys.showUserIDs) }
    }

    /// Показывать ID чата в профиле собеседника.
    var showChatIDs: Bool {
        didSet { defaults.set(showChatIDs, forKey: Keys.showChatIDs) }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.showUserIDs = defaults.bool(forKey: Keys.showUserIDs)
        self.showChatIDs = defaults.bool(forKey: Keys.showChatIDs)
    }

    /// Сброс к дефолтам и удаление значений из UserDefaults (вызывается из `DependencyContainer.reset()` при logout).
    func reset() {
        showUserIDs = false
        showChatIDs = false
        defaults.removeObject(forKey: Keys.showUserIDs)
        defaults.removeObject(forKey: Keys.showChatIDs)
    }
}
