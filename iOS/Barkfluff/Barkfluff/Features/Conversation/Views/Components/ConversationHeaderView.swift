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
                isOnline: onlineStatus == .online,
                showOnlineIndicator: !chat.isGroupChat
            )

            // Информация
            VStack(alignment: .leading, spacing: 2) {
                Text(chat.title)
                    .font(.headline)
                    .lineLimit(1)

                if !chat.isGroupChat {
                    onlineStatusText
                }
            }

            Spacer()
        }
        .padding(.horizontal, Theme.Spacing.lg)
        .padding(.vertical, Theme.Spacing.sm)
        .background(.ultraThinMaterial)
    }

    @ViewBuilder
    private var onlineStatusText: some View {
        switch onlineStatus {
        case .online:
            Text("онлайн")
                .font(.caption)
                .foregroundStyle(.green)
        case .offline(let lastSeen):
            Text("был(а) \(formatLastSeen(lastSeen))")
                .font(.caption)
                .foregroundStyle(.secondary)
        case .unknown:
            EmptyView()
        }
    }

    private func formatLastSeen(_ date: Date) -> String {
        let calendar = Calendar.current
        let now = Date()

        if calendar.isDateInToday(date) {
            let formatter = DateFormatter()
            formatter.dateFormat = "HH:mm"
            return "в \(formatter.string(from: date))"
        } else if calendar.isDateInYesterday(date) {
            return "вчера"
        } else {
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "ru_RU")
            formatter.dateFormat = "d MMM"
            return formatter.string(from: date)
        }
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
