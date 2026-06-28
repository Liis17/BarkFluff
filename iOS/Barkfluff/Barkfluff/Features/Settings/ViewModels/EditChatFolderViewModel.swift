//
//  EditChatFolderViewModel.swift
//  Barkfluff (iOS)
//

import SwiftUI
import Observation
import BFCore

@MainActor
@Observable
final class EditChatFolderViewModel {

    var name: String = ""
    var icon: String = ""
    var selectedChatIDs: Set<String> = []

    var isSaving: Bool = false
    var errorMessage: String?

    /// nil — создание новой папки, иначе редактирование.
    let folderID: String?

    weak var dependencyContainer: DependencyContainer?

    init(folder: ChatFolder? = nil) {
        self.folderID = folder?.id
        self.name = folder?.name ?? ""
        self.icon = folder?.icon ?? ""
        self.selectedChatIDs = Set(folder?.chatIDs ?? [])
    }

    var isCreating: Bool { folderID == nil }

    var trimmedName: String {
        name.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    var isNameValid: Bool {
        let n = trimmedName.count
        return n >= 1 && n <= 64
    }

    /// Возвращает true при успешном сохранении.
    func save() async -> Bool {
        guard let dc = dependencyContainer else { return false }
        guard isNameValid else {
            errorMessage = String(localized: "settings.chat_folders.error.name_length")
            return false
        }

        isSaving = true
        errorMessage = nil
        defer { isSaving = false }

        do {
            if let id = folderID {
                _ = try await dc.chatFolderService.update(
                    folderID: id,
                    name: trimmedName,
                    icon: icon,
                    chatIDs: Array(selectedChatIDs)
                )
            } else {
                let created = try await dc.chatFolderService.create(name: trimmedName, icon: icon)
                if !selectedChatIDs.isEmpty {
                    _ = try await dc.chatFolderService.update(
                        folderID: created.id,
                        name: nil,
                        icon: nil,
                        chatIDs: Array(selectedChatIDs)
                    )
                }
            }
            return true
        } catch {
            errorMessage = String(localized: "settings.chat_folders.error.save")
            return false
        }
    }

    /// Возвращает true при успешном удалении.
    func delete() async -> Bool {
        guard let dc = dependencyContainer, let id = folderID else { return false }

        isSaving = true
        errorMessage = nil
        defer { isSaving = false }

        do {
            try await dc.chatFolderService.delete(folderID: id)
            return true
        } catch {
            errorMessage = String(localized: "settings.chat_folders.error.delete")
            return false
        }
    }
}
