//
//  MessageBubbleView.swift
//  Barkfluff
//
//  Пузырь сообщения в стиле iMessage
//

import SwiftUI
import BFCore

/// Пузырь сообщения в стиле iMessage
struct MessageBubbleView: View {
    let message: Message
    let currentUserID: Int64
    let groupInfo: MessageGroupInfo
    let showSenderName: Bool

    /// Радиус скругления углов (как у облачка)
    static let bubbleCornerRadius: CGFloat = 18

    /// Является ли сообщение своим
    private var isOwn: Bool {
        message.senderID == currentUserID
    }

    /// Максимальная ширина пузырька (70% от контейнера)
    private let maxBubbleWidth: CGFloat = 400

    /// Только медиа вложения без текста
    private var isMediaOnly: Bool {
        !message.content.hasText && message.content.hasAttachments &&
        message.content.attachments.allSatisfy {
            $0.type == .image || $0.type == .video || $0.type == .gif
        }
    }

    var body: some View {
        HStack(alignment: .bottom, spacing: Theme.Spacing.xxs) {
            if isOwn {
                Spacer(minLength: 0)
            }

            VStack(alignment: isOwn ? .trailing : .leading, spacing: 2) {
                // Имя отправителя (только для групповых чатов, первое в группе)
                if !isOwn && showSenderName && groupInfo.isFirstInGroup {
                    Text(message.senderName ?? "Неизвестный")
                        .font(.caption)
                        .fontWeight(.medium)
                        .foregroundStyle(.secondary)
                        .padding(.leading, Theme.Spacing.md)
                        .padding(.bottom, Theme.Spacing.xxs)
                }

                // Контент сообщения
                bubbleContent
                    .frame(maxWidth: maxBubbleWidth, alignment: isOwn ? .trailing : .leading)

                // Время и статус
                if groupInfo.showTime || groupInfo.isLastInGroup {
                    MessageTimeView(
                        message: message,
                        currentUserID: currentUserID,
                        isOwn: isOwn
                    )
                    .padding(.horizontal, Theme.Spacing.sm)
                }
            }

            if !isOwn {
                Spacer(minLength: 0)
            }
        }
        .padding(.vertical, 1)
    }

    @ViewBuilder
    private var bubbleContent: some View {
        // Текст сообщения
        if message.isSystem {
            // Системное сообщение
            Text(message.content.text)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, Theme.Spacing.lg)
                .padding(.vertical, Theme.Spacing.md)
        } else if isOwn {
            // Исходящее сообщение — синий пузырь
            let showTail = groupInfo.isLastInGroup
            let padding = isMediaOnly ? CGFloat(4) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(4) : Theme.Spacing.sm

            VStack(alignment: .trailing, spacing: Theme.Spacing.xs) {
                if message.content.hasText {
                    Text(message.content.text)
                        .font(.body)
                        .foregroundStyle(.white)
                        .textSelection(.enabled)
                }

                // Вложения
                if message.content.hasAttachments {
                    attachmentsView
                }
            }
            .padding(.horizontal, padding)
            .padding(.vertical, verticalPadding)
            .background(
                MessageBubbleShape(tailSide: .right, showTail: showTail)
                    .fill(Color(red: 0, green: 122/255, blue: 1))
            )
            .clipShape(MessageBubbleShape(tailSide: .right, showTail: showTail))
            .padding(.trailing, showTail ? 8 : 0)
            .padding(.bottom, showTail ? 4 : 0)
        } else {
            // Входящее сообщение — серый пузырь
            let showTail = groupInfo.isLastInGroup
            let padding = isMediaOnly ? CGFloat(4) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(4) : Theme.Spacing.sm

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                if message.content.hasText {
                    Text(message.content.text)
                        .font(.body)
                        .foregroundStyle(.primary)
                        .textSelection(.enabled)
                }

                // Вложения
                if message.content.hasAttachments {
                    attachmentsView
                }
            }
            .padding(.horizontal, padding)
            .padding(.vertical, verticalPadding)
            .background(
                MessageBubbleShape(tailSide: .left, showTail: showTail)
                    .fill(Color(nsColor: .secondarySystemFill))
            )
            .clipShape(MessageBubbleShape(tailSide: .left, showTail: showTail))
            .padding(.leading, showTail ? 8 : 0)
            .padding(.bottom, showTail ? 4 : 0)
        }
    }

    @ViewBuilder
    private var attachmentsView: some View {
        // Разделяем вложения на медиа и документы
        let mediaAttachments = message.content.attachments.filter {
            $0.type == .image || $0.type == .video || $0.type == .gif
        }
        let documentAttachments = message.content.attachments.filter {
            $0.type == .document || $0.type == .audio
        }

        // Медиа вложения в сетке
        if !mediaAttachments.isEmpty {
            AttachmentGridView(
                attachments: mediaAttachments,
                isOwn: isOwn,
                onTap: { attachment in
                    // Обработка tap будет в ConversationView через callback
                    NotificationCenter.default.post(
                        name: .attachmentTapped,
                        object: nil,
                        userInfo: ["attachment": attachment, "allAttachments": mediaAttachments]
                    )
                }
            )
        }

        // Документы списком
        ForEach(documentAttachments) { attachment in
            if attachment.type == .document {
                DocumentAttachmentView(
                    attachment: attachment,
                    onTap: {
                        NotificationCenter.default.post(
                            name: .attachmentTapped,
                            object: nil,
                            userInfo: ["attachment": attachment, "allAttachments": documentAttachments]
                        )
                    }
                )
            } else if attachment.type == .audio {
                AudioAttachmentView(
                    attachment: attachment,
                    onTap: {
                        NotificationCenter.default.post(
                            name: .attachmentTapped,
                            object: nil,
                            userInfo: ["attachment": attachment, "allAttachments": documentAttachments]
                        )
                    }
                )
            }
        }
    }
}

