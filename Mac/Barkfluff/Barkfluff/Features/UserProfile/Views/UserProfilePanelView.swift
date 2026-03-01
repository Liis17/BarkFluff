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
                    .padding(.top, Theme.Spacing.lg)
                    .padding(.bottom, Theme.Spacing.xl)
                }
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
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
            }
        }
        .task {
            let vm = UserProfilePanelViewModel(
                chat: chat,
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
