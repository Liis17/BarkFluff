//
//  MessageTimeView.swift
//  Barkfluff
//
//  Время и статус прочтения сообщения (iOS версия)
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
            // Иконка карандаша — сообщение было отредактировано
            if message.isEdited {
                Image(systemName: "pencil")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
                    .accessibilityLabel("Сообщение отредактировано")
            }

            // Время отправки
            Text(message.sentAt, style: .time)
                .font(.caption2)
                .foregroundStyle(.tertiary)

            // Статус отправки / прочтения для своих сообщений
            if isOwn {
                sendingStatusIcon
            }
        }
    }

    @ViewBuilder
    private var sendingStatusIcon: some View {
        switch message.sendingState {
        case .sending:
            ProgressView()
                .controlSize(.mini)
                .scaleEffect(0.5)
                .frame(width: 10, height: 10)
        case .failed:
            Image(systemName: "exclamationmark.circle.fill")
                .font(.caption2)
                .foregroundStyle(.red)
        case .sent:
            readStatusIcon
        }
    }

    @ViewBuilder
    private var readStatusIcon: some View {
        let otherReadBy = message.readBy.filter { $0 != message.senderID }
        let isRead = !otherReadBy.isEmpty

        if isRead {
            // Две галочки — прочитано
            HStack(spacing: -4) {
                Image(systemName: "checkmark")
                    .font(.system(size: 9, weight: .semibold))
                Image(systemName: "checkmark")
                    .font(.system(size: 9, weight: .semibold))
            }
            .foregroundStyle(.blue)
            .frame(width: 14, height: 10)
        } else {
            // Одна галочка — отправлено
            Image(systemName: "checkmark")
                .font(.system(size: 9, weight: .semibold))
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
