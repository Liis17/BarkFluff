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
                Section("group_chat.create.section.info") {
                    HStack {
                        Spacer()
                        // TODO: Показать аватар группы
                        Image(systemName: "person.3.fill")
                            .font(.system(size: 60))
                            .foregroundStyle(.secondary)
                        Spacer()
                    }

                    LabeledContent("group_chat.create.field.title") {
                        Text("common.chat")
                    }

                    LabeledContent("group_chat.create.field.members_count") {
                        Text("5")
                    }
                }

                Section("group_chat.members.title") {
                    // TODO: Показать список участников
                    ForEach(0..<5, id: \.self) { _ in
                        HStack {
                            Image(systemName: "person.circle.fill")
                                .foregroundStyle(.secondary)
                            Text("group_chat.members.member_placeholder")
                            Spacer()
                            Text("group_chat.members.section.admins")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }

                Section {
                    Button("group_chat.info.add_member") {
                        // TODO: Реализовать
                    }

                    Button("group_chat.info.leave_group", role: .destructive) {
                        // TODO: Реализовать
                    }
                }
            }
            .formStyle(.grouped)
            .navigationTitle("group_chat.info.title")
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

#Preview {
    GroupInfoView(chatID: "test")
}
