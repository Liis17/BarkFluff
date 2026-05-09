//
//  GroupMembersSection.swift
//  Barkfluff (iOS)
//
//  Список участников группового чата.
//

import SwiftUI
import BFCore

struct GroupMembersSection: View {
    @Bindable var viewModel: UserProfilePanelViewModel
    @State private var expanded = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Участники (\(viewModel.memberCount))")
                    .font(.headline)
                Spacer()
                if viewModel.members.count < viewModel.memberCount && !expanded {
                    Button("Показать всех") {
                        expanded = true
                        Task { await viewModel.loadAllMembers() }
                    }
                    .font(.caption)
                }
            }
            .padding(.horizontal, 16)

            VStack(spacing: 0) {
                ForEach(viewModel.members) { member in
                    MemberRow(member: member)
                    if member.id != viewModel.members.last?.id {
                        Divider()
                            .padding(.leading, 64)
                    }
                }
                if viewModel.isLoadingMembers {
                    HStack {
                        Spacer()
                        ProgressView()
                            .padding(8)
                        Spacer()
                    }
                }
            }
            .background(Color(uiColor: .secondarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: 12))
            .padding(.horizontal, 16)
        }
    }
}

private struct MemberRow: View {
    let member: DetailedChatMember

    var body: some View {
        HStack(spacing: 12) {
            AvatarView(
                imageURL: member.profilePictureURL,
                initials: member.initials,
                size: 40
            )
            VStack(alignment: .leading, spacing: 2) {
                Text(member.displayName)
                    .font(.body)
                Text("@\(member.username)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            if member.role == .owner {
                Text("Владелец")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            } else if member.role == .admin {
                Text("Админ")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }
}
