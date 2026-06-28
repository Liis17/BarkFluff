//
//  ForwardedMessageView.swift
//  Barkfluff (iOS)
//
//  Карточка пересланного сообщения внутри пузыря: имя автора, текст и мини-вложения
//

import SwiftUI
import BFCore

/// Карточка пересланного сообщения.
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
        case .image: return String(localized: "conversation.attach.preview.photo")
        case .video: return String(localized: "conversation.attach.preview.video")
        case .gif: return String(localized: "conversation.attach.preview.gif")
        case .audio: return String(localized: "conversation.attach.preview.audio")
        case .voice: return String(localized: "conversation.attach.preview.voice")
        case .sticker: return String(localized: "conversation.attach.preview.sticker")
        case .document: return att.fileName.isEmpty ? String(localized: "conversation.attach.preview.document") : att.fileName
        case .forwardedMessage: return String(localized: "conversation.attach.preview.forwarded")
        }
    }
}
