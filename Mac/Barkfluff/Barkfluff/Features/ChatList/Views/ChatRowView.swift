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
    let currentUserID: Int64
    let onlineStatusService: OnlineStatusServiceProtocol

    @State private var onlineStatus: OnlineStatus = .unknown
    @Environment(\.locale) private var locale

    private var otherUserID: Int64? {
        guard !chat.isGroupChat else { return nil }
        return chat.otherUserID(excluding: currentUserID)
    }

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
                        Text(DateFormatterHelper.formatForChatList(date, locale: locale))
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
                        Text("chat_list.row.no_messages")
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
        .task(id: otherUserID) {
            await observeOnlineStatus()
        }
    }

    /// Подписка на онлайн-статус собеседника.
    /// `.task(id: otherUserID)` гарантирует, что при reuse cell под другой чат
    /// предыдущая таска отменится и запустится новая для нового userID.
    private func observeOnlineStatus() async {
        guard let userID = otherUserID else {
            onlineStatus = .unknown
            return
        }

        // 1. Snapshot из кеша — мгновенный показ без сетевой задержки.
        onlineStatus = await onlineStatusService.currentStatus(for: userID)

        // 2. Track + подписка на per-user stream. При завершении таски
        //    (исчезновение row или смена userID) — untrack через cancellation handler.
        await onlineStatusService.track(userID)

        await withTaskCancellationHandler {
            let stream = await onlineStatusService.statusStream(for: userID)

            // Свежий fetch уже состоялся внутри track — синхронизируем UI с кешем
            // ещё раз на случай если он успел обновиться между snapshot'ом и track'ом.
            onlineStatus = await onlineStatusService.currentStatus(for: userID)

            for await newStatus in stream {
                onlineStatus = newStatus
            }
        } onCancel: {
            Task { await onlineStatusService.untrack(userID) }
        }
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
