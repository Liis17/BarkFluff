//
//  ScrollToBottomButton.swift
//  Barkfluff
//
//  Кнопка прокрутки вниз (iOS версия)
//

import SwiftUI

/// Кнопка прокрутки вниз с индикатором непрочитанных
struct ScrollToBottomButton: View {
    let unreadCount: Int
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack(alignment: .topTrailing) {
                Image(systemName: "chevron.down")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: 36, height: 36)
                    .glassEffect(.regular, in: .circle)

                // Бейдж непрочитанных
                if unreadCount > 0 {
                    Text(unreadCount > 99 ? "99+" : "\(unreadCount)")
                        .font(.caption2)
                        .fontWeight(.semibold)
                        .foregroundStyle(.white)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(.blue)
                        .clipShape(Capsule())
                        .offset(x: 8, y: -8)
                }
            }
        }
        .buttonStyle(.bfPressable)
        .accessibilityLabel(Text("conversation.input.scroll_to_bottom"))
        .accessibilityValue(unreadCount > 0 ? Text("\(unreadCount)") : Text(""))
    }
}

#Preview {
    ZStack {
        Color(uiColor: .systemGroupedBackground)
            .ignoresSafeArea()

        VStack(spacing: 20) {
            ScrollToBottomButton(unreadCount: 0, action: {})
            ScrollToBottomButton(unreadCount: 3, action: {})
            ScrollToBottomButton(unreadCount: 150, action: {})
        }
    }
}
