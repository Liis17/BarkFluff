//
//  ConversationHeaderView.swift
//  Barkfluff
//
//  Заголовок экрана переписки (iOS версия)
//

import SwiftUI
import BFCore

/// Заголовок экрана переписки
struct ConversationHeaderView: View {
    let chat: Chat
    let onlineStatus: OnlineStatus

    var body: some View {
        HStack(spacing: Theme.Spacing.sm) {
            // Аватар
            AvatarView(
                imageURL: chat.pictureURL,
                initials: chat.avatarInitials,
                size: 36,
                isOnline: onlineStatus.isOnline,
                showOnlineIndicator: !chat.isGroupChat
            )

            // Информация
            VStack(alignment: .leading, spacing: 2) {
                Text(chat.title)
                    .font(.headline)
                    .lineLimit(1)

                if !chat.isGroupChat {
                    OnlineStatusText(status: onlineStatus)
                }
            }

            Spacer()
        }
        .padding(.horizontal, Theme.Spacing.lg)
        .padding(.vertical, Theme.Spacing.sm)
        .background(.ultraThinMaterial)
    }
}

#Preview {
    VStack(spacing: 0) {
        ConversationHeaderView(
            chat: Chat(id: "1", title: "Иван Иванов", isGroupChat: false),
            onlineStatus: .online
        )

        ConversationHeaderView(
            chat: Chat(id: "2", title: "Петр Петров", isGroupChat: false),
            onlineStatus: .offline(lastSeen: Date().addingTimeInterval(-3600))
        )

        ConversationHeaderView(
            chat: Chat(id: "3", title: "Группа разработчиков", isGroupChat: true),
            onlineStatus: .unknown
        )
    }
}
