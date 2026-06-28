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
            Section("sidebar.section.messages") {
                ContentUnavailableView(
                    "sidebar.empty.title",
                    systemImage: "message",
                    description: Text("sidebar.empty.description")
                )
            }
        }
        .listStyle(.sidebar)
        .searchable(text: $searchText, prompt: Text("sidebar.search.prompt"))
    }
}
