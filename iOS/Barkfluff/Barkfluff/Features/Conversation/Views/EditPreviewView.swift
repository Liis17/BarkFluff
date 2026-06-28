//
//  EditPreviewView.swift
//  Barkfluff (iOS)
//
//  Превью редактируемого сообщения над полем ввода (Telegram-style)
//

import SwiftUI
import BFCore

/// Превью сообщения, которое сейчас редактируется.
struct EditPreviewView: View {
    let snippet: String
    let onCancel: () -> Void

    var body: some View {
        HStack(alignment: .center, spacing: Theme.Spacing.sm) {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(Color.accentColor)
                .frame(width: 3)
                .frame(maxHeight: .infinity)

            Image(systemName: "pencil")
                .font(.callout)
                .foregroundStyle(Color.accentColor)

            VStack(alignment: .leading, spacing: 1) {
                Text("conversation.edit.title")
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
            .accessibilityLabel(Text("conversation.edit.cancel_a11y"))
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.sm)
        .frame(minHeight: 44)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: Theme.Radius.md))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.Radius.md)
                .stroke(Color.accentColor.opacity(0.15), lineWidth: 1)
        )
        .fixedSize(horizontal: false, vertical: true)
    }
}
