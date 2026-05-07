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
                    localChatRepository: container.localChatRepository
                )
                // Устанавливаем замыкание для проверки активного чата
                vm.isActiveChatChecker = { [weak coordinator] chatID in
                    coordinator?.selectedChat?.id == chatID
                }
                viewModel = vm
                // Устанавливаем ссылку в координатор для уведомлений о прочтении
                coordinator.chatListViewModel = vm
                // Параллельно: выгрузка текущего пользователя (нужна для онлайн-статусов)
                // и загрузка чатов (сразу из кэша + revalidate с сервера).
                async let userLoad: Void = container.loadCurrentUser()
                async let chatsLoad: Void = vm.loadChats()
                _ = await (userLoad, chatsLoad)
                await vm.startListeningForUpdates()
            }
        }
        .onDisappear {
            viewModel?.stopListeningForUpdates()
        }
    }

    @ViewBuilder
    private func chatListContent(viewModel: ChatListViewModel) -> some View {
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

            // Chats section
            Section("Сообщения") {
                if viewModel.isLoading && viewModel.chats.isEmpty {
                    ForEach(0..<5, id: \.self) { _ in
                        ChatRowPlaceholderView()
                    }
                } else if viewModel.chats.isEmpty && !viewModel.isLoading {
                    if viewModel.searchText.isEmpty {
                        Text("Нет чатов")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .center)
                            .padding(.vertical, Theme.Spacing.xl)
                    } else {
                        Text("Ничего не найдено")
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
            if viewModel.isRefreshing && !viewModel.chats.isEmpty {
                RefreshingIndicatorView()
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.2), value: viewModel.isRefreshing)
        .searchable(text: Binding(
            get: { viewModel.searchText },
            set: { newValue in
                viewModel.searchText = newValue
                viewModel.onSearchTextChanged()
            }
        ), placement: .sidebar, prompt: "Поиск")
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

                    Button("Повторить") {
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
