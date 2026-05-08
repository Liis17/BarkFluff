//
//  ProfileHeaderSection.swift
//  Barkfluff
//
//  Секция шапки профиля с постером, аватаром и именем
//

import SwiftUI
import BFCore

/// Шапка профиля: постер (для DM), аватар, имя, юзернейм
struct ProfileHeaderSection: View {
    let viewModel: UserProfilePanelViewModel

    @Environment(DependencyContainer.self) private var container
    @State private var isHoveringAvatar = false

    private static let avatarSize: CGFloat = 96
    private static let avatarOverlap: CGFloat = avatarSize / 2

    var body: some View {
        VStack(spacing: 0) {
            // Постер 3:1 — только для DM. У групп постера нет.
            if !viewModel.isGroupChat {
                posterView
            }

            // Аватар: для DM перекрывает нижнюю половину постера через отрицательный top padding.
            avatarButton
                .padding(.top, viewModel.isGroupChat ? 0 : -Self.avatarOverlap)
                .padding(.bottom, Theme.Spacing.sm)

            // Текстовый блок: имя, @username, статус / счётчик участников
            VStack(spacing: Theme.Spacing.sm) {
                Text(viewModel.displayName)
                    .font(.title2.bold())
                    .lineLimit(2)
                    .multilineTextAlignment(.center)

                if let username = viewModel.username {
                    Text("@\(username)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }

                if !viewModel.isGroupChat {
                    OnlineStatusText(status: viewModel.onlineStatus)
                }

                if viewModel.isGroupChat {
                    Text("\(viewModel.memberCount) участников")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.bottom, Theme.Spacing.md)
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: - Subviews

    @ViewBuilder
    private var posterView: some View {
        GeometryReader { geo in
            Group {
                if let fileID = viewModel.posterFileID {
                    CachedImageView(
                        fileID: fileID,
                        type: .poster,
                        content: { image in
                            image
                                .resizable()
                                .aspectRatio(contentMode: .fill)
                        },
                        placeholder: { posterPlaceholder }
                    )
                } else {
                    posterPlaceholder
                }
            }
            .frame(width: geo.size.width, height: geo.size.height)
            .clipped()
        }
        .aspectRatio(3.0, contentMode: .fit)
        .frame(maxWidth: .infinity)
    }

    private var posterPlaceholder: some View {
        LinearGradient(
            colors: [
                Color.accentColor.opacity(0.25),
                Color.accentColor.opacity(0.10)
            ],
            startPoint: .topLeading,
            endPoint: .bottomTrailing
        )
    }

    private var avatarButton: some View {
        Button {
            openFullSizeAvatar()
        } label: {
            AvatarView(
                imageURL: viewModel.avatarURL,
                initials: viewModel.initials,
                size: Self.avatarSize
            )
            .overlay {
                // Обводка цветом фона панели — визуальный «вырез» аватара из постера.
                if !viewModel.isGroupChat {
                    Circle()
                        .strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: 3)
                }
            }
            .overlay {
                if isHoveringAvatar {
                    RoundedRectangle(cornerRadius: Self.avatarSize / 2)
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
        .contentShape(Circle())
        .onHover { hovering in
            isHoveringAvatar = hovering
        }
        .disabled(viewModel.avatarURL == nil)
    }

    private func openFullSizeAvatar() {
        guard let avatarURL = viewModel.fullSizeAvatarURL else { return }

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
            messageText: nil,
            container: container
        )
    }
}

#Preview {
    let container = DependencyContainer()
    let chat = Chat(id: "1", title: "Тест", isGroupChat: false, members: [
        ChatMember(userID: 2, username: "test", firstName: "Тест", lastName: "Тестов", role: .member)
    ])

    ProfileHeaderSection(
        viewModel: UserProfilePanelViewModel(
            chat: chat,
            currentUserID: 0,
            userService: container.userService,
            chatService: container.chatService,
            sharedMediaService: container.sharedMediaService,
            fileService: container.fileService,
            onlineStatusService: container.onlineStatusService
        )
    )
    .padding()
    .frame(width: 320)
}
