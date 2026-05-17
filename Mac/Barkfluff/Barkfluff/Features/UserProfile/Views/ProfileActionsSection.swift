//
//  ProfileActionsSection.swift
//  Barkfluff
//
//  Секция действий в профиле
//

import SwiftUI
import BFCore

/// Действия профиля
struct ProfileActionsSection: View {
    let chat: Chat
    let viewModel: UserProfilePanelViewModel

    @Environment(AppCoordinator.self) private var coordinator

    var body: some View {
        VStack(spacing: Theme.Spacing.xs) {
            if !chat.isGroupChat {
                // DM: Отправить сообщение (фокус на поле ввода)
                ProfileActionButton(
                    titleKey: "user_profile.action.send_message",
                    icon: "message.fill",
                    color: .accentColor
                ) {
                    coordinator.showProfilePanel = false
                    // TODO: фокус на MessageInputView (через FocusState)
                }
            }
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.md)
    }
}

/// Кнопка действия в профиле
struct ProfileActionButton: View {
    let titleKey: LocalizedStringKey
    let icon: String
    var color: Color = .primary
    var isDestructive: Bool = false
    let action: () -> Void

    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: Theme.Spacing.sm) {
                Image(systemName: icon)
                    .font(.body)
                    .foregroundStyle(isDestructive ? .red : color)
                    .frame(width: 24, height: 24)

                Text(titleKey)
                    .font(.body)
                    .foregroundStyle(isDestructive ? .red : .primary)

                Spacer()
            }
            .padding(.horizontal, Theme.Spacing.sm)
            .padding(.vertical, Theme.Spacing.xs)
            .background {
                RoundedRectangle(cornerRadius: 8)
                    .fill(isHovering
                          ? (isDestructive ? Color.red.opacity(0.1) : Color.primary.opacity(0.05))
                          : Color.clear)
            }
        }
        .buttonStyle(.plain)
        .onHover { hovering in
            isHovering = hovering
        }
    }
}

#Preview {
    let container = DependencyContainer()
    let chat = Chat(id: "1", title: "Тест", isGroupChat: false, members: [])

    ProfileActionsSection(
        chat: chat,
        viewModel: UserProfilePanelViewModel(
            chat: chat,
            currentUserID: 0,
            userService: container.userService,
            chatService: container.chatService,
            sharedMediaService: container.sharedMediaService,
            fileService: container.fileService,
            onlineStatusService: container.onlineStatusService
        )
    )
    .padding()
    .frame(width: 300)
    .environment(AppCoordinator())
}
