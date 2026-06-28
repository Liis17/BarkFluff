//
//  CreateGroupChatView.swift
//  Barkfluff (iOS)
//
//  Экран создания группового чата: название + multi-select участников.
//

import SwiftUI
import BFCore

struct CreateGroupChatView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: GroupChatViewModel?

    var body: some View {
        NavigationStack {
            Group {
                if let vm = viewModel {
                    content(vm: vm)
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .navigationTitle("group_chat.create.title")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("common.cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("group_chat.create.action") {
                        Task { await create() }
                    }
                    .disabled(viewModel?.canCreate != true)
                }
            }
        }
        .task {
            if viewModel == nil {
                viewModel = GroupChatViewModel(
                    chatService: container.chatService,
                    userService: container.userService
                )
            }
        }
    }

    @ViewBuilder
    private func content(vm: GroupChatViewModel) -> some View {
        @Bindable var vm = vm

        Form {
            Section("group_chat.create.name_section") {
                TextField("group_chat.create.name_placeholder", text: $vm.title)
                    .textInputAutocapitalization(.sentences)
            }

            if !vm.selectedUsers.isEmpty {
                Section(header: Text("group_chat.create.selected_section \(vm.selectedUsers.count)")) {
                    ForEach(vm.selectedUsers) { user in
                        HStack {
                            AvatarView(
                                imageURL: user.profilePicturePreviewURL,
                                initials: user.initials,
                                size: 32
                            )
                            Text(user.displayName)
                            Spacer()
                            Button {
                                vm.toggleUserSelection(user)
                            } label: {
                                Image(systemName: "minus.circle.fill")
                                    .foregroundStyle(.red)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }

            Section("group_chat.create.search_section") {
                TextField("group_chat.create.search_placeholder", text: $vm.searchQuery)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .onChange(of: vm.searchQuery) { _, _ in
                        vm.searchUsers()
                    }

                if vm.isLoading {
                    HStack {
                        Spacer()
                        ProgressView()
                        Spacer()
                    }
                }

                ForEach(vm.searchResults) { user in
                    Button {
                        vm.toggleUserSelection(user)
                    } label: {
                        HStack {
                            AvatarView(
                                imageURL: user.profilePicturePreviewURL,
                                initials: user.initials,
                                size: 36
                            )
                            VStack(alignment: .leading, spacing: 2) {
                                Text(user.displayName)
                                    .foregroundStyle(.primary)
                                Text("@\(user.username)")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            if vm.selectedUserIDs.contains(user.id) {
                                Image(systemName: "checkmark.circle.fill")
                                    .foregroundStyle(.blue)
                            }
                        }
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
    }

    private func create() async {
        guard let vm = viewModel else { return }
        if let chat = await vm.createGroupChat() {
            dismiss()
            // Открываем новый чат в стеке списка чатов.
            coordinator.openChat(chat)
        }
    }
}
