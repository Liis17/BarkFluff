//
//  EditChatFolderView.swift
//  Barkfluff (iOS)
//
//  Создание / редактирование папки чатов.
//

import SwiftUI
import BFCore

private let folderIcons: [String] = [
    "📥", "⭐", "💼", "👥", "🎮", "✈️", "📚", "🎵",
    "💬", "❤️", "🔥", "🛒", "🏠", "☕", "🎂", "📷",
    "🎬", "🌐", "📰", "📌"
]

struct EditChatFolderView: View {
    @Environment(DependencyContainer.self) private var container
    @Environment(\.dismiss) private var dismiss

    @State private var viewModel: EditChatFolderViewModel
    @State private var showDeleteConfirm = false

    init(folder: ChatFolder? = nil) {
        self._viewModel = State(initialValue: EditChatFolderViewModel(folder: folder))
    }

    var body: some View {
        Form {
            Section("settings.chat_folders.name.section") {
                TextField("settings.chat_folders.name.placeholder.ios", text: $viewModel.name)
                    .textInputAutocapitalization(.sentences)
            }

            Section("settings.chat_folders.icon.section") {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 5), spacing: 8) {
                    ForEach(folderIcons, id: \.self) { emoji in
                        Button {
                            viewModel.icon = (viewModel.icon == emoji) ? "" : emoji
                        } label: {
                            Text(emoji)
                                .font(.title)
                                .frame(width: 44, height: 44)
                                .background(
                                    RoundedRectangle(cornerRadius: 10)
                                        .fill(viewModel.icon == emoji ? Color.accentColor.opacity(0.2) : Color.clear)
                                )
                                .overlay(
                                    RoundedRectangle(cornerRadius: 10)
                                        .stroke(viewModel.icon == emoji ? Color.accentColor : Color.gray.opacity(0.2), lineWidth: 1)
                                )
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.vertical, 4)
            }

            Section("settings.chat_folders.chats.section") {
                NavigationLink {
                    FolderChatPickerView(selectedChatIDs: $viewModel.selectedChatIDs)
                } label: {
                    HStack {
                        Text("settings.chat_folders.chats.included")
                        Spacer()
                        Text(viewModel.selectedChatIDs.isEmpty
                             ? String(localized: "common.none")
                             : "\(viewModel.selectedChatIDs.count)")
                            .foregroundStyle(.secondary)
                    }
                }
            }

            if let error = viewModel.errorMessage {
                Section {
                    Text(error).foregroundStyle(.red).font(.footnote)
                }
            }

            if !viewModel.isCreating {
                Section {
                    Button(role: .destructive) {
                        showDeleteConfirm = true
                    } label: {
                        HStack {
                            Spacer()
                            Text("settings.chat_folders.delete_button")
                            Spacer()
                        }
                    }
                }
            }
        }
        .navigationTitle(viewModel.isCreating
                         ? Text("settings.chat_folders.new")
                         : Text("settings.chat_folders.edit"))
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("common.save") {
                    Task {
                        if await viewModel.save() {
                            dismiss()
                        }
                    }
                }
                .disabled(!viewModel.isNameValid || viewModel.isSaving)
            }
        }
        .task {
            viewModel.dependencyContainer = container
        }
        .confirmationDialog(
            "settings.chat_folders.delete_confirm.title",
            isPresented: $showDeleteConfirm,
            titleVisibility: .visible
        ) {
            Button("common.delete", role: .destructive) {
                Task {
                    if await viewModel.delete() {
                        dismiss()
                    }
                }
            }
            Button("common.cancel", role: .cancel) {}
        } message: {
            Text("settings.chat_folders.delete_confirm.message")
        }
    }
}
