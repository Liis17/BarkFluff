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

    /// Callback для повтора отправки (localID)
    var onRetry: ((String) -> Void)?
    /// Callback для удаления неотправленного (localID)
    var onDeleteFailed: ((String) -> Void)?
    /// Callback на «Ответить» (форвард в текущий чат)
    var onReply: ((Message) -> Void)?
    /// Callback на «Переслать» (открыть выбор чата) — передаётся id сообщения
    var onForward: ((Int64) -> Void)?
    /// Callback на «Изменить» — переводит ConversationView в режим редактирования
    var onEdit: ((Message) -> Void)?
    /// Callback на «Удалить» — открывает confirmationDialog в ConversationView
    var onDelete: ((Int64) -> Void)?
    /// Callback на «Копировать текст»
    var onCopyText: ((String) -> Void)?
    /// Callback на «Сохранить изображение(я)»
    var onSaveImages: (([MessageAttachment]) -> Void)?
    /// Callback на «Скопировать изображение» (только для одного изображения)
    var onCopyImage: ((MessageAttachment) -> Void)?
    /// Callback на «Сохранить в загрузки» — для документов и аудио
    var onSaveDocuments: (([MessageAttachment]) -> Void)?

    @Environment(DependencyContainer.self) private var container

    /// Радиус скругления по умолчанию — используется в превью без DI и
    /// как fallback. Реальный радиус берётся из PersonalizationSettings.
    static let defaultBubbleCornerRadius: CGFloat = 18

    /// Непрозрачный серый для входящих пузырей. Адаптируется к теме —
    /// близко к iMessage (светло-серый в light, тёмно-серый в dark).
    static let incomingBubbleColor: NSColor = NSColor(name: nil) { appearance in
        let isDark = appearance.bestMatch(from: [.darkAqua, .vibrantDark]) != nil
        return isDark
            ? NSColor(red: 44/255, green: 44/255, blue: 46/255, alpha: 1.0)
            : NSColor(red: 229/255, green: 229/255, blue: 234/255, alpha: 1.0)
    }

    /// Текущий радиус из настроек (точка чтения для всего пузырька и его медиа).
    private var bubbleCornerRadius: CGFloat {
        CGFloat(container.personalizationSettings.bubbleCornerRadius)
    }

    /// Является ли сообщение своим
    private var isOwn: Bool {
        message.senderID == currentUserID
    }

    /// Максимальная ширина пузырька (70% от контейнера)
    private let maxBubbleWidth: CGFloat = 400

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

    /// Только медиа вложения без текста (forwarded сюда не попадает — у него своя «карточка»)
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
            } else if !message.isSystem && message.id > 0 {
                let attachments = message.content.attachments
                let imageAttachments = attachments.filter { $0.type == .image }
                let documentAttachments = attachments.filter {
                    $0.type == .document || $0.type == .audio || $0.type == .voice
                }
                let nonForwardedAttachments = attachments.filter { $0.type != .forwardedMessage }
                let canEdit = isOwn && (message.content.hasText || !nonForwardedAttachments.isEmpty)

                if canEdit {
                    Button {
                        onEdit?(message)
                    } label: {
                        Label("Изменить", systemImage: "pencil")
                    }
                }

                Button {
                    onReply?(message)
                } label: {
                    Label("Ответить", systemImage: "arrowshape.turn.up.left")
                }
                Button {
                    // Если сообщение само пересланное — пересылаем оригинал, не wrapper
                    onForward?(message.forwardSourceID)
                } label: {
                    Label("Переслать", systemImage: "arrowshape.turn.up.right")
                }

                if !message.content.text.isEmpty {
                    Button {
                        onCopyText?(message.content.text)
                    } label: {
                        Label("Копировать текст", systemImage: "doc.on.doc")
                    }
                }

                if imageAttachments.count == 1, let one = imageAttachments.first {
                    Button {
                        onCopyImage?(one)
                    } label: {
                        Label("Скопировать изображение", systemImage: "photo.on.rectangle")
                    }
                }

                if !imageAttachments.isEmpty {
                    let title = imageAttachments.count == 1 ? "Сохранить изображение" : "Сохранить изображения"
                    Button {
                        onSaveImages?(imageAttachments)
                    } label: {
                        Label(title, systemImage: "square.and.arrow.down")
                    }
                }

                if !documentAttachments.isEmpty {
                    Button {
                        onSaveDocuments?(documentAttachments)
                    } label: {
                        Label("Сохранить в загрузки", systemImage: "arrow.down.doc")
                    }
                }

                if isOwn {
                    Divider()
                    Button(role: .destructive) {
                        onDelete?(message.id)
                    } label: {
                        Label("Удалить", systemImage: "trash")
                    }
                }
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
            let padding = isMediaOnly ? CGFloat(4) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(4) : Theme.Spacing.sm

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                // Вложения сверху
                if message.content.hasAttachments {
                    attachmentsView
                }

                if message.content.hasText {
                    Text(message.content.text)
                        .font(.body)
                        .foregroundStyle(.white)
                        .textSelection(.enabled)
                        .multilineTextAlignment(.leading)
                }
            }
            .padding(.horizontal, padding)
            .padding(.vertical, verticalPadding)
            .background(
                MessageBubbleShape(tailSide: .right, showTail: showTail, cornerRadius: bubbleCornerRadius)
                    .fill(Color(red: 0, green: 122/255, blue: 1))
            )
            .clipShape(MessageBubbleShape(tailSide: .right, showTail: showTail, cornerRadius: bubbleCornerRadius))
            .padding(.trailing, showTail ? 6 : 0)
        } else {
            // Входящее сообщение — серый пузырь
            let showTail = groupInfo.isLastInGroup
            let padding = isMediaOnly ? CGFloat(4) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(4) : Theme.Spacing.sm

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                // Вложения сверху
                if message.content.hasAttachments {
                    attachmentsView
                }

                if message.content.hasText {
                    Text(message.content.text)
                        .font(.body)
                        .foregroundStyle(.primary)
                        .textSelection(.enabled)
                        .multilineTextAlignment(.leading)
                }
            }
            .padding(.horizontal, padding)
            .padding(.vertical, verticalPadding)
            .background(
                MessageBubbleShape(tailSide: .left, showTail: showTail, cornerRadius: bubbleCornerRadius)
                    .fill(Color(nsColor: Self.incomingBubbleColor))
            )
            .clipShape(MessageBubbleShape(tailSide: .left, showTail: showTail, cornerRadius: bubbleCornerRadius))
            .padding(.leading, showTail ? 6 : 0)
        }
    }

    @ViewBuilder
    private var attachmentsView: some View {
        // Forwarded-сообщения отрисовываются отдельной карточкой со снимком оригинала
        let forwardedAttachments = message.content.attachments.filter {
            $0.type == .forwardedMessage && $0.forwarded != nil
        }
        // Разделяем остальные вложения на медиа и документы
        let mediaAttachments = message.content.attachments.filter {
            $0.type == .image || $0.type == .video || $0.type == .gif
        }
        let documentAttachments = message.content.attachments.filter {
            $0.type == .document || $0.type == .audio
        }

        // Карточки пересланных сообщений
        ForEach(forwardedAttachments) { attachment in
            if let payload = attachment.forwarded {
                ForwardedMessageView(payload: payload, isOwn: isOwn)
            }
        }

        // Медиа вложения в сетке
        if !mediaAttachments.isEmpty {
            AttachmentGridView(
                attachments: mediaAttachments,
                isOwn: isOwn,
                onTap: { attachment in
                    // Не открываем превью для pending-вложений
                    guard attachment.id > 0 else { return }
                    var userInfo: [String: Any] = [
                        "attachment": attachment,
                        "allAttachments": mediaAttachments
                    ]
                    if message.content.hasText {
                        userInfo["messageText"] = message.content.text
                    }
                    NotificationCenter.default.post(
                        name: .attachmentTapped,
                        object: nil,
                        userInfo: userInfo
                    )
                }
            )
            .overlay {
                // Круговой прогресс поверх медиа (Telegram-стиль)
                if let progress = message.uploadProgress, progress < 1.0 {
                    MediaUploadProgressView(progress: progress)
                        .clipShape(RoundedRectangle(cornerRadius: bubbleCornerRadius, style: .continuous))
                }
            }
        }

        // Документы списком
        ForEach(documentAttachments) { attachment in
            if attachment.type == .document {
                DocumentAttachmentView(
                    attachment: attachment,
                    onTap: {
                        NotificationCenter.default.post(
                            name: .documentDownloadRequested,
                            object: nil,
                            userInfo: ["attachment": attachment]
                        )
                    },
                    uploadProgress: message.uploadProgress
                )
            } else if attachment.type == .audio {
                AudioAttachmentView(
                    attachment: attachment,
                    onTap: {
                        NotificationCenter.default.post(
                            name: .documentDownloadRequested,
                            object: nil,
                            userInfo: ["attachment": attachment]
                        )
                    },
                    uploadProgress: message.uploadProgress
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
