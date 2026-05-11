//
//  EditPreviewView.swift
//  Barkfluff
//
//  Превью редактируемого сообщения над полем ввода (Telegram-style)
//

import SwiftUI
import BFCore

/// Превью сообщения, которое сейчас редактируется.
/// Появляется над `MessageInputView`. Кнопка × сбрасывает режим редактирования.
struct EditPreviewView: View {
    let snippet: String
    let onCancel: () -> Void

    var body: some View {
        HStack(alignment: .center, spacing: Theme.Spacing.sm) {
            // Полоска-акцент. Без maxHeight: .infinity, иначе HStack тянется
            // на всю доступную вертикаль внутри VStack composer'а.
            RoundedRectangle(cornerRadius: 1.5)
                .fill(Color.accentColor)
                .frame(width: 3, height: 32)

            Image(systemName: "pencil")
                .font(.callout)
                .foregroundStyle(Color.accentColor)

            VStack(alignment: .leading, spacing: 1) {
                Text("Редактирование сообщения")
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
            .help("Отменить редактирование")
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.sm)
        .fixedSize(horizontal: false, vertical: true)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: Theme.Radius.md))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.Radius.md)
                .stroke(Color.accentColor.opacity(0.15), lineWidth: 1)
        )
    }
}

#Preview {
    VStack(spacing: 12) {
        EditPreviewView(
            snippet: "Старый текст сообщения, который сейчас редактируется.",
            onCancel: {}
        )
    }
    .padding()
    .frame(width: 480)
}
