//
//  DateFormatters.swift
//  BFCore
//
//  Форматтеры дат
//

import Foundation

/// Утилиты для форматирования дат
public enum DateFormatterHelper {
    /// Форматтер для отображения времени сообщений
    nonisolated(unsafe) public static let timeFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .none
        formatter.timeStyle = .short
        return formatter
    }()

    /// Форматтер для отображения даты сообщений
    nonisolated(unsafe) public static let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .short
        formatter.timeStyle = .none
        return formatter
    }()

    /// Форматтер для отображения даты и времени
    nonisolated(unsafe) public static let dateTimeFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .short
        formatter.timeStyle = .short
        return formatter
    }()

    /// ISO8601 форматтер для парсинга дат с сервера
    nonisolated(unsafe) public static let iso8601Formatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    /// Форматтер для относительного отображения времени
    nonisolated(unsafe) public static let relativeFormatter: RelativeDateTimeFormatter = {
        let formatter = RelativeDateTimeFormatter()
        formatter.unitsStyle = .abbreviated
        return formatter
    }()

    /// Отформатировать дату для отображения в списке чатов
    public static func formatForChatList(_ date: Date) -> String {
        let calendar = Calendar.current
        let now = Date()

        if calendar.isDateInToday(date) {
            return timeFormatter.string(from: date)
        } else if calendar.isDateInYesterday(date) {
            return "Вчера"
        } else if let daysAgo = calendar.dateComponents([.day], from: date, to: now).day, daysAgo < 7 {
            return relativeFormatter.localizedString(for: date, relativeTo: now)
        } else {
            return dateFormatter.string(from: date)
        }
    }

    /// Отформатировать дату для отображения в сообщении
    public static func formatForMessage(_ date: Date) -> String {
        let calendar = Calendar.current

        if calendar.isDateInToday(date) {
            return timeFormatter.string(from: date)
        } else if calendar.isDateInYesterday(date) {
            return "Вчера, \(timeFormatter.string(from: date))"
        } else {
            return dateTimeFormatter.string(from: date)
        }
    }

    /// Отформатировать дату последней активности
    public static func formatLastActive(_ date: Date) -> String {
        let calendar = Calendar.current
        let now = Date()

        if let minutes = calendar.dateComponents([.minute], from: date, to: now).minute, minutes < 5 {
            return "В сети"
        } else if calendar.isDateInToday(date) {
            return "Сегодня, \(timeFormatter.string(from: date))"
        } else {
            return dateTimeFormatter.string(from: date)
        }
    }
}
