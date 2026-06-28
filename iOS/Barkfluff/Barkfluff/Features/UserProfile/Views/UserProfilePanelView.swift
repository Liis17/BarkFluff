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
                UserProfilePanelPlaceholderView()
            }
        }
        .navigationTitle(chat.isGroupChat ? Text("user_profile.title.group") : Text("user_profile.title.user"))
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

// MARK: - Placeholder

/// Skeleton-плейсхолдер: постер 3:1 + аватар-кружок + 2 строки текста +
/// блок инфо из 3 row'ов. Срабатывает, пока `viewModel == nil`.
private struct UserProfilePanelPlaceholderView: View {
    var body: some View {
        ScrollView {
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
