//
//  ProfileHeaderSection.swift
//  Barkfluff
//
//  Секция шапки профиля с аватаром и именем
//

import SwiftUI
import BFCore

/// Шапка профиля: аватар, имя, юзернейм
struct ProfileHeaderSection: View {
    let viewModel: UserProfilePanelViewModel

    @Environment(DependencyContainer.self) private var container
    @State private var isHoveringAvatar = false

    var body: some View {
        VStack(spacing: Theme.Spacing.sm) {
            // Большой аватар (96pt), кликабельный для полного размера
            Button {
                openFullSizeAvatar()
            } label: {
                AvatarView(
                    imageURL: viewModel.avatarURL,
                    initials: viewModel.initials,
                    size: 96
                )
                .overlay {
                    if isHoveringAvatar {
                        RoundedRectangle(cornerRadius: 48)
                            .fill(Color.black.opacity(0.3))
                            .overlay {
                                Image(systemName: "arrow.up.left.and.arrow.down.right")
                                    .font(.title2)
                                    .foregroundStyle(.white)
                            }
                    }
                }
            }
            .buttonStyle(.plain)
            .onHover { hovering in
                isHoveringAvatar = hovering
            }
            .disabled(viewModel.avatarURL == nil)

            // Имя и фамилия
            Text(viewModel.displayName)
                .font(.title2.bold())
                .lineLimit(2)
                .multilineTextAlignment(.center)

            // Юзернейм
            if let username = viewModel.username {
                Text("@\(username)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            // Количество участников (для группы)
            if viewModel.isGroupChat {
                Text("\(viewModel.memberCount) участников")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.bottom, Theme.Spacing.md)
        .frame(maxWidth: .infinity)
    }

    private func openFullSizeAvatar() {
        guard let avatarURL = viewModel.fullSizeAvatarURL else { return }

        // Создаем attachment для просмотра
        let attachment = MessageAttachment(
            id: 0,
            type: .image,
            fileID: "",
            fileName: "avatar.jpg",
            fileSize: 0,
            previewURL: avatarURL
        )

        FullScreenMediaWindowManager.shared.openMediaViewer(
            attachments: [attachment],
            initialIndex: 0,
            container: container
        )
    }
}

#Preview {
    let container = DependencyContainer()
    let chat = Chat(id: "1", title: "Тест", isGroupChat: true, members: [])

    ProfileHeaderSection(
        viewModel: UserProfilePanelViewModel(
            chat: chat,
            userService: container.userService,
            chatService: container.chatService,
            sharedMediaService: container.sharedMediaService,
            fileService: container.fileService
        )
    )
    .padding()
    .frame(width: 300)
}
