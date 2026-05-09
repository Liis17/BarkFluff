//
//  UserProfilePanelView.swift
//  Barkfluff (iOS)
//
//  Полноэкранный профиль собеседника / инфо группы.
//  Открывается push-навигацией из ConversationView через ConversationDestination.userProfile.
//

import SwiftUI
import BFCore

struct UserProfilePanelView: View {
    let chat: Chat

    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: UserProfilePanelViewModel?

    var body: some View {
        Group {
            if let vm = viewModel {
                content(vm: vm)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .navigationTitle(chat.isGroupChat ? "Информация о группе" : "Профиль")
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
                await vm.loadSharedMedia()
            }
        }
        .onDisappear {
            viewModel?.stopListeningForOnlineStatus()
        }
    }

    @ViewBuilder
    private func content(vm: UserProfilePanelViewModel) -> some View {
        ScrollView {
            VStack(spacing: 16) {
                ProfileHeaderSection(viewModel: vm)
                ProfileInfoSection(viewModel: vm)

                if vm.isGroupChat {
                    GroupMembersSection(viewModel: vm)
                }

                SharedMediaSection(viewModel: vm)
            }
            .padding(.bottom, 32)
        }
    }
}
