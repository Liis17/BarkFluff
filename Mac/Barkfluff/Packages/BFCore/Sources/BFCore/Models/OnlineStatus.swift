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

    /// Человекочитаемая строка для UI
    public var displayText: String {
        switch self {
        case .online:
            return "В сети"
        case .offline(let lastSeen):
            guard let lastSeen else { return "Был(а) давно" }
            return Self.formatLastSeen(lastSeen)
        case .unknown:
            return ""
        }
    }

    /// Форматирование "Был(а) N минут назад" и т.д.
    private static func formatLastSeen(_ date: Date) -> String {
        let now = Date()
        let interval = now.timeIntervalSince(date)

        if interval < 60 {
            return "Был(а) только что"
        } else if interval < 3600 {
            let minutes = Int(interval / 60)
            let suffix = minutes == 1 ? "у" : (minutes < 5 ? "ы" : "")
            return "Был(а) \(minutes) минут\(suffix) назад"
        } else if interval < 86400 {
            let hours = Int(interval / 3600)
            let suffix = hours == 1 ? "" : (hours < 5 ? "а" : "ов")
            return "Был(а) \(hours) час\(suffix) назад"
        } else if interval < 604800 {
            let days = Int(interval / 86400)
            let suffix: String
            if days == 1 {
                suffix = "ень"
            } else if days < 5 {
                suffix = "ня"
            } else {
                suffix = "ней"
            }
            return "Был(а) \(days) д\(suffix) назад"
        } else {
            let formatter = DateFormatter()
            formatter.dateStyle = .short
            formatter.timeStyle = .short
            return "Был(а) \(formatter.string(from: date))"
        }
    }
}
