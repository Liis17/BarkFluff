//
//  MessageGrouper.swift
//  Barkfluff
//
//  Группировка сообщений по дате и отправителю
//

import Foundation
import BFCore

/// Элемент для отображения в списке сообщений
enum MessageListItem: Identifiable, Hashable {
    /// Разделитель дат
    case dateSeparator(date: Date)
    /// Сообщение с метаинформацией о группировке
    case message(Message, MessageGroupInfo)

    var id: String {
        switch self {
        case .dateSeparator(let date):
            return "date-\(date.timeIntervalSince1970)"
        case .message(let msg, _):
            if let localID = msg.localID { return "local-\(localID)" }
            return "msg-\(msg.id)"
        }
    }
}

/// Информация о группировке сообщения
struct MessageGroupInfo: Hashable {
    /// Первое сообщение в группе (нужно показать имя отправителя)
    let isFirstInGroup: Bool
    /// Последнее сообщение в группе (нужно показать время)
    let isLastInGroup: Bool
    /// Показывать время для этого сообщения
    let showTime: Bool
}

/// Группировщик сообщений
enum MessageGrouper {
    /// Интервал группировки сообщений от одного отправителя (в секундах)
    static let groupingInterval: TimeInterval = 60

    /// Сгруппировать сообщения для отображения
    /// - Parameters:
    ///   - messages: Массив сообщений (должен быть отсортирован по дате)
    ///   - currentUserID: ID текущего пользователя
    ///   - showTimeFor: Интервал показа времени (каждые N сообщений в группе)
    /// - Returns: Массив элементов для отображения
    static func groupMessages(
        _ messages: [Message],
        currentUserID: Int64,
        showTimeInterval: TimeInterval = 300 // Показывать время каждые 5 минут
    ) -> [MessageListItem] {
        guard !messages.isEmpty else { return [] }

        var items: [MessageListItem] = []
        var currentDate: Date?
        var currentSenderID: Int64?
        var groupStartTime: Date?
        var messageCountInCurrentGroup = 0

        for (index, message) in messages.enumerated() {
            let messageDate = message.sentAt
            let calendar = Calendar.current

            // Проверяем, нужно ли добавить разделитель дат
            if let date = currentDate {
                let sameDay = calendar.isDate(date, inSameDayAs: messageDate)
                if !sameDay {
                    items.append(.dateSeparator(date: messageDate))
                    currentSenderID = nil
                    groupStartTime = nil
                    messageCountInCurrentGroup = 0
                }
            } else {
                // Первое сообщение — добавляем разделитель
                items.append(.dateSeparator(date: messageDate))
            }
            currentDate = messageDate

            // Определяем группировку по отправителю
            let isFirstInGroup: Bool
            let isLastInGroup: Bool

            // Проверяем, является ли это новым отправителем
            let isNewSender = currentSenderID != message.senderID

            // Проверяем интервал времени
            let timeInterval: TimeInterval
            if let startTime = groupStartTime {
                timeInterval = messageDate.timeIntervalSince(startTime)
            } else {
                timeInterval = 0
            }

            // Начинаем новую группу если:
            // - Новый отправитель
            // - Прошло больше groupingInterval секунд
            let shouldStartNewGroup = isNewSender || timeInterval > groupingInterval

            if shouldStartNewGroup {
                isFirstInGroup = true
                messageCountInCurrentGroup = 1
                groupStartTime = messageDate
            } else {
                isFirstInGroup = false
                messageCountInCurrentGroup += 1
            }

            // Проверяем, последнее ли это сообщение в группе
            let nextMessage = messages[safe: index + 1]
            let isLastSender = nextMessage?.senderID != message.senderID
            let nextTimeInterval: TimeInterval
            if let nextDate = nextMessage?.sentAt, let startTime = groupStartTime {
                nextTimeInterval = nextDate.timeIntervalSince(startTime)
            } else {
                nextTimeInterval = groupingInterval + 1
            }

            isLastInGroup = isLastSender || nextTimeInterval > groupingInterval

            // Определяем, показывать ли время
            // Показываем время для последнего сообщения в группе или если прошло достаточно времени
            let showTime = isLastInGroup || timeInterval >= showTimeInterval

            let groupInfo = MessageGroupInfo(
                isFirstInGroup: isFirstInGroup,
                isLastInGroup: isLastInGroup,
                showTime: showTime
            )

            items.append(.message(message, groupInfo))

            currentSenderID = message.senderID

            // Сбрасываем время группы если начинаем новую
            if shouldStartNewGroup {
                groupStartTime = messageDate
            }
        }

        return items
    }

    /// Форматировать дату для разделителя
    static func formatDateSeparator(_ date: Date) -> String {
        let calendar = Calendar.current

        if calendar.isDateInToday(date) {
            return "Сегодня"
        } else if calendar.isDateInYesterday(date) {
            return "Вчера"
        } else {
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "ru_RU")
            formatter.dateFormat = "d MMMM"
            return formatter.string(from: date)
        }
    }
}

// MARK: - Array Extension

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