// MARK: - Legacy Support (для обратной совместимости)

/// Превью сообщения (устаревшее, оставлено для совместимости)
struct MessagePreview: Identifiable {
    let id: Int64
    let text: String
    let time: Date
    let isOwn: Bool
    let isRead: Bool
    let senderName: String
    let senderID: Int64
}

#Preview {
    let currentUserID: Int64 = 1

    ScrollView {
        VStack(spacing: Theme.Spacing.sm) {
            // Разделитель даты
            MessageDateSeparatorView(date: Date())

            // Входящее сообщение
            MessageBubbleView(
                message: Message(
                    id: 1,
                    chatID: "test",
                    senderID: 2,
                    senderName: "Иван Иванов",
                    content: MessageContent(text: "Привет! Как дела? Что нового?"),
                    sentAt: Date().addingTimeInterval(-3600),
                    readBy: [1, 2]
                ),
                currentUserID: currentUserID,
                groupInfo: MessageGroupInfo(isFirstInGroup: true, isLastInGroup: true, showTime: true),
                showSenderName: true
            )

            // Исходящее сообщение
            MessageBubbleView(
                message: Message(
                    id: 2,
                    chatID: "test",
                    senderID: currentUserID,
                    senderName: "Я",
                    content: MessageContent(text: "Привет! Всё отлично, спасибо!"),
                    sentAt: Date().addingTimeInterval(-3500),
                    readBy: [1, 2]
                ),
                currentUserID: currentUserID,
                groupInfo: MessageGroupInfo(isFirstInGroup: true, isLastInGroup: true, showTime: true),
                showSenderName: true
            )

            // Группа сообщений от одного отправителя
            MessageBubbleView(
                message: Message(
                    id: 3,
                    chatID: "test",
                    senderID: 2,
                    senderName: "Иван Иванов",
                    content: MessageContent(text: "Первое сообщение в группе"),
                    sentAt: Date().addingTimeInterval(-3000),
                    readBy: [2]
                ),
                currentUserID: currentUserID,
                groupInfo: MessageGroupInfo(isFirstInGroup: true, isLastInGroup: false, showTime: false),
                showSenderName: true
            )

            MessageBubbleView(
                message: Message(
                    id: 4,
                    chatID: "test",
                    senderID: 2,
                    senderName: "Иван Иванов",
                    content: MessageContent(text: "Второе сообщение в группе"),
                    sentAt: Date().addingTimeInterval(-2950),
                    readBy: [2]
                ),
                currentUserID: currentUserID,
                groupInfo: MessageGroupInfo(isFirstInGroup: false, isLastInGroup: true, showTime: true),
                showSenderName: true
            )

            // Длинное сообщение
            MessageBubbleView(
                message: Message(
                    id: 5,
                    chatID: "test",
                    senderID: currentUserID,
                    senderName: "Я",
                    content: MessageContent(text: "Это очень длинное сообщение, которое должно продемонстрировать, как выглядит пузырь с большим количеством текста. В iMessage такие сообщения тоже выглядят хорошо и читабельно."),
                    sentAt: Date().addingTimeInterval(-2000),
                    readBy: [1]
                ),
                currentUserID: currentUserID,
                groupInfo: MessageGroupInfo(isFirstInGroup: true, isLastInGroup: true, showTime: true),
                showSenderName: false
            )
        }
        .padding()
    }
    .frame(width: 400)
}
