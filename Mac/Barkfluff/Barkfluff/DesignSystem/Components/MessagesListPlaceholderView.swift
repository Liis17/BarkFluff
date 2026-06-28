//
//  MessagesListPlaceholderView.swift
//  Barkfluff
//
//  Skeleton-плейсхолдер для списка сообщений, пока загружается переписка.
//  Имитирует разбросанные пузыри слева/справа, оборачивается в .redacted —
//  iMessage-style загрузочное состояние без голой крутилки.
//

import SwiftUI

struct MessageBubblePlaceholderRow: View {
    let isFromCurrentUser: Bool
    let width: CGFloat

    var body: some View {
        HStack(spacing: 0) {
            if isFromCurrentUser { Spacer(minLength: Theme.Spacing.xl) }
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .fill(Color.gray.opacity(0.22))
                .frame(width: width, height: 32)
            if !isFromCurrentUser { Spacer(minLength: Theme.Spacing.xl) }
        }
        .padding(.horizontal, Theme.Spacing.md)
    }
}

struct MessagesListPlaceholderView: View {
    private static let bubbles: [(isMine: Bool, width: CGFloat)] = [
        (false, 200), (false, 140), (true, 180),
        (false, 240), (true, 110), (true, 160)
    ]

    var body: some View {
        VStack(spacing: 6) {
            Spacer()
            ForEach(Array(Self.bubbles.enumerated()), id: \.offset) { _, b in
                MessageBubblePlaceholderRow(isFromCurrentUser: b.isMine, width: b.width)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(.bottom, Theme.Spacing.lg)
        .redacted(reason: .placeholder)
        .accessibilityHidden(true)
    }
}

#Preview {
    MessagesListPlaceholderView()
        .frame(width: 600, height: 500)
}
