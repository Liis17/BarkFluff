//
//  MembersListView.swift
//  Barkfluff
//
//  Список участников чата
//

import SwiftUI
import BFCore

struct MembersListView: View {
    let chatID: String
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                Section("group_chat.members.section.owner") {
                    MemberRow(name: String(localized: "group_chat.members.owner_placeholder"), role: .owner)
                }

                Section("group_chat.members.section.admins") {
                    MemberRow(name: String(localized: "group_chat.members.admin_placeholder \(1)"), role: .admin)
                }

                Section("group_chat.members.section.members") {
                    ForEach(0..<3, id: \.self) { _ in
                        MemberRow(name: String(localized: "group_chat.members.member_placeholder"), role: .member)
                    }
                }
            }
            .navigationTitle("group_chat.members.title")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.done") {
                        dismiss()
                    }
                }
            }
        }
    }
}

struct MemberRow: View {
    let name: String
    let role: ChatMemberRole
    @Environment(\.locale) private var locale

    var body: some View {
        HStack {
            Image(systemName: "person.circle.fill")
                .foregroundStyle(.secondary)

            Text(name)

            Spacer()

            Text(role.displayName(in: locale))
                .font(.caption)
                .padding(.horizontal, 8)
                .padding(.vertical, 4)
                .background(roleColor)
                .foregroundStyle(.white)
                .clipShape(Capsule())
        }
    }

    var roleColor: Color {
        switch role {
        case .owner: return .red
        case .admin: return .orange
        case .member: return .gray
        }
    }
}

#Preview {
    MembersListView(chatID: "test")
}
