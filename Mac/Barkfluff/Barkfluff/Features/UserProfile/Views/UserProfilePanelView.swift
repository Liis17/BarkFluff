//
//  UserProfilePanelView.swift
//  Barkfluff
//
//  Панель профиля пользователя/группы (inspector)
//

import SwiftUI
import BFCore

/// Главный контейнер панели профиля
struct UserProfilePanelView: View {
    let chat: Chat

    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: UserProfilePanelViewModel?

    var body: some View {
        Group {
            if let viewModel {
                ScrollView(.vertical, showsIndicators: true) {
                    VStack(spacing: 0) {
                        // 1. Шапка: аватар, имя, юзернейм
                        ProfileHeaderSection(viewModel: viewModel)

                        Divider()
                            .padding(.horizontal, Theme.Spacing.md)

                        // 2. Информация: био, баджи, дата, хранилище
                        ProfileInfoSection(viewModel: viewModel)

                        Divider()
                            .padding(.horizontal, Theme.Spacing.md)

                        // 3. Действия
                        ProfileActionsSection(chat: chat, viewModel: viewModel)

                        // 4. Участники (только для групповых чатов)
                        if chat.isGroupChat {
                            Divider()
                                .padding(.horizontal, Theme.Spacing.md)

                            GroupMembersSection(viewModel: viewModel)
                        }

                        Divider()
                            .padding(.horizontal, Theme.Spacing.md)

                        // 5. Shared Media
                        SharedMediaSection(viewModel: viewModel)
                    }
                    .padding(.bottom, Theme.Spacing.xl)
                }
            } else {
                UserProfilePanelPlaceholderView()
            }
        }
        .background(.ultraThinMaterial)
        .toolbar {
            ToolbarItem(placement: .cancellationAction) {
                Button {
                    coordinator.closeProfilePanel()
                } label: {
                    Image(systemName: "xmark")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: 24, height: 24)
                }
                .accessibilityLabel(Text("user_profile.close"))
            }
        }
        .task {
            let vm = UserProfilePanelViewModel(
                chat: chat,
                currentUserID: container.currentUserID,
                userService: container.userService,
                chatService: container.chatService,
                sharedMediaService: container.sharedMediaService,
                fileService: container.fileService,
                onlineStatusService: container.onlineStatusService
            )
            viewModel = vm
            await vm.loadProfile()
            await vm.loadSharedMedia()
        }
        .onDisappear {
            viewModel?.stopListeningForOnlineStatus()
        }
        .onChange(of: chat.id) {
            viewModel?.stopListeningForOnlineStatus()
            Task {
                let vm = UserProfilePanelViewModel(
                    chat: chat,
                    currentUserID: container.currentUserID,
                    userService: container.userService,
                    chatService: container.chatService,
                    sharedMediaService: container.sharedMediaService,
                    fileService: container.fileService,
                    onlineStatusService: container.onlineStatusService
                )
                viewModel = vm
                await vm.loadProfile()
                await vm.loadSharedMedia()
            }
        }
    }
}

// MARK: - Placeholder

/// Skeleton-плейсхолдер: постер 3:1 + аватар-кружок + 2 строки текста +
/// блок инфо из 3 row'ов. Срабатывает, пока `viewModel == nil` (короткий момент
/// до `task`/`loadProfile`).
private struct UserProfilePanelPlaceholderView: View {
    var body: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(spacing: 0) {
                ZStack(alignment: .bottomLeading) {
                    Rectangle()
                        .fill(Color.gray.opacity(0.20))
                        .aspectRatio(3, contentMode: .fit)
                    Circle()
                        .fill(Color.gray.opacity(0.32))
                        .frame(width: 80, height: 80)
                        .padding(Theme.Spacing.md)
                        .offset(y: 40)
                }
                .padding(.bottom, 48)

                VStack(alignment: .leading, spacing: 6) {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(Color.gray.opacity(0.32))
                        .frame(width: 180, height: 18)
                    RoundedRectangle(cornerRadius: 4)
                        .fill(Color.gray.opacity(0.22))
                        .frame(width: 120, height: 14)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, Theme.Spacing.md)

                Divider()
                    .padding(.horizontal, Theme.Spacing.md)
                    .padding(.top, Theme.Spacing.md)

                VStack(alignment: .leading, spacing: 14) {
                    ForEach(0..<3, id: \.self) { _ in
                        VStack(alignment: .leading, spacing: 6) {
                            RoundedRectangle(cornerRadius: 4)
                                .fill(Color.gray.opacity(0.22))
                                .frame(width: 90, height: 12)
                            RoundedRectangle(cornerRadius: 4)
                                .fill(Color.gray.opacity(0.30))
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .frame(height: 16)
                        }
                    }
                }
                .padding(Theme.Spacing.md)
            }
        }
        .redacted(reason: .placeholder)
        .accessibilityHidden(true)
    }
}

#Preview {
    UserProfilePanelView(
        chat: Chat(
            id: "1",
            title: "Иван Иванов",
            isGroupChat: false,
            members: [
                ChatMember(
                    userID: 2,
                    username: "ivan_ivanov",
                    firstName: "Иван",
                    lastName: "Иванов",
                    role: .member
                )
            ]
        )
    )
    .environment(DependencyContainer())
    .environment(AppCoordinator())
    .frame(width: 320, height: 600)
}
