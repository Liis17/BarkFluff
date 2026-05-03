//
//  ForwardedMessageView.swift
//  Barkfluff
//
//  Карточка пересланного сообщения внутри пузыря: имя автора, текст и мини-вложения
//

import SwiftUI
import BFCore

/// Карточка пересланного сообщения. Отрисовывается внутри `MessageBubbleView`,
/// когда attachment имеет `type == .forwardedMessage` и заполненный `forwarded`.
struct ForwardedMessageView: View {
    let payload: ForwardedMessagePayload
    let isOwn: Bool

    private var stripeColor: Color {
        isOwn ? Color.white.opacity(0.7) : Color.accentColor
    }

    private var titleColor: Color {
        isOwn ? .white : .accentColor
    }

    private var textColor: Color {
        isOwn ? .white : .primary
    }

    private var secondaryColor: Color {
        isOwn ? Color.white.opacity(0.75) : .secondary
    }

    var body: some View {
        HStack(alignment: .top, spacing: Theme.Spacing.xs) {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(stripeColor)
                .frame(width: 3)

            VStack(alignment: .leading, spacing: 2) {
                Text(payload.authorName)
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundStyle(titleColor)

                if !payload.text.isEmpty {
                    Text(payload.text)
                        .font(.callout)
                        .foregroundStyle(textColor)
                        .lineLimit(4)
                        .textSelection(.enabled)
                }

                if !payload.attachments.isEmpty {
                    forwardedAttachmentsRow
                }
            }
        }
        .padding(.vertical, 2)
    }

    @ViewBuilder
    private var forwardedAttachmentsRow: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(payload.attachments) { att in
                HStack(spacing: 6) {
                    Image(systemName: att.type.systemImage)
                        .font(.caption)
                        .foregroundStyle(secondaryColor)

                    Text(forwardedAttachmentLabel(att))
                        .font(.caption)
                        .foregroundStyle(secondaryColor)
                        .lineLimit(1)
                }
            }
        }
    }

    private func forwardedAttachmentLabel(_ att: MessageAttachment) -> String {
        switch att.type {
        case .image: return "Фото"
        case .video: return "Видео"
        case .gif: return "GIF"
        case .audio: return "Аудио"
        case .voice: return "Голосовое"
        case .sticker: return "Стикер"
        case .document: return att.fileName.isEmpty ? "Документ" : att.fileName
        case .forwardedMessage: return "Пересланное"
        }
    }
}

#Preview {
    VStack(spacing: 12) {
        ForwardedMessageView(
            payload: ForwardedMessagePayload(
                authorName: "Иван Иванов",
                originalMessageID: 42,
                text: "Ребята, посмотрите этот документ — он важный.",
                attachments: [
                    MessageAttachment(id: 1, type: .image, fileID: "f1", fileName: "photo.jpg", fileSize: 1024),
                    MessageAttachment(id: 2, type: .document, fileID: "f2", fileName: "report.pdf", fileSize: 4096)
                ]
            ),
            isOwn: false
        )
        .padding(12)
        .background(Color(nsColor: .secondarySystemFill))
        .clipShape(RoundedRectangle(cornerRadius: 18))

        ForwardedMessageView(
            payload: ForwardedMessagePayload(
                authorName: "Мария",
                originalMessageID: 7,
                text: "Короткое сообщение",
                attachments: []
            ),
            isOwn: true
        )
        .padding(12)
        .background(Color(red: 0, green: 122/255, blue: 1))
        .clipShape(RoundedRectangle(cornerRadius: 18))
    }
    .padding()
    .frame(width: 400)
}
