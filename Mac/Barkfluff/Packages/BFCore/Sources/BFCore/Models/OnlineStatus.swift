//
//  OnlineStatus.swift
//  BFCore
//
//  Статус пользователя в сети
//

import Foundation

/// Статус пользователя в сети
public enum OnlineStatus: Sendable, Equatable, Hashable {
    case online
    /// `lastSeen == nil` означает offline без известного времени последнего визита
    case offline(lastSeen: Date?)
    case unknown

    /// Пользователь сейчас онлайн
    public var isOnline: Bool {
        if case .online = self { return true }
        return false
    }

    /// Человекочитаемая строка для UI (системная локаль).
    public var displayText: String { displayText(in: .current) }

    /// Человекочитаемая строка для UI с явной локалью (для реактивного UI).
    public func displayText(in locale: Locale) -> String {
        switch self {
        case .online:
            return String(localized: "bfcore.online.online", bundle: .module, locale: locale)
        case .offline(let lastSeen):
            guard let lastSeen else {
                return String(localized: "bfcore.online.last_seen_unknown", bundle: .module, locale: locale)
            }
            return Self.formatLastSeen(lastSeen, locale: locale)
        case .unknown:
            return ""
        }
    }

    /// Форматирование "Был(а) N минут назад" и т.д.
    private static func formatLastSeen(_ date: Date, locale: Locale) -> String {
        let now = Date()
        let interval = now.timeIntervalSince(date)

        if interval < 60 {
            return String(localized: "bfcore.online.last_seen_just_now", bundle: .module, locale: locale)
        } else if interval < 3600 {
            let minutes = Int(interval / 60)
            return String(localized: "bfcore.online.last_seen_minutes \(minutes)", bundle: .module, locale: locale)
        } else if interval < 86400 {
            let hours = Int(interval / 3600)
            return String(localized: "bfcore.online.last_seen_hours \(hours)", bundle: .module, locale: locale)
        } else if interval < 604800 {
            let days = Int(interval / 86400)
            return String(localized: "bfcore.online.last_seen_days \(days)", bundle: .module, locale: locale)
        } else {
            let formatter = DateFormatter()
            formatter.dateStyle = .short
            formatter.timeStyle = .short
            formatter.locale = locale
            return String(localized: "bfcore.online.last_seen_date \(formatter.string(from: date))", bundle: .module, locale: locale)
        }
    }
}
