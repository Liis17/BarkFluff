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
            Section("Название") {
                TextField("Например, Работа", text: $viewModel.name)
                    .textInputAutocapitalization(.sentences)
            }

            Section("Иконка") {
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

            Section("Чаты в папке") {
                NavigationLink {
                    FolderChatPickerView(selectedChatIDs: $viewModel.selectedChatIDs)
                } label: {
                    HStack {
                        Text("Включённые чаты")
                        Spacer()
                        Text(viewModel.selectedChatIDs.isEmpty ? "Нет" : "\(viewModel.selectedChatIDs.count)")
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
                            Text("Удалить папку")
                            Spacer()
                        }
                    }
                }
            }
        }
        .navigationTitle(viewModel.isCreating ? "Новая папка" : "Папка")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Сохранить") {
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
            "Удалить папку?",
            isPresented: $showDeleteConfirm,
            titleVisibility: .visible
        ) {
            Button("Удалить", role: .destructive) {
                Task {
                    if await viewModel.delete() {
                        dismiss()
                    }
                }
            }
            Button("Отмена", role: .cancel) {}
        } message: {
            Text("Чаты не будут удалены, только сама папка.")
        }
    }
}
