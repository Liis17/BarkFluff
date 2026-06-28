//
//  ChatFoldersListViewModel.swift
//  Barkfluff (iOS)
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

    func delete(at offsets: IndexSet) async {
        guard let dc = dependencyContainer else { return }
        let toDelete = offsets.map { folders[$0] }
        let previous = folders
        folders.remove(atOffsets: offsets)
        for folder in toDelete {
            do {
                try await dc.chatFolderService.delete(folderID: folder.id)
            } catch {
                folders = previous
                errorMessage = String(localized: "settings.chat_folders.error.delete")
                return
            }
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
