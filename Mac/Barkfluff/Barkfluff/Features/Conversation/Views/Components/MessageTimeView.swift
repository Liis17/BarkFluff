//
//  MessageTimeView.swift
//  Barkfluff
//
//  Время и статус прочтения сообщения
//

import SwiftUI
import BFCore

/// Время и статус прочтения сообщения
struct MessageTimeView: View {
    let message: Message
    let currentUserID: Int64
    let isOwn: Bool

    var body: some View {
        HStack(spacing: 4) {
            // Время отправки
            Text(message.sentAt, style: .time)
                .font(.caption2)
                .foregroundStyle(.tertiary)

            // Статус прочтения для своих сообщений
            if isOwn {
                readStatusIcon
            }
        }
    }

    @ViewBuilder
    private var readStatusIcon: some View {
        // Проверяем, прочитано ли сообщение всеми участниками чата
        // (исключая отправителя из списка прочитавших)
        let otherReadBy = message.readBy.filter { $0 != message.senderID }
        let isRead = !otherReadBy.isEmpty

        if isRead {
            Image(systemName: "checkmark.circle.fill")
                .font(.caption2)
                .foregroundStyle(.green)
        } else {
            Image(systemName: "checkmark")
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
    }
}

#Preview {
    VStack(alignment: .trailing, spacing: 16) {
        // Непрочитанное сообщение
        MessageTimeView(
            message: Message(
                id: 1,
                chatID: "test",
                senderID: 1,
                content: MessageContent(text: "Test"),
                sentAt: Date(),
                readBy: [1]
            ),
            currentUserID: 1,
            isOwn: true
        )

        // Прочитанное сообщение
        MessageTimeView(
            message: Message(
                id: 2,
                chatID: "test",
                senderID: 1,
                content: MessageContent(text: "Test"),
                sentAt: Date(),
                readBy: [1, 2, 3]
            ),
            currentUserID: 1,
            isOwn: true
        )

        // Входящее сообщение
        MessageTimeView(
            message: Message(
                id: 3,
                chatID: "test",
                senderID: 2,
                content: MessageContent(text: "Test"),
                sentAt: Date(),
                readBy: [1, 2]
            ),
            currentUserID: 1,
            isOwn: false
        )
    }
    .padding()
}
