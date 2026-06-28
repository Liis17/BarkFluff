//
//  ChatFoldersListViewModel.swift
//  Barkfluff
//

import SwiftUI
import Observation
import BFCore

@MainActor
@Observable
final class ChatFoldersListViewModel {

    var folders: [ChatFolder] = []
    var isLoading: Bool = false
    var errorMessage: String?

    weak var dependencyContainer: DependencyContainer?

    func load() async {
        guard let dc = dependencyContainer else { return }

        let cached = await dc.chatFolderService.loadCached()
        if !cached.isEmpty {
            folders = cached
        }

        isLoading = true
        errorMessage = nil
        do {
            folders = try await dc.chatFolderService.refresh()
        } catch {
            errorMessage = String(localized: "settings.chat_folders.error.load")
        }
        isLoading = false
    }

    func delete(folderID: String) async {
        guard let dc = dependencyContainer else { return }
        let previous = folders
        folders.removeAll { $0.id == folderID }
        do {
            try await dc.chatFolderService.delete(folderID: folderID)
        } catch {
            folders = previous
            errorMessage = String(localized: "settings.chat_folders.error.delete")
        }
    }

    func move(from source: IndexSet, to destination: Int) async {
        guard let dc = dependencyContainer else { return }
        let previous = folders
        folders.move(fromOffsets: source, toOffset: destination)
        let orders: [(folderID: String, sortOrder: Int32)] = folders.enumerated().map { idx, folder in
            (folderID: folder.id, sortOrder: Int32(idx))
        }
        do {
            try await dc.chatFolderService.reorder(orders)
            folders = try await dc.chatFolderService.refresh()
        } catch {
            folders = previous
            errorMessage = String(localized: "settings.chat_folders.error.reorder")
        }
    }
}
