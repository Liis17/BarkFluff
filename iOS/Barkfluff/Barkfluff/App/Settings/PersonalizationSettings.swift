//
//  PersonalizationSettings.swift
//  Barkfluff (iOS)
//
//  Локальные настройки персонализации (UserDefaults-backed).
//  Радиус закругления пузырей, выбранный фон чата, размытие и затемнение —
//  всё хранится только локально, как в Android- и macOS-клиентах.
//

import Foundation
import Observation

@Observable
final class PersonalizationSettings {

    @ObservationIgnored
    private let defaults: UserDefaults

    private enum Keys {
        static let cornerRadius     = "personalization.bubbleCornerRadius"
        static let blurEnabled      = "personalization.bgBlurEnabled"
        static let blurRadius       = "personalization.bgBlurRadius"
        static let dimPercent       = "personalization.bgDimPercent"
        static let currentBgFileID  = "personalization.currentBackgroundFileID"
    }

    /// Радиус скругления пузыря сообщения, pt. Диапазон UI 0…30, дефолт 18.
    var bubbleCornerRadius: Int {
        didSet { defaults.set(bubbleCornerRadius, forKey: Keys.cornerRadius) }
    }

    /// Применять ли размытие к фоновой картинке чата.
    var backgroundBlurEnabled: Bool {
        didSet { defaults.set(backgroundBlurEnabled, forKey: Keys.blurEnabled) }
    }

    /// Радиус размытия фона. Диапазон UI 1…25, дефолт 12.
    var backgroundBlurRadius: Int {
        didSet { defaults.set(backgroundBlurRadius, forKey: Keys.blurRadius) }
    }

    /// Затемнение фона в процентах. Диапазон UI 0…100, дефолт 0.
    var backgroundDimPercent: Int {
        didSet { defaults.set(backgroundDimPercent, forKey: Keys.dimPercent) }
    }

    /// fileID текущего фона чата. Пустая строка — без фона.
    var currentBackgroundFileID: String {
        didSet { defaults.set(currentBackgroundFileID, forKey: Keys.currentBgFileID) }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.bubbleCornerRadius = defaults.object(forKey: Keys.cornerRadius) as? Int ?? 18
        self.backgroundBlurEnabled = defaults.object(forKey: Keys.blurEnabled) as? Bool ?? false
        self.backgroundBlurRadius = defaults.object(forKey: Keys.blurRadius) as? Int ?? 12
        self.backgroundDimPercent = defaults.object(forKey: Keys.dimPercent) as? Int ?? 0
        self.currentBackgroundFileID = defaults.string(forKey: Keys.currentBgFileID) ?? ""
    }
}
