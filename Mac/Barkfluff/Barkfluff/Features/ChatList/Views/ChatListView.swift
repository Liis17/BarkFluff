//
//  ChatListView.swift
//  Barkfluff
//
//  Список чатов (sidebar)
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
                // Устанавливаем замыкание для проверки активного чата
                vm.isActiveChatChecker = { [weak coordinator] chatID in
                    coordinator?.selectedChat?.id == chatID
                }
                vm.excludeFolderChatsFromAll = excludeFolderChatsFromAll
                viewModel = vm
                // Устанавливаем ссылку в координатор для уведомлений о прочтении
                coordinator.chatListViewModel = vm
                // Ждём пока сетевой слой готов (beacon endpoints + refresh access-токена).
                // До этого `listChats`/`track`/`getCurrentUser` упадут с «Messages
                // не настроено», а онлайн-статусы будут пустые. Пока ждём —
                // `isLoading=true` и UI показывает ChatRowPlaceholderView.
                vm.isLoading = true
                let ready = await coordinator.waitForConnectionReady()
                guard ready else {
                    vm.isLoading = false
                    return
                }

                // Connection готов — параллельно: чаты, папки, профиль.
                async let foldersLoad: Void = vm.loadFolders()
                async let chatsLoad: Void = vm.loadChats()
                async let userLoad: Void = container.loadCurrentUser()
                _ = await (foldersLoad, chatsLoad, userLoad)

                await vm.startListeningForUpdates()

                // Системные уведомления — после loadCurrentUser, нужен currentUserID.
                await container.notificationService.start(
                    coordinator: coordinator,
                    currentUserID: container.currentUserID
                )
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
            ChatFolderTabsBar(
                folders: viewModel.folders,
                selectedFolderID: viewModel.selectedFolderID,
                allChatsUnread: viewModel.unreadCount(for: nil),
                unreadByFolder: { viewModel.unreadCount(for: $0) },
                compact: compactFolders,
                onSelect: { viewModel.selectFolder($0) }
            )

            chatListContentInner(viewModel: viewModel)
        }
    }

    @ViewBuilder
    private func topInset(viewModel: ChatListViewModel) -> some View {
        VStack(spacing: Theme.Spacing.xs) {
            if viewModel.isOffline && !viewModel.chats.isEmpty {
                ErrorBannerView(
                    message: LocalizedStringResource("chat_list.offline_banner"),
                    onRetry: { Task { await viewModel.revalidateChats() } }
                )
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.top, Theme.Spacing.xs)
                .transition(.opacity)
            }
            if viewModel.isRefreshing && !viewModel.chats.isEmpty {
                RefreshingIndicatorView()
                    .transition(.opacity)
            }
        }
    }

    @ViewBuilder
    private func chatListContentInner(viewModel: ChatListViewModel) -> some View {
        List(selection: Binding(
            get: { coordinator.selectedChat?.id },
            set: { newID in
                if let id = newID {
                    coordinator.selectedChat = viewModel.chats.first { $0.id == id }
                } else {
                    coordinator.selectedChat = nil
                }
            }
        )) {
            // Search results section
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

            // Chats section
            Section("chat_list.section.messages") {
                if viewModel.isLoading && viewModel.chats.isEmpty {
                    ForEach(0..<5, id: \.self) { _ in
                        ChatRowPlaceholderView()
                    }
                } else if viewModel.chats.isEmpty && !viewModel.isLoading {
                    if viewModel.searchText.isEmpty {
                        Text("chat_list.empty.title")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .center)
                            .padding(.vertical, Theme.Spacing.xl)
                    } else {
                        Text("user_search.empty.title")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .center)
                            .padding(.vertical, Theme.Spacing.xl)
                    }
                } else {
                    ForEach(viewModel.chats) { chat in
                        ChatRowView(
                            chat: chat,
                            currentUserID: container.currentUserID,
                            onlineStatusService: container.onlineStatusService
                        )
                        .tag(chat.id)
                        .onAppear {
                            if chat.id == viewModel.chats.last?.id {
                                Task { await viewModel.loadMoreChats() }
                            }
                        }
                    }
                }
            }
        }
        .listStyle(.sidebar)
        .safeAreaInset(edge: .top, spacing: 0) {
            topInset(viewModel: viewModel)
        }
        .animation(.easeInOut(duration: 0.2), value: viewModel.isRefreshing)
        .animation(.easeInOut(duration: 0.2), value: viewModel.isOffline)
        .searchable(text: Binding(
            get: { viewModel.searchText },
            set: { newValue in
                viewModel.searchText = newValue
                viewModel.onSearchTextChanged()
            }
        ), placement: .sidebar, prompt: Text("chat_list.search.prompt"))
        .overlay {
            if let error = viewModel.errorMessage, viewModel.chats.isEmpty {
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

#Preview {
    NavigationStack {
        ChatListView()
            .environment(AppCoordinator())
            .environment(DependencyContainer())
    }
}
