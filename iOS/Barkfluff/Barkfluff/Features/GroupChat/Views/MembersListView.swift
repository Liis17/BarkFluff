//
//  MembersListView.swift
//  Barkfluff (iOS)
//
//  Список участников группового чата.
//  В iOS отображение участников интегрировано в UserProfilePanelView.GroupMembersSection,
//  но этот экран остаётся отдельной точкой навигации для full-list view.
//

import SwiftUI
import BFCore

struct MembersListView: View {
    let chat: Chat

    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: UserProfilePanelViewModel?

    var body: some View {
        Group {
            if let vm = viewModel {
                List {
                    ForEach(vm.members) { member in
                        HStack(spacing: 12) {
                            AvatarView(
                                imageURL: member.profilePictureURL,
                                initials: member.initials,
                                size: 40
                            )
                            VStack(alignment: .leading, spacing: 2) {
                                Text(member.displayName)
                                Text("@\(member.username)")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Text(member.role.displayName)
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                        }
                    }
                    if vm.isLoadingMembers {
                        HStack {
                            Spacer()
                            ProgressView()
                            Spacer()
                        }
                    }
                }
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .navigationTitle("Участники")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            if viewModel == nil {
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
                await vm.loadAllMembers()
            }
        }
    }
}
