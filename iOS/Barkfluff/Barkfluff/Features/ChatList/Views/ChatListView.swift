//
//  ChatListView.swift
//  Barkfluff
//
//  Список чатов (iOS версия)
//

import SwiftUI
import BFCore

struct ChatListView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ChatListViewModel?

    var body: some View {
        Group {
            if let viewModel {
                chatListContent(viewModel: viewModel)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .navigationTitle("Чаты")
        .task {
            if viewModel == nil {
                let vm = ChatListViewModel(
                    chatService: container.chatService,
                    userService: container.userService,
                    updatesService: container.updatesService,
                    onlineStatusService: container.onlineStatusService,
                    currentUserID: container.currentUserID
                )
                vm.isActiveChatChecker = { [weak coordinator] chatID in
                    coordinator?.selectedChat?.id == chatID
                }
                viewModel = vm
                coordinator.chatListViewModel = vm
                await container.loadCurrentUser()
                await vm.loadChats()
                await vm.startListeningForUpdates()
            }
        }
        .onDisappear {
            viewModel?.stopListeningForUpdates()
        }
    }

    @ViewBuilder
    private func chatListContent(viewModel: ChatListViewModel) -> some View {
        ZStack {
            // Основной список
            List {
                // Search results section
                if !viewModel.searchResults.isEmpty {
                    Section("Пользователи") {
                        ForEach(viewModel.searchResults) { user in
                            Button {
                                Task {
                                    await viewModel.openConversation(
                                        with: user,
                                        coordinator: coordinator
                                    )
                                }
                            } label: {
                                UserSearchRowView(user: user)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }

                // Chats section (без заголовка)
                ForEach(viewModel.chats) { chat in
                    ChatRowView(
                        chat: chat,
                        onlineStatus: viewModel.onlineStatus(for: chat)
                    )
                    .contentShape(Rectangle()) // Весь ряд кликабельный
                    .onTapGesture {
                        coordinator.openChat(chat)
                        viewModel.markChatAsReadLocally(chatID: chat.id)
                    }
                    .onAppear {
                        if chat.id == viewModel.chats.last?.id {
                            Task { await viewModel.loadMoreChats() }
                        }
                    }
                }
            }
            .listStyle(.plain)

            // Крутеляшка загрузки (только при самой первой загрузке)
            if viewModel.isLoading && viewModel.chats.isEmpty {
                Color(uiColor: .systemBackground)
                    .ignoresSafeArea()
                ProgressView()
                    .scaleEffect(1.2)
            }

            // "Нет чатов" - только когда не грузим и список действительно пустой
            if !viewModel.isLoading && viewModel.chats.isEmpty && viewModel.searchText.isEmpty && viewModel.errorMessage == nil {
                ContentUnavailableView(
                    "Нет чатов",
                    systemImage: "message",
                    description: Text("Начните диалог или создайте группу")
                )
            }

            // Ошибка
            if let error = viewModel.errorMessage, viewModel.chats.isEmpty {
                VStack(spacing: Theme.Spacing.md) {
                    Image(systemName: "wifi.exclamationmark")
                        .font(.largeTitle)
                        .foregroundStyle(.secondary)

                    Text(error)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)

                    Button("Повторить") {
                        Task { await viewModel.refresh() }
                    }
                    .buttonStyle(.bordered)
                }
                .padding()
                .background(Color(uiColor: .systemBackground))
            }
        }
        .searchable(
            text: Binding(
                get: { viewModel.searchText },
                set: { newValue in
                    viewModel.searchText = newValue
                    viewModel.onSearchTextChanged()
                }
            ),
            prompt: "Поиск"
        )
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button {
                    coordinator.presentedSheet = .createGroupChat
                } label: {
                    Image(systemName: "square.and.pencil")
                }
            }
        }
        .refreshable {
            await viewModel.refresh()
        }
    }
}

// MARK: - Chat Row View

struct ChatRowView: View {
    let chat: Chat
    let onlineStatus: OnlineStatus

    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            // Аватар
            AvatarView(
                imageURL: chat.pictureURL,
                initials: chat.avatarInitials,
                size: 50,
                isOnline: onlineStatus == .online,
                showOnlineIndicator: !chat.isGroupChat
            )

            // Контент
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(chat.title)
                        .font(.headline)
                        .foregroundStyle(.primary)
                        .lineLimit(1)

                    Spacer()

                    if let date = chat.lastMessageDate {
                        Text(date, style: .time)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                }

                HStack {
                    Text(chat.lastMessagePreview ?? "")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)

                    Spacer()

                    if chat.hasUnread {
                        Text("\(chat.unreadCount)")
                            .font(.caption2)
                            .fontWeight(.semibold)
                            .foregroundStyle(.white)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(.blue)
                            .clipShape(Capsule())
                    }
                }
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
    }
}

// MARK: - User Search Row View

struct UserSearchRowView: View {
    let user: User

    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            AvatarView(
                imageURL: user.profilePicturePreviewURL,
                initials: user.initials,
                size: 44
            )

            VStack(alignment: .leading, spacing: 2) {
                Text(user.displayName)
                    .font(.headline)
                    .foregroundStyle(.primary)

                Text("@\(user.username)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
    }
}

// MARK: - Placeholder View

struct ChatRowPlaceholderView: View {
    var body: some View {
        HStack(spacing: Theme.Spacing.md) {
            Circle()
                .fill(Color(uiColor: .systemGray4))
                .frame(width: 50, height: 50)

            VStack(alignment: .leading, spacing: 4) {
                RoundedRectangle(cornerRadius: 4)
                    .fill(Color(uiColor: .systemGray4))
                    .frame(width: 120, height: 16)

                RoundedRectangle(cornerRadius: 4)
                    .fill(Color(uiColor: .systemGray5))
                    .frame(width: 200, height: 12)
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
        .redacted(reason: .placeholder)
    }
}

#Preview {
    NavigationStack {
        ChatListView()
            .environment(AppCoordinator())
            .environment(DependencyContainer())
    }
}
