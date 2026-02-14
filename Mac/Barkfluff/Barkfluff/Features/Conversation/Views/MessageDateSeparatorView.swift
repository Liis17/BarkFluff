//
//  MessageDateSeparatorView.swift
//  Barkfluff
//
//  Разделитель дат между сообщениями
//

import SwiftUI

/// Разделитель дат между сообщениями
struct MessageDateSeparatorView: View {
    let date: Date

    var body: some View {
        HStack {
            Spacer()
            Text(formattedDate)
                .font(.caption)
                .fontWeight(.medium)
                .foregroundStyle(.secondary)
                .padding(.horizontal, Theme.Spacing.lg)
                .padding(.vertical, Theme.Spacing.xs)
                .background(
                    Capsule()
                        .fill(.ultraThinMaterial)
                )
            Spacer()
        }
        .padding(.vertical, Theme.Spacing.sm)
    }

    private var formattedDate: String {
        MessageGrouper.formatDateSeparator(date)
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
