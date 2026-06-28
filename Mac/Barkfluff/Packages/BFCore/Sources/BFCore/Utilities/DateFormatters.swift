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

    /// Короткая дата с двузначным годом для списка чатов: «ДД.ММ.ГГ» в ru, «M/d/yy» в en.
    private static func makeShortDateFormatter(locale: Locale) -> DateFormatter {
        let f = DateFormatter()
        f.locale = locale
        f.setLocalizedDateFormatFromTemplate("ddMMyy")
        return f
    }

    /// Отформатировать дату для отображения в списке чатов.
    ///
    /// Сегодня — только время («16:27»), вчера/позавчера — слово + время («Вчера 16:27»),
    /// старше — короткая дата с двузначным годом («23.05.26»).
    /// - Parameter locale: локаль для текстов и форматирования времени/даты.
    public static func formatForChatList(_ date: Date, locale: Locale = .current) -> String {
        let calendar = Calendar.current
        let now = Date()

        if calendar.isDateInToday(date) {
            return makeTimeFormatter(locale: locale).string(from: date)
        }

        let timeStr = makeTimeFormatter(locale: locale).string(from: date)

        if calendar.isDateInYesterday(date) {
            let word = String(localized: "bfcore.date.yesterday", bundle: .module, locale: locale)
            return "\(word) \(timeStr)"
        }

        let startOfDate = calendar.startOfDay(for: date)
        let startOfNow = calendar.startOfDay(for: now)
        if calendar.dateComponents([.day], from: startOfDate, to: startOfNow).day == 2 {
            let word = String(localized: "bfcore.date.day_before_yesterday", bundle: .module, locale: locale)
            return "\(word) \(timeStr)"
        }

        return makeShortDateFormatter(locale: locale).string(from: date)
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
