//
//  NotificationContentBuilder.swift
//  Barkfluff
//
//  Чистая сборка UNNotificationContent из NewMessageEvent + контекста.
//  Не делает сетевых вызовов — все зависимости приходят параметрами.
//

import Foundation
import UserNotifications
import BFCore

enum NotificationContentBuilder {

    /// Собрать `UNNotificationRequest` для уведомления о входящем сообщении.
    /// - Parameters:
    ///   - event: событие из real-time стрима.
    ///   - sender: пользователь-отправитель (если удалось резолвнуть). В DM используется
    ///     для заголовка, в группе — для префикса в теле.
    ///   - chat: чат, в который пришло сообщение. Для группы определяет заголовок.
    ///   - attachmentFileURL: уже скопированный в temp файл аватара (см. NotificationService).
    ///     При nil уведомление уходит без картинки.
    ///   - playSound: проигрывать ли системный звук.
    static func build(
        event: NewMessageEvent,
        sender: User?,
        chat: Chat?,
        attachmentFileURL: URL?,
        playSound: Bool
    ) -> UNNotificationRequest {
        let content = UNMutableNotificationContent()

        let senderDisplayName = senderName(sender: sender, message: event.message)
        let isGroup = chat?.isGroupChat ?? false

        if isGroup, let chat {
            content.title = chat.title
            content.subtitle = senderDisplayName
            content.body = bodyText(message: event.message, prefix: nil)
        } else {
            content.title = senderDisplayName
            content.body = bodyText(message: event.message, prefix: nil)
        }

        if playSound {
            content.sound = .default
        }

        // Группировка системой по чату — баннеры одного чата складываются стопкой.
        content.threadIdentifier = event.chatID

        // Identifier нужен для clearDelivered при открытии чата.
        let identifier = notificationIdentifier(chatID: event.chatID, messageID: event.message.id)

        // userInfo используется делегатом при клике — чтобы открыть нужный чат.
        content.userInfo = [
            "chatID": event.chatID,
            "messageID": event.message.id
        ]

        if let attachmentFileURL {
            // UNNotificationAttachment может бросить, если формат не поддерживается —
            // тогда просто продолжаем без картинки.
            if let attachment = try? UNNotificationAttachment(
                identifier: "avatar-\(event.message.id)",
                url: attachmentFileURL,
                options: nil
            ) {
                content.attachments = [attachment]
            }
        }

        return UNNotificationRequest(
            identifier: identifier,
            content: content,
            trigger: nil // показать сразу
        )
    }

    /// Identifier поста уведомления — стабильно зависит от chatID + messageID,
    /// чтобы потом можно было снять конкретные уведомления при открытии чата.
    static func notificationIdentifier(chatID: String, messageID: Int64) -> String {
        "chat-\(chatID)-msg-\(messageID)"
    }

    // MARK: - Body composition

    /// Собрать тело уведомления из текста и/или сводки по вложениям.
    /// Для группового чата `prefix` может быть «Имя:» (но мы пока используем subtitle).
    private static func bodyText(message: Message, prefix: String?) -> String {
        let text = message.content.text.trimmingCharacters(in: .whitespacesAndNewlines)
        let attachmentsSummary = attachmentSummary(message.content.attachments)

        var body = ""
        if !text.isEmpty {
            body = text
            if !attachmentsSummary.isEmpty {
                body += "\n" + attachmentsSummary
            }
        } else if !attachmentsSummary.isEmpty {
            body = attachmentsSummary
        } else {
            body = "Новое сообщение"
        }

        if let prefix, !prefix.isEmpty {
            body = prefix + " " + body
        }
        return body
    }

    /// Имя отправителя для заголовка/субтайтла. Берём из User (если резолвнули),
    /// иначе из самого события (`senderName` в proto ≈ nil), иначе плейсхолдер.
    private static func senderName(sender: User?, message: Message) -> String {
        if let sender, !sender.displayName.isEmpty {
            return sender.displayName
        }
        if let name = message.senderName, !name.isEmpty {
            return name
        }
        return "Сообщение"
    }

    // MARK: - Attachment summary

    /// Собрать строку-сводку по вложениям: эмодзи + тип + количество.
    /// Если в одном сообщении несколько типов — берём «самый визуальный».
    static func attachmentSummary(_ attachments: [MessageAttachment]) -> String {
        guard !attachments.isEmpty else { return "" }

        // Считаем по типам.
        var counts: [AttachmentType: Int] = [:]
        for att in attachments {
            counts[att.type, default: 0] += 1
        }

        // Приоритет — что показать как «основной» тип.
        let priority: [AttachmentType] = [
            .image, .video, .gif, .voice, .audio,
            .document, .sticker, .forwardedMessage
        ]

        guard let dominant = priority.first(where: { counts[$0] != nil }) else {
            return ""
        }

        let dominantCount = counts[dominant] ?? 0
        let dominantPart = format(type: dominant, count: dominantCount)

        // Если есть вложения других типов — добавим суммарный «+N»,
        // чтобы пользователь понимал что есть ещё.
        let otherCount = attachments.count - dominantCount
        if otherCount > 0 {
            return "\(dominantPart) +\(otherCount)"
        }
        return dominantPart
    }

    /// Локализованная форма единственного/множественного числа для вложения.
    private static func format(type: AttachmentType, count: Int) -> String {
        switch type {
        case .image:
            return count == 1 ? "🖼 Фото" : "🖼 \(count) фото"
        case .video:
            return count == 1 ? "🎥 Видео" : "🎥 \(count) видео"
        case .gif:
            return count == 1 ? "GIF" : "GIF ×\(count)"
        case .document:
            return count == 1 ? "📎 Документ" : "📎 \(count) \(pluralizeRu(count, one: "документ", few: "документа", many: "документов"))"
        case .audio:
            return count == 1 ? "🎵 Аудио" : "🎵 \(count) аудио"
        case .voice:
            return count == 1 ? "🎤 Голосовое" : "🎤 \(count) \(pluralizeRu(count, one: "голосовое", few: "голосовых", many: "голосовых"))"
        case .sticker:
            return count == 1 ? "🎭 Стикер" : "🎭 \(count) \(pluralizeRu(count, one: "стикер", few: "стикера", many: "стикеров"))"
        case .forwardedMessage:
            return count == 1 ? "↪️ Пересланное" : "↪️ \(count) пересланных"
        }
    }

    /// Простая RU-плюрализация по последним цифрам (1, 2-4, 5+).
    private static func pluralizeRu(_ count: Int, one: String, few: String, many: String) -> String {
        let mod10 = count % 10
        let mod100 = count % 100
        if mod10 == 1 && mod100 != 11 { return one }
        if (2...4).contains(mod10) && !(12...14).contains(mod100) { return few }
        return many
    }
}
