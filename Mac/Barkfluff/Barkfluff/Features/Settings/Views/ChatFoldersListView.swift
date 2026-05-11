//
//  ChatFoldersListView.swift
//  Barkfluff
//
//  Экран управления папками чатов (Mac).
//

import SwiftUI
import BFCore

struct ChatFoldersListView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = ChatFoldersListViewModel()
    @State private var editingFolder: ChatFolder?
    @State private var isCreating: Bool = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Папки чатов").font(.title2.weight(.semibold))
                Spacer()
                Button {
                    isCreating = true
                } label: {
                    Label("Новая папка", systemImage: "plus")
                }
                .keyboardShortcut("n", modifiers: [.command])
            }
            .padding(.horizontal)
            .padding(.top)

            if let err = viewModel.errorMessage {
                HStack(spacing: 8) {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                    Text(err).font(.caption).foregroundStyle(.red)
                    Spacer()
                }
                .padding(.horizontal)
                .padding(.top, 8)
            }

            if viewModel.isLoading && viewModel.folders.isEmpty {
                HStack { Spacer(); ProgressView(); Spacer() }
                    .frame(maxHeight: .infinity)
            } else if viewModel.folders.isEmpty {
                VStack(spacing: 8) {
                    Image(systemName: "folder.badge.questionmark")
                        .font(.largeTitle)
                        .foregroundStyle(.secondary)
                    Text("Пока нет папок. Создайте первую папку, чтобы группировать чаты.")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .padding()
            } else {
                List {
                    ForEach(viewModel.folders) { folder in
                        FolderRow(folder: folder)
                            .contentShape(Rectangle())
                            .onTapGesture {
                                editingFolder = folder
                            }
                            .contextMenu {
                                Button("Редактировать") { editingFolder = folder }
                                Divider()
                                Button("Удалить", role: .destructive) {
                                    Task { await viewModel.delete(folderID: folder.id) }
                                }
                            }
                    }
                    .onMove { source, destination in
                        Task { await viewModel.move(from: source, to: destination) }
                    }
                }
                .listStyle(.inset)
            }
        }
        .frame(minWidth: 480, minHeight: 400)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.load()
        }
        .sheet(isPresented: $isCreating) {
            NavigationStack {
                EditChatFolderView(folder: nil) { didChange in
                    isCreating = false
                    if didChange { Task { await viewModel.load() } }
                }
            }
            .frame(minWidth: 520, minHeight: 520)
        }
        .sheet(item: $editingFolder) { folder in
            NavigationStack {
                EditChatFolderView(folder: folder) { didChange in
                    editingFolder = nil
                    if didChange { Task { await viewModel.load() } }
                }
            }
            .frame(minWidth: 520, minHeight: 520)
        }
    }
}

private struct FolderRow: View {
    let folder: ChatFolder

    var body: some View {
        HStack(spacing: 12) {
            ZStack {
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color.accentColor.opacity(0.12))
                    .frame(width: 32, height: 32)
                if folder.icon.isEmpty {
                    Image(systemName: "folder.fill")
                        .foregroundStyle(Color.accentColor)
                } else {
                    Text(folder.icon).font(.title3)
                }
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(folder.name.isEmpty ? "Без названия" : folder.name)
                    .font(.body)
                Text("Чатов: \(folder.chatIDs.count)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Image(systemName: "line.3.horizontal")
                .foregroundStyle(.tertiary)
                .font(.callout)
        }
        .padding(.vertical, 2)
    }
}
