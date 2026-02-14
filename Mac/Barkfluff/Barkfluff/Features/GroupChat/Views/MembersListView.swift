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
                Section("Владелец") {
                    MemberRow(name: "Владелец группы", role: .owner)
                }

                Section("Администраторы") {
                    MemberRow(name: "Админ 1", role: .admin)
                }

                Section("Участники") {
                    ForEach(0..<3, id: \.self) { _ in
                        MemberRow(name: "Участник", role: .member)
                    }
                }
            }
            .navigationTitle("Участники")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Готово") {
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

    var body: some View {
        HStack {
            Image(systemName: "person.circle.fill")
                .foregroundStyle(.secondary)

            Text(name)

            Spacer()

            Text(role.displayName)
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
