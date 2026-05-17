//
//  MessageDateSeparatorView.swift
//  Barkfluff
//
//  Разделитель дат в списке сообщений (iOS версия)
//

import SwiftUI

/// Разделитель дат в списке сообщений
struct MessageDateSeparatorView: View {
    let date: Date

    @Environment(\.locale) private var locale

    var body: some View {
        HStack {
            Spacer()
            Text(MessageGrouper.formatDateSeparator(date, locale: locale))
                .font(.caption)
                .fontWeight(.medium)
                .foregroundStyle(.secondary)
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.xs)
                .background(.ultraThinMaterial)
                .clipShape(Capsule())
            Spacer()
        }
        .padding(.vertical, Theme.Spacing.sm)
    }
}

#Preview {
    VStack(spacing: 20) {
        MessageDateSeparatorView(date: Date())
        MessageDateSeparatorView(date: Date().addingTimeInterval(-86400))
        MessageDateSeparatorView(date: Date().addingTimeInterval(-86400 * 3))
    }
    .padding()
}
