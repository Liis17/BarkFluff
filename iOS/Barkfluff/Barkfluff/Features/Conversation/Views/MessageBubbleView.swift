//
//  MessageBubbleView.swift
//  Barkfluff (iOS)
//
//  Пузырь сообщения в стиле iMessage с поддержкой контекстного меню,
//  пересланных сообщений, стикеров и отметок «изменено».
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
    /// Callback на «Ответить»
    var onReply: ((Message) -> Void)?
    /// Callback на «Переслать» — передаётся id сообщения
    var onForward: ((Int64) -> Void)?
    /// Callback на «Изменить»
    var onEdit: ((Message) -> Void)?
    /// Callback на «Удалить»
    var onDelete: ((Int64) -> Void)?
    /// Callback на «Копировать текст»
    var onCopyText: ((String) -> Void)?
    /// Callback на «Сохранить изображения»
    var onSaveImages: (([MessageAttachment]) -> Void)?
    /// Callback на «Скопировать изображение» (одно изображение)
    var onCopyImage: ((MessageAttachment) -> Void)?
    /// Callback на «Сохранить документы»
    var onSaveDocuments: (([MessageAttachment]) -> Void)?

    @Environment(DependencyContainer.self) private var container

    /// Радиус скругления по умолчанию (используется в превью без DI).
    static let defaultBubbleCornerRadius: CGFloat = 18
    static let bubbleCornerRadius: CGFloat = 18  // legacy alias

    /// Непрозрачный серый для входящих пузырей. Адаптируется к теме —
    /// близко к iMessage (светло-серый в light, тёмно-серый в dark).
    static let incomingBubbleColor: UIColor = UIColor { traitCollection in
        traitCollection.userInterfaceStyle == .dark
            ? UIColor(red: 44/255, green: 44/255, blue: 46/255, alpha: 1.0)
            : UIColor(red: 229/255, green: 229/255, blue: 234/255, alpha: 1.0)
    }

    /// Текущий радиус из настроек.
    private var bubbleCornerRadius: CGFloat {
        CGFloat(container.personalizationSettings.bubbleCornerRadius)
    }

    private var isOwn: Bool {
        message.senderID == currentUserID
    }

    /// Максимальная ширина пузырька
    private let maxBubbleWidth: CGFloat = 300

    private var isSending: Bool {
        if case .sending = message.sendingState { return true }
        return false
    }

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

    /// Сообщение состоит ровно из одного стикера и не содержит текста.
    private var isStickerOnly: Bool {
        !message.content.hasText
            && message.content.attachments.count == 1
            && message.content.attachments.first?.type == .sticker
    }

    var body: some View {
        HStack(alignment: .bottom, spacing: Theme.Spacing.xxs) {
            if isOwn {
                Spacer(minLength: 0)
            }

            VStack(alignment: isOwn ? .trailing : .leading, spacing: 2) {
                if !isOwn && showSenderName && groupInfo.isFirstInGroup {
                    Text(message.senderName ?? String(localized: "common.unknown_user"))
                        .font(.caption)
                        .fontWeight(.medium)
                        .foregroundStyle(.secondary)
                        .padding(.leading, Theme.Spacing.md)
                        .padding(.bottom, Theme.Spacing.xxs)
                }

                if isStickerOnly, let stickerAttachment = message.content.attachments.first {
                    StickerMessageView(attachment: stickerAttachment)
                        .frame(maxWidth: maxBubbleWidth, alignment: isOwn ? .trailing : .leading)
                        .opacity(isSending ? 0.7 : 1.0)
                        .animation(.easeInOut(duration: 0.2), value: message.sendingState)
                } else {
                    bubbleContent
                        .frame(maxWidth: maxBubbleWidth, alignment: isOwn ? .trailing : .leading)
                        .opacity(isSending && !message.content.hasAttachments ? 0.7 : 1.0)
                        .animation(.easeInOut(duration: 0.2), value: message.sendingState)
                }

                if isFailed {
                    HStack(spacing: 4) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.caption2)
                            .foregroundStyle(.red)
                        Text("conversation.errors.send_failed")
                            .font(.caption2)
                            .foregroundStyle(.red)
                    }
                    .padding(.horizontal, Theme.Spacing.sm)
                }

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
            contextMenuContent
        }
    }

    // MARK: - Context Menu

    @ViewBuilder
    private var contextMenuContent: some View {
        if isFailed, let localID = message.localID {
            Button("conversation.message.context_menu.retry_send") { onRetry?(localID) }
            Button("conversation.message.context_menu.delete_message", role: .destructive) { onDeleteFailed?(localID) }
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
                    Label("conversation.message.context_menu.edit", systemImage: "pencil")
                }
            }

            Button {
                onReply?(message)
            } label: {
                Label("conversation.message.context_menu.reply", systemImage: "arrowshape.turn.up.left")
            }
            Button {
                onForward?(message.forwardSourceID)
            } label: {
                Label("conversation.message.context_menu.forward", systemImage: "arrowshape.turn.up.right")
            }

            if !message.content.text.isEmpty {
                Button {
                    onCopyText?(message.content.text)
                } label: {
                    Label("conversation.message.context_menu.copy_text", systemImage: "doc.on.doc")
                }
            }

            if imageAttachments.count == 1, let one = imageAttachments.first {
                Button {
                    onCopyImage?(one)
                } label: {
                    Label("conversation.message.context_menu.copy_image", systemImage: "photo.on.rectangle")
                }
            }

            if !imageAttachments.isEmpty {
                let titleKey: LocalizedStringKey = imageAttachments.count == 1
                    ? "conversation.message.context_menu.save_image"
                    : "conversation.message.context_menu.save_images"
                Button {
                    onSaveImages?(imageAttachments)
                } label: {
                    Label(titleKey, systemImage: "square.and.arrow.down")
                }
            }

            if !documentAttachments.isEmpty {
                Button {
                    onSaveDocuments?(documentAttachments)
                } label: {
                    Label("conversation.message.context_menu.save_document", systemImage: "arrow.down.doc")
                }
            }

            if isOwn {
                Divider()
                Button(role: .destructive) {
                    onDelete?(message.id)
                } label: {
                    Label("conversation.message.context_menu.delete", systemImage: "trash")
                }
            }
        }
    }

    @ViewBuilder
    private var bubbleContent: some View {
        if message.isSystem {
            Text(message.content.text)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, Theme.Spacing.lg)
                .padding(.vertical, Theme.Spacing.md)
        } else if isOwn {
            let showTail = groupInfo.isLastInGroup
            let padding = isMediaOnly ? CGFloat(2) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(2) : Theme.Spacing.sm

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
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
            let showTail = groupInfo.isLastInGroup
            let padding = isMediaOnly ? CGFloat(2) : Theme.Spacing.md
            let verticalPadding = isMediaOnly ? CGFloat(2) : Theme.Spacing.sm

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
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
                    .fill(Color(uiColor: Self.incomingBubbleColor))
            )
            .clipShape(MessageBubbleShape(tailSide: .left, showTail: showTail, cornerRadius: bubbleCornerRadius))
            .padding(.leading, showTail ? 6 : 0)
        }
    }

    @ViewBuilder
    private var attachmentsView: some View {
        let forwardedAttachments = message.content.attachments.filter {
            $0.type == .forwardedMessage && $0.forwarded != nil
        }
        let mediaAttachments = message.content.attachments.filter {
            $0.type == .image || $0.type == .video || $0.type == .gif
        }
        let stickerAttachments = message.content.attachments.filter {
            $0.type == .sticker
        }
        let documentAttachments = message.content.attachments.filter {
            $0.type == .document || $0.type == .audio
        }

        ForEach(forwardedAttachments) { attachment in
            if let payload = attachment.forwarded {
                ForwardedMessageView(payload: payload, isOwn: isOwn)
            }
        }

        ForEach(stickerAttachments) { attachment in
            StickerMessageView(attachment: attachment, size: 140)
        }

        if !mediaAttachments.isEmpty {
            AttachmentGridView(
                attachments: mediaAttachments,
                isOwn: isOwn,
                onTap: { attachment in
                    onAttachmentTap?(attachment, mediaAttachments)
                }
            )
            .overlay {
                if let progress = message.uploadProgress, progress < 1.0 {
                    MediaUploadProgressView(progress: progress)
                        .clipShape(RoundedRectangle(cornerRadius: bubbleCornerRadius, style: .continuous))
                }
            }
        }

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
