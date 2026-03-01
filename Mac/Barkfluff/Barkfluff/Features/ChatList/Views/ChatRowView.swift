//
//  ChatRowView.swift
//  Barkfluff
//
//  Строка чата в списке
//

import SwiftUI
import BFCore

struct ChatRowView: View {
    let chat: Chat
    var onlineStatus: OnlineStatus = .unknown

    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            AvatarView(
                imageURL: chat.pictureURL,
                initials: chat.avatarInitials,
                size: 44,
                isOnline: onlineStatus.isOnline,
                showOnlineIndicator: !chat.isGroupChat
            )

            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text(chat.title)
                        .font(.headline)
                        .lineLimit(1)

                    Spacer()

                    if let date = chat.lastMessageDate {
                        Text(DateFormatterHelper.formatForChatList(date))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                HStack {
                    if let preview = chat.lastMessagePreview {
                        Text(preview)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    } else {
                        Text("Нет сообщений")
                            .font(.subheadline)
                            .foregroundStyle(.tertiary)
                    }

                    Spacer()

                    if chat.hasUnread {
                        UnreadBadgeView(count: chat.unreadCount)
                    }
                }
            }
        }
        .padding(.vertical, 4)
    }
}

struct ChatRowPlaceholderView: View {
    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            Circle()
                .fill(Color.gray.opacity(0.3))
                .frame(width: 44, height: 44)

            VStack(alignment: .leading, spacing: 4) {
                Rectangle()
                    .fill(Color.gray.opacity(0.3))
                    .frame(height: 16)
                    .frame(maxWidth: 100)

                Rectangle()
                    .fill(Color.gray.opacity(0.2))
                    .frame(height: 12)
                    .frame(maxWidth: 150)
            }
        }
        .padding(.vertical, 4)
        .redacted(reason: .placeholder)
    }
}
