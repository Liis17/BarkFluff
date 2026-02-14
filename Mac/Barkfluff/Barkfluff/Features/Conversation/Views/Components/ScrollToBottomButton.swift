//
//  ScrollToBottomButton.swift
//  Barkfluff
//
//  Кнопка прокрутки вниз
//

import SwiftUI

/// Кнопка прокрутки вниз
struct ScrollToBottomButton: View {
    let unreadCount: Int
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: Theme.Spacing.xs) {
                Image(systemName: "chevron.down")
                    .font(.caption)
                    .fontWeight(.semibold)

                if unreadCount > 0 {
                    Text("\(unreadCount)")
                        .font(.caption)
                        .fontWeight(.medium)
                }
            }
            .foregroundStyle(.white)
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.vertical, Theme.Spacing.sm)
            .background(
                Capsule()
                    .fill(.ultraThinMaterial)
                    .overlay {
                        Capsule()
                            .strokeBorder(.white.opacity(0.2), lineWidth: 0.5)
                    }
            )
            .shadow(color: .black.opacity(0.15), radius: 4, x: 0, y: 2)
        }
        .buttonStyle(.plain)
    }
}

#Preview {
    ZStack {
        Color.gray.opacity(0.2)
            .ignoresSafeArea()

        VStack {
            Spacer()
            ScrollToBottomButton(unreadCount: 5) {
                print("Scroll to bottom")
            }
            .padding(.bottom, 20)
        }
    }
}
