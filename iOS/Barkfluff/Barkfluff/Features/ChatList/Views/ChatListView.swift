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

    @AppStorage("folders.compact") private var compactFolders: Bool = false
    @AppStorage("folders.excludeFromAll") private var excludeFolderChatsFromAll: Bool = false

    var body: some View {
        Group {
            if let viewModel {
                chatListContent(viewModel: viewModel)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .navigationTitle("chat_list.title")
        .task {
            if viewModel == nil {
                let vm = ChatListViewModel(
                    chatService: container.chatService,
                    userService: container.userService,
                    updatesService: container.updatesService,
                    onlineStatusService: container.onlineStatusService,
                    currentUserID: container.currentUserID,
                    localChatRepository: container.localChatRepository,
                    chatFolderService: container.chatFolderService
                )
                vm.isActiveChatChecker = { [weak coordinator] chatID in
                    coordinator?.selectedChat?.id == chatID
                }
                vm.excludeFolderChatsFromAll = excludeFolderChatsFromAll
                viewModel = vm
                coordinator.chatListViewModel = vm

                // Ждём пока сетевой слой будет готов (beacon endpoints +
                // refresh access-токена). До этого `listChats`/`track`/`getCurrentUser`
                // упадут с «Messages не настроено», а пользователи будут показаны
                // как онлайн по дефолту. Пока ждём — `isLoading=true` и UI показывает
                // ChatRowPlaceholderView с крутилкой.
                vm.isLoading = true
                let ready = await coordinator.waitForConnectionReady()
                guard ready else {
                    // Соединение не появилось за 30 сек — VM сама покажет ошибку
                    // при первой попытке loadChats. Снимаем сплеш, чтобы юзер
                    // увидел экран.
                    vm.isLoading = false
                    coordinator.isInitialChatsLoaded = true
                    return
                }

                // Connection готов — параллельно: чаты, папки, профиль.
                async let foldersLoad: Void = vm.loadFolders()
                async let chatsLoad: Void = vm.loadChats()
                async let userLoad: Void = container.loadCurrentUser()
                _ = await (foldersLoad, chatsLoad, userLoad)

                // Первая загрузка завершена (даже при ошибке — иначе сплеш повиснет;
                // ошибка отобразится плашкой в списке чатов).
                coordinator.isInitialChatsLoaded = true

                await vm.startListeningForUpdates()
            }
        }
        .onChange(of: excludeFolderChatsFromAll) { _, newValue in
            viewModel?.excludeFolderChatsFromAll = newValue
        }
        .onDisappear {
            viewModel?.stopListeningForUpdates()
        }
    }

    @ViewBuilder
    private func chatListContent(viewModel: ChatListViewModel) -> some View {
        VStack(spacing: 0) {
            // Папки сверху — всегда видны, ничем не перекрываются.
            ChatFolderTabsBar(
                folders: viewModel.folders,
                selectedFolderID: viewModel.selectedFolderID,
                allChatsUnread: viewModel.unreadCount(for: nil),
                unreadByFolder: { viewModel.unreadCount(for: $0) },
                compact: compactFolders,
                onSelect: { viewModel.selectFolder($0) }
            )

            List {
                if !viewModel.searchResults.isEmpty {
                    Section("chat_list.search.section.users") {
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

                if viewModel.isLoading && viewModel.chats.isEmpty {
                    ForEach(0..<5, id: \.self) { _ in
                        ChatRowPlaceholderView()
                    }
                } else {
                    ForEach(viewModel.chats) { chat in
                        Button {
                            coordinator.openChat(chat)
                            viewModel.markChatAsReadLocally(chatID: chat.id)
                        } label: {
                            ChatRowView(
                                chat: chat,
                                currentUserID: container.currentUserID,
                                onlineStatusService: container.onlineStatusService
                            )
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.bfPressable)
                    }
                }
            }
            .listStyle(.plain)
            .overlay {
                if !viewModel.isLoading && viewModel.chats.isEmpty
                    && viewModel.searchText.isEmpty && viewModel.errorMessage == nil {
                    ContentUnavailableView(
                        "chat_list.empty.title",
                        systemImage: "message",
                        description: Text("chat_list.empty.description")
                    )
                } else if let error = viewModel.errorMessage, viewModel.chats.isEmpty {
                    VStack(spacing: Theme.Spacing.md) {
                        Image(systemName: "wifi.exclamationmark")
                            .font(.largeTitle)
                            .foregroundStyle(.secondary)

                        Text(error)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)

                        Button("common.retry") {
                            Task { await viewModel.refresh() }
                        }
                        .buttonStyle(.bordered)
                    }
                    .padding()
                }
            }
            .safeAreaInset(edge: .top, spacing: 0) {
                VStack(spacing: Theme.Spacing.xs) {
                    if viewModel.isOffline && !viewModel.chats.isEmpty {
                        ErrorBannerView(
                            message: String(localized: "chat_list.offline_banner"),
                            onDismiss: { viewModel.isOffline = false }
                        )
                        .padding(.horizontal, Theme.Spacing.md)
                        .padding(.top, Theme.Spacing.xs)
                    }
                    if viewModel.isRefreshing {
                        RefreshingIndicatorView()
                    }
                }
            }
            .refreshable {
                await viewModel.refresh()
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
            prompt: Text("chat_list.search.prompt")
        )
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button {
                        coordinator.presentedSheet = .userSearch
                    } label: {
                        Label("chat_list.menu.new_chat", systemImage: "person.crop.circle.badge.plus")
                    }
                    Button {
                        coordinator.presentedSheet = .createGroupChat
                    } label: {
                        Label("chat_list.menu.new_group", systemImage: "person.3.fill")
                    }
                } label: {
                    Image(systemName: "square.and.pencil")
                }
            }
        }
    }
}

// MARK: - Chat Row View

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
            // Аватар
            AvatarView(
                imageURL: chat.pictureURL,
                initials: chat.avatarInitials,
                size: 50,
                isOnline: onlineStatus.isOnline,
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
                        Text(DateFormatterHelper.formatForChatList(date, locale: locale))
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
