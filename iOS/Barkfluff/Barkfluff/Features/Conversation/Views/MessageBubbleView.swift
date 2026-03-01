//
//  MessageBubbleView.swift
//  Barkfluff
//
//  Пузырь сообщения в стиле iMessage (iOS версия)
//

import SwiftUI
import BFCore

/// Пузырь сообщения в стиле iMessage
struct MessageBubbleView: View {
    let message: Message
    let currentUserID: Int64
    let groupInfo: MessageGroupInfo
    let showSenderName: Bool

    /// Callback для повтора отправки (localID)
    var onRetry: ((String) -> Void)?
    /// Callback для удаления неотправленного (localID)
    var onDeleteFailed: ((String) -> Void)?
    /// Callback для нажатия на вложение
    var onAttachmentTap: ((MessageAttachment, [MessageAttachment]) -> Void)?

    /// Радиус скругления углов (как у облачка)
    static let bubbleCornerRadius: CGFloat = 18

    /// Является ли сообщение своим
    private var isOwn: Bool {
        message.senderID == currentUserID
    }

    /// Максимальная ширина пузырька (70% от контейнера)
    private let maxBubbleWidth: CGFloat = 300

    /// Сообщение в процессе отправки
    private var isSending: Bool {
        if case .sending = message.sendingState { return true }
        return false
    }

    /// Сообщение не удалось отправить
    private var isFailed: Bool {
        if case .failed = message.sendingState { return true }
        return false
    }

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
                    .opacity(isSending && !message.content.hasAttachments ? 0.7 : 1.0)
                    .animation(.easeInOut(duration: 0.2), value: message.sendingState)

                // Ошибка отправки
                if isFailed {
                    HStack(spacing: 4) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.caption2)
                            .foregroundStyle(.red)
                        Text("Ошибка отправки")
                            .font(.caption2)
                            .foregroundStyle(.red)
                    }
                    .padding(.horizontal, Theme.Spacing.sm)
                    .contextMenu {
                        if let localID = message.localID {
                            Button("Повторить") { onRetry?(localID) }
                            Button("Удалить", role: .destructive) { onDeleteFailed?(localID) }
                        }
                    }
                }

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
        .contextMenu {
            if isFailed, let localID = message.localID {
                Button("Повторить отправку") { onRetry?(localID) }
                Button("Удалить сообщение", role: .destructive) { onDeleteFailed?(localID) }
            }
        }
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
            // Для медиа используем минимальные отступы, скругления уже на картинках
            let padding = isMediaOnly ? CGFloat(2) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(2) : Theme.Spacing.sm

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
            .padding(.trailing, showTail ? 6 : 0)
        } else {
            // Входящее сообщение — серый пузырь (как в macOS)
            let showTail = groupInfo.isLastInGroup
            // Для медиа используем минимальные отступы, скругления уже на картинках
            let padding = isMediaOnly ? CGFloat(2) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(2) : Theme.Spacing.sm

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
                    .fill(Color(uiColor: .tertiarySystemFill))
            )
            .clipShape(MessageBubbleShape(tailSide: .left, showTail: showTail))
            .padding(.leading, showTail ? 6 : 0)
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
                    onAttachmentTap?(attachment, mediaAttachments)
                }
            )
            .overlay {
                // Круговой прогресс поверх медиа (Telegram-стиль)
                if let progress = message.uploadProgress, progress < 1.0 {
                    MediaUploadProgressView(progress: progress)
                        .clipShape(RoundedRectangle(cornerRadius: MessageBubbleView.bubbleCornerRadius, style: .continuous))
                }
            }
        }

        // Документы списком
        ForEach(documentAttachments) { attachment in
            if attachment.type == .document {
                DocumentAttachmentView(
                    attachment: attachment,
                    uploadProgress: message.uploadProgress
                )
            } else if attachment.type == .audio {
                AudioAttachmentView(
                    attachment: attachment,
                    uploadProgress: message.uploadProgress
                )
            }
        }
    }
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
        }
        .padding()
    }
    .frame(width: 400)
}
