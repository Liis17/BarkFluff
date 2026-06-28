//
//  ChatFoldersListView.swift
//  Barkfluff (iOS)
//
//  Экран управления папками чатов.
//

import SwiftUI
import BFCore

struct ChatFoldersListView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = ChatFoldersListViewModel()
    @State private var showCreate = false

    var body: some View {
        List {
            if let err = viewModel.errorMessage {
                Section {
                    Text(err).foregroundStyle(.red).font(.footnote)
                }
            }

            if viewModel.isLoading && viewModel.folders.isEmpty {
                Section {
                    HStack { Spacer(); ProgressView(); Spacer() }
                }
            }

            if viewModel.folders.isEmpty && !viewModel.isLoading {
                Section {
                    Text("settings.chat_folders.empty.short")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            } else {
                Section {
                    ForEach(viewModel.folders) { folder in
                        NavigationLink {
                            EditChatFolderView(folder: folder)
                        } label: {
                            FolderRowView(folder: folder)
                        }
                    }
                    .onDelete { offsets in
                        Task { await viewModel.delete(at: offsets) }
                    }
                    .onMove { source, destination in
                        Task { await viewModel.move(from: source, to: destination) }
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("settings.category.chat_folders")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarLeading) {
                if !viewModel.folders.isEmpty {
                    EditButton()
                }
            }
            ToolbarItem(placement: .primaryAction) {
                NavigationLink {
                    EditChatFolderView(folder: nil)
                } label: {
                    Image(systemName: "plus")
                }
            }
        }
        .task {
            viewModel.dependencyContainer = container
            await viewModel.load()
        }
        .refreshable {
            await viewModel.load()
        }
    }
}

private struct FolderRowView: View {
    let folder: ChatFolder

    var body: some View {
        HStack(spacing: 12) {
            ZStack {
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color.accentColor.opacity(0.12))
                    .frame(width: 36, height: 36)
                if folder.icon.isEmpty {
                    Image(systemName: "folder.fill")
                        .foregroundStyle(Color.accentColor)
                } else {
                    Text(folder.icon).font(.title3)
                }
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(folder.name.isEmpty ? String(localized: "settings.chat_folders.row.untitled") : folder.name)
                    .font(.body)
                Text("settings.chat_folders.row.count \(folder.chatIDs.count)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 2)
    }
}
