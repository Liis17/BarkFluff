//
//  UserSearchView.swift
//  Barkfluff (iOS)
//
//  Поиск пользователей. Открывается из таб-бара чатов через `.userSearch` sheet.
//  Тап по найденному пользователю — открыть/создать DM-чат.
//

import SwiftUI
import BFCore

struct UserSearchView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: UserSearchViewModel?

    var body: some View {
        NavigationStack {
            content
                .navigationTitle("Поиск")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Отмена") { dismiss() }
                    }
                }
        }
        .task {
            if viewModel == nil {
                viewModel = UserSearchViewModel(userService: container.userService)
            }
        }
    }

    @ViewBuilder
    private var content: some View {
        if let vm = viewModel {
            @Bindable var vm = vm

            List {
                if vm.searchQuery.isEmpty {
                    ContentUnavailableView(
                        "Поиск пользователей",
                        systemImage: "person.magnifyingglass",
                        description: Text("Введите имя или username для поиска")
                    )
                } else if vm.isLoading {
                    HStack {
                        Spacer()
                        ProgressView()
                        Spacer()
                    }
                } else if vm.searchResults.isEmpty, vm.searchQuery.count >= 3 {
                    ContentUnavailableView(
                        "Ничего не найдено",
                        systemImage: "person.slash",
                        description: Text("Попробуйте изменить запрос")
                    )
                } else {
                    ForEach(vm.searchResults) { user in
                        Button {
                            Task { await openConversation(with: user) }
                        } label: {
                            UserSearchResultRow(user: user)
                        }
                        .buttonStyle(.plain)
                    }
                }

                if let error = vm.errorMessage {
                    Section {
                        Text(error)
                            .foregroundStyle(.red)
                            .font(.footnote)
                    }
                }
            }
            .searchable(text: $vm.searchQuery, prompt: "Имя или username")
            .onChange(of: vm.searchQuery) { _, _ in
                vm.search()
            }
        } else {
            ProgressView()
        }
    }

    private func openConversation(with user: User) async {
        // Используем уже существующий ChatListViewModel, если он есть, чтобы
        // переиспользовать логику резолвинга существующего DM-чата.
        if let listVM = coordinator.chatListViewModel {
            await listVM.openConversation(with: user, coordinator: coordinator)
        } else {
            // Fallback: создаём placeholder-чат для нового диалога.
            coordinator.openChat(Chat.newConversationPlaceholder(with: user))
        }
        dismiss()
    }
}

private struct UserSearchResultRow: View {
    let user: User

    var body: some View {
        HStack(spacing: 12) {
            AvatarView(
                imageURL: user.profilePicturePreviewURL,
                initials: user.initials,
                size: 44
            )

            VStack(alignment: .leading, spacing: 2) {
                Text(user.displayName)
                    .font(.headline)
                Text("@\(user.username)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            Spacer()
        }
        .padding(.vertical, 4)
    }
}
