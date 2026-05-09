//
//  ReplyPreviewView.swift
//  Barkfluff
//
//  Превью ответа над полем ввода (Telegram-style)
//

import SwiftUI
import BFCore

/// Превью сообщения, на которое сейчас формируется ответ.
/// Появляется над `MessageInputView`. Кнопка × очищает состояние ответа.
struct ReplyPreviewView: View {
    let authorName: String
    let snippet: String
    let onCancel: () -> Void

    var body: some View {
        HStack(alignment: .center, spacing: Theme.Spacing.sm) {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(Color.accentColor)
                .frame(width: 3)
                .frame(maxHeight: .infinity)

            Image(systemName: "arrowshape.turn.up.left.fill")
                .font(.callout)
                .foregroundStyle(Color.accentColor)

            VStack(alignment: .leading, spacing: 1) {
                Text(authorName)
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundStyle(Color.accentColor)
                    .lineLimit(1)

                Text(snippet)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            Spacer(minLength: 0)

            Button {
                onCancel()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("Отменить ответ")
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.sm)
        .frame(minHeight: 44)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: Theme.Radius.md))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.Radius.md)
                .stroke(Color.accentColor.opacity(0.15), lineWidth: 1)
        )
    }

    /// Сформировать однострочный snippet для превью.
    /// Если у сообщения есть текст — берём его (обрезка по 160 симв.),
    /// иначе — emoji-резюме первого вложения.
    static func makeSnippet(_ message: Message) -> String {
        let text = message.content.text
        if !text.isEmpty {
            return String(text.prefix(160))
        }
        if let first = message.content.attachments.first {
            return first.type.previewText(fileName: first.fileName)
        }
        return ""
    }
}

#Preview {
    VStack(spacing: 12) {
        ReplyPreviewView(
            authorName: "Иван Иванов",
            snippet: "Ребята, посмотрите этот документ — он важный.",
            onCancel: {}
        )
        ReplyPreviewView(
            authorName: "Мария",
            snippet: "\u{1F4F7} Фото",
            onCancel: {}
        )
    }
    .padding()
    .frame(width: 480)
}
