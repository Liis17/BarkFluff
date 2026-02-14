//
//  GroupInfoView.swift
//  Barkfluff
//
//  Информация о групповом чате
//

import SwiftUI

struct GroupInfoView: View {
    let chatID: String
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section("Информация") {
                    HStack {
                        Spacer()
                        // TODO: Показать аватар группы
                        Image(systemName: "person.3.fill")
                            .font(.system(size: 60))
                            .foregroundStyle(.secondary)
                        Spacer()
                    }

                    LabeledContent("Название") {
                        Text("Групповой чат")
                    }

                    LabeledContent("Участников") {
                        Text("5")
                    }
                }

                Section("Участники") {
                    // TODO: Показать список участников
                    ForEach(0..<5, id: \.self) { _ in
                        HStack {
                            Image(systemName: "person.circle.fill")
                                .foregroundStyle(.secondary)
                            Text("Участник")
                            Spacer()
                            Text("Администратор")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }

                Section {
                    Button("Добавить участника") {
                        // TODO: Реализовать
                    }

                    Button("Покинуть группу", role: .destructive) {
                        // TODO: Реализовать
                    }
                }
            }
            .formStyle(.grouped)
            .navigationTitle("Информация о группе")
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

#Preview {
    GroupInfoView(chatID: "test")
}
