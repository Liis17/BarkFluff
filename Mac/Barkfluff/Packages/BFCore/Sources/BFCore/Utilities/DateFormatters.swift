//
//  DateFormatters.swift
//  BFCore
//
//  Форматтеры дат
//

import Foundation

/// Утилиты для форматирования дат
public enum DateFormatterHelper {
    /// Форматтер для отображения времени сообщений (системная локаль).
    nonisolated(unsafe) public static let timeFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .none
        formatter.timeStyle = .short
        return formatter
    }()

    /// Форматтер для отображения даты сообщений (системная локаль).
    nonisolated(unsafe) public static let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .short
        formatter.timeStyle = .none
        return formatter
    }()

    /// Форматтер для отображения даты и времени (системная локаль).
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

    /// Форматтер для относительного отображения времени (системная локаль).
    nonisolated(unsafe) public static let relativeFormatter: RelativeDateTimeFormatter = {
        let formatter = RelativeDateTimeFormatter()
        formatter.unitsStyle = .abbreviated
        return formatter
    }()

    // MARK: - Locale-aware форматтеры

    private static func makeTimeFormatter(locale: Locale) -> DateFormatter {
        let f = DateFormatter()
        f.dateStyle = .none
        f.timeStyle = .short
        f.locale = locale
        return f
    }

    private static func makeDateFormatter(locale: Locale) -> DateFormatter {
        let f = DateFormatter()
        f.dateStyle = .short
        f.timeStyle = .none
        f.locale = locale
        return f
    }

    private static func makeDateTimeFormatter(locale: Locale) -> DateFormatter {
        let f = DateFormatter()
        f.dateStyle = .short
        f.timeStyle = .short
        f.locale = locale
        return f
    }

    private static func makeRelativeFormatter(locale: Locale) -> RelativeDateTimeFormatter {
        let f = RelativeDateTimeFormatter()
        f.unitsStyle = .abbreviated
        f.locale = locale
        return f
    }

    /// Отформатировать дату для отображения в списке чатов.
    /// - Parameter locale: локаль для текстов («Вчера»/«Yesterday») и форматирования времени.
    public static func formatForChatList(_ date: Date, locale: Locale = .current) -> String {
        let calendar = Calendar.current
        let now = Date()

        if calendar.isDateInToday(date) {
            return makeTimeFormatter(locale: locale).string(from: date)
        } else if calendar.isDateInYesterday(date) {
            return String(localized: "bfcore.date.yesterday", bundle: .module, locale: locale)
        } else if let daysAgo = calendar.dateComponents([.day], from: date, to: now).day, daysAgo < 7 {
            return makeRelativeFormatter(locale: locale).localizedString(for: date, relativeTo: now)
        } else {
            return makeDateFormatter(locale: locale).string(from: date)
        }
    }

    /// Отформатировать дату для отображения в сообщении.
    public static func formatForMessage(_ date: Date, locale: Locale = .current) -> String {
        let calendar = Calendar.current

        if calendar.isDateInToday(date) {
            return makeTimeFormatter(locale: locale).string(from: date)
        } else if calendar.isDateInYesterday(date) {
            let time = makeTimeFormatter(locale: locale).string(from: date)
            return String(localized: "bfcore.date.yesterday_at \(time)", bundle: .module, locale: locale)
        } else {
            return makeDateTimeFormatter(locale: locale).string(from: date)
        }
    }

    /// Отформатировать дату последней активности.
    public static func formatLastActive(_ date: Date, locale: Locale = .current) -> String {
        let calendar = Calendar.current
        let now = Date()

        if let minutes = calendar.dateComponents([.minute], from: date, to: now).minute, minutes < 5 {
            return String(localized: "bfcore.online.online", bundle: .module, locale: locale)
        } else if calendar.isDateInToday(date) {
            let time = makeTimeFormatter(locale: locale).string(from: date)
            return String(localized: "bfcore.date.today_at \(time)", bundle: .module, locale: locale)
        } else {
            return makeDateTimeFormatter(locale: locale).string(from: date)
        }
    }
}
