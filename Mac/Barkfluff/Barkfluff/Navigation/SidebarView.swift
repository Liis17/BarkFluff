//
//  SidebarView.swift
//  Barkfluff
//
//  Сайдбар с списком чатов (альтернативный вариант, не используется)
//

import SwiftUI
import BFCore

struct SidebarView: View {
    @Binding var selectedChatID: String?
    @State private var searchText: String = ""

    var body: some View {
        List(selection: $selectedChatID) {
            Section("Сообщения") {
                ContentUnavailableView(
                    "Нет чатов",
                    systemImage: "message",
                    description: Text("Начните диалог или создайте группу")
                )
            }
        }
        .listStyle(.sidebar)
        .searchable(text: $searchText, prompt: "Поиск чатов")
    }
}
