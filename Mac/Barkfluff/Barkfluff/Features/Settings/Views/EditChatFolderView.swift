//
//  EditChatFolderView.swift
//  Barkfluff
//
//  Создание / редактирование папки чатов (Mac).
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
    @State private var showChatPicker = false

    /// Колбэк вызывается при закрытии — true если были изменения.
    private let onClose: (Bool) -> Void

    init(folder: ChatFolder?, onClose: @escaping (Bool) -> Void) {
        self._viewModel = State(initialValue: EditChatFolderViewModel(folder: folder))
        self.onClose = onClose
    }

    var body: some View {
        Form {
            Section("Название") {
                TextField("Например, Работа", text: $viewModel.name)
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
                HStack {
                    Text("Включённые чаты")
                    Spacer()
                    Text(viewModel.selectedChatIDs.isEmpty ? "Нет" : "\(viewModel.selectedChatIDs.count)")
                        .foregroundStyle(.secondary)
                    Button("Изменить…") {
                        showChatPicker = true
                    }
                }
            }

            if let error = viewModel.errorMessage {
                Section {
                    Text(error).foregroundStyle(.red).font(.caption)
                }
            }
        }
        .formStyle(.grouped)
        .navigationTitle(viewModel.isCreating ? "Новая папка" : "Папка")
        .toolbar {
            ToolbarItem(placement: .cancellationAction) {
                Button("Отмена") {
                    onClose(false)
                    dismiss()
                }
            }
            if !viewModel.isCreating {
                ToolbarItem(placement: .destructiveAction) {
                    Button("Удалить", role: .destructive) {
                        showDeleteConfirm = true
                    }
                }
            }
            ToolbarItem(placement: .confirmationAction) {
                Button("Сохранить") {
                    Task {
                        if await viewModel.save() {
                            onClose(true)
                            dismiss()
                        }
                    }
                }
                .keyboardShortcut(.return, modifiers: [.command])
                .disabled(!viewModel.isNameValid || viewModel.isSaving)
            }
        }
        .task {
            viewModel.dependencyContainer = container
        }
        .sheet(isPresented: $showChatPicker) {
            NavigationStack {
                FolderChatPickerView(selectedChatIDs: $viewModel.selectedChatIDs)
            }
            .frame(minWidth: 420, minHeight: 480)
        }
        .confirmationDialog(
            "Удалить папку?",
            isPresented: $showDeleteConfirm
        ) {
            Button("Удалить", role: .destructive) {
                Task {
                    if await viewModel.delete() {
                        onClose(true)
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
