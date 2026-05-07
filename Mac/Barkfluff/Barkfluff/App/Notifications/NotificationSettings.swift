//
//  NotificationSettings.swift
//  Barkfluff
//
//  Пользовательские настройки уведомлений (UserDefaults-backed).
//

import Foundation
import Observation

@Observable
final class NotificationSettings {

    @ObservationIgnored
    private let defaults: UserDefaults

    private enum Keys {
        static let show = "notif.showNotifications"
        static let sound = "notif.playSound"
    }

    /// Показывать ли системные баннеры для входящих сообщений.
    var showNotifications: Bool {
        didSet { defaults.set(showNotifications, forKey: Keys.show) }
    }

    /// Проигрывать ли звук при показе уведомления.
    var playSound: Bool {
        didSet { defaults.set(playSound, forKey: Keys.sound) }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        // Дефолты — true (как и в исходном стабе settings view).
        self.showNotifications = defaults.object(forKey: Keys.show) as? Bool ?? true
        self.playSound = defaults.object(forKey: Keys.sound) as? Bool ?? true
    }
}
