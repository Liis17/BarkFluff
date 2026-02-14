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
                Section("Название группы") {
                    TextField("Введите название", text: $title)
                }

                Section("Участники") {
                    TextField("Поиск пользователей", text: $searchQuery)
                        .textFieldStyle(.roundedBorder)

                    // TODO: Показать список пользователей для выбора
                    Text("Выберите участников для группы")
                        .foregroundStyle(.secondary)
                }

                Section {
                    Button("Создать группу") {
                        // TODO: Реализовать создание группы
                        dismiss()
                    }
                    .disabled(title.isEmpty || selectedUsers.isEmpty)
                }
            }
            .formStyle(.grouped)
            .navigationTitle("Новая группа")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Отмена") {
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
