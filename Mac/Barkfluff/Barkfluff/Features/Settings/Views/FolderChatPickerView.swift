//
//  FolderChatPickerView.swift
//  Barkfluff
//
//  Экран мульти-выбора чатов для папки (Mac).
//

import SwiftUI
import BFCore

struct FolderChatPickerView: View {
    @Environment(DependencyContainer.self) private var container
    @Environment(\.dismiss) private var dismiss

    @Binding var selectedChatIDs: Set<String>

    @State private var chats: [Chat] = []
    @State private var isLoading: Bool = false
    @State private var errorMessage: String?
    @State private var searchText: String = ""

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(selectedChatIDs.isEmpty ? "Выберите чаты" : "Выбрано: \(selectedChatIDs.count)")
                    .font(.headline)
                Spacer()
                Button("Готово") { dismiss() }
                    .keyboardShortcut(.return, modifiers: [.command])
            }
            .padding()

            if let err = errorMessage {
                Text(err).foregroundStyle(.red).font(.caption).padding(.horizontal)
            }

            if isLoading && chats.isEmpty {
                HStack { Spacer(); ProgressView(); Spacer() }
                    .frame(maxHeight: .infinity)
            } else {
                List {
                    ForEach(filteredChats, id: \.id) { chat in
                        Button {
                            toggle(chatID: chat.id)
                        } label: {
                            HStack {
                                Image(systemName: selectedChatIDs.contains(chat.id) ? "checkmark.circle.fill" : "circle")
                                    .foregroundStyle(selectedChatIDs.contains(chat.id) ? Color.accentColor : .secondary)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(chat.title.isEmpty ? "Чат" : chat.title)
                                        .foregroundStyle(.primary)
                                    if chat.isGroupChat {
                                        Text("Группа · \(chat.members.count) уч.")
                                            .font(.caption)
                                            .foregroundStyle(.secondary)
                                    }
                                }
                                Spacer()
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
                .listStyle(.inset)
                .searchable(text: $searchText, prompt: "Поиск чата")
            }
        }
        .task { await load() }
    }

    private var filteredChats: [Chat] {
        guard !searchText.isEmpty else { return chats }
        return chats.filter { $0.title.localizedCaseInsensitiveContains(searchText) }
    }

    private func toggle(chatID: String) {
        if selectedChatIDs.contains(chatID) {
            selectedChatIDs.remove(chatID)
        } else {
            selectedChatIDs.insert(chatID)
        }
    }

    private func load() async {
        let cached = (try? await container.localChatRepository.loadCachedChats()) ?? []
        if !cached.isEmpty {
            chats = cached
        }
        isLoading = true
        defer { isLoading = false }
        do {
            let page = try await container.chatService.listChats(offset: 0, size: 200)
            chats = page.items
        } catch {
            if chats.isEmpty {
                errorMessage = "Не удалось загрузить чаты"
            }
        }
    }
}
