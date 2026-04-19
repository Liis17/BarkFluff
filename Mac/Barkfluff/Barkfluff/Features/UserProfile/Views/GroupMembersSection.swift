//
//  GroupMembersSection.swift
//  Barkfluff
//
//  Секция участников группы
//

import SwiftUI
import BFCore

/// Секция списка участников группы
struct GroupMembersSection: View {
    let viewModel: UserProfilePanelViewModel

    @State private var showAllMembers = false

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            // Заголовок
            HStack {
                Label("Участники", systemImage: "person.2.fill")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.secondary)

                Spacer()

                Text("\(viewModel.memberCount)")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(.quaternary))
            }

            // Список участников (показать первые 5, с кнопкой "Показать всех")
            let visibleMembers = showAllMembers
                ? viewModel.members
                : Array(viewModel.members.prefix(5))

            if viewModel.members.isEmpty && viewModel.isLoadingMembers {
                ProgressView()
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, Theme.Spacing.md)
            } else {
                ForEach(visibleMembers, id: \.userID) { member in
                    HStack(spacing: Theme.Spacing.sm) {
                        // Мини-аватар
                        AvatarView(
                            imageURL: member.profilePicturePreviewURL ?? member.profilePictureURL,
                            initials: memberInitials(member),
                            size: 32
                        )

                        VStack(alignment: .leading, spacing: 2) {
                            Text(member.displayName)
                                .font(.callout)
                                .lineLimit(1)

                            Text("@\(member.username)")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }

                        Spacer()

                        // Роль
                        if member.role != .member {
                            Text(member.role.displayName)
                                .font(.caption2)
                                .foregroundStyle(.white)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(roleColor(for: member.role))
                                .clipShape(Capsule())
                        }
                    }
                    .padding(.vertical, 2)
                }

                // Кнопка "Показать всех" если больше 5
                if viewModel.memberCount > 5, !showAllMembers {
                    Button {
                        showAllMembers = true
                        Task {
                            await viewModel.loadAllMembers()
                        }
                    } label: {
                        Text("Показать всех (\(viewModel.memberCount))")
                            .font(.callout)
                            .foregroundStyle(Color.accentColor)
                    }
                    .buttonStyle(.plain)
                    .padding(.top, Theme.Spacing.xxs)
                }

                // Пагинация для длинных списков
                if viewModel.isLoadingMembers {
                    HStack {
                        Spacer()
                        ProgressView()
                            .controlSize(.small)
                        Spacer()
                    }
                }
            }
        }
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.md)
    }

    private func memberInitials(_ member: DetailedChatMember) -> String {
        let first = member.firstName.first.map(String.init) ?? ""
        let last = member.lastName.first.map(String.init) ?? ""
        return "\(first)\(last)"
    }

    private func roleColor(for role: ChatMemberRole) -> Color {
        switch role {
        case .owner: return .orange
        case .admin: return .blue
        case .member: return .gray
        }
    }
}

#Preview {
    let container = DependencyContainer()
    let chat = Chat(id: "1", title: "Группа", isGroupChat: true, members: [])

    GroupMembersSection(
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
    .frame(width: 300)
}
