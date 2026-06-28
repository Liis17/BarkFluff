//
//  CreateGroupChatView.swift
//  Barkfluff
//
//  Создание группового чата
//

import SwiftUI

struct CreateGroupChatView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var title: String = ""
    @State private var selectedUsers: Set<Int64> = []
    @State private var searchQuery: String = ""

    var body: some View {
        NavigationStack {
            Form {
                Section("group_chat.create.name_section") {
                    TextField("group_chat.create.name_placeholder", text: $title)
                }

                Section("group_chat.members.title") {
                    TextField("group_chat.create.search_placeholder_users", text: $searchQuery)
                        .textFieldStyle(.roundedBorder)

                    // TODO: Показать список пользователей для выбора
                    Text("group_chat.create.select_members_hint")
                        .foregroundStyle(.secondary)
                }

                Section {
                    Button("group_chat.create.create_button") {
                        // TODO: Реализовать создание группы
                        dismiss()
                    }
                    .disabled(title.isEmpty || selectedUsers.isEmpty)
                }
            }
            .formStyle(.grouped)
            .navigationTitle("group_chat.create.title")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("common.cancel") {
                        dismiss()
                    }
                }
            }
        }
    }
}

#Preview {
    CreateGroupChatView()
}
