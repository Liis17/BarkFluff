//
//  GroupChatViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel для создания группового чата.
//

import SwiftUI
import Observation
import BFCore

@Observable
final class GroupChatViewModel {

    // MARK: - State

    var title: String = ""
    var selectedUserIDs: Set<Int64> = []
    /// Снимок выбранных пользователей (для отображения «чипсов» сверху).
    var selectedUsers: [User] = []
    var searchQuery: String = ""
    var searchResults: [User] = []
    var isLoading: Bool = false
    var isCreating: Bool = false
    var errorMessage: String?

    // MARK: - Dependencies

    private let chatService: ChatServiceProtocol
    private let userService: UserServiceProtocol
    private var searchTask: Task<Void, Never>?

    // MARK: - Init

    init(chatService: ChatServiceProtocol, userService: UserServiceProtocol) {
        self.chatService = chatService
        self.userService = userService
    }

    // MARK: - Computed

    var canCreate: Bool {
        !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !selectedUserIDs.isEmpty
            && !isCreating
    }

    // MARK: - Actions

    /// Поиск с debounce 300мс. Минимальная длина — 3 символа.
    func searchUsers() {
        searchTask?.cancel()

        let trimmed = searchQuery.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            searchResults = []
            isLoading = false
            return
        }

        guard trimmed.count >= 3 else {
            searchResults = []
            isLoading = false
            return
        }

        isLoading = true
        let query = trimmed
        let service = userService

        searchTask = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(300))
            guard !Task.isCancelled else { return }
            guard let self else { return }

            do {
                let result = try await service.searchUsers(query: query, offset: 0, size: 20)
                guard !Task.isCancelled else { return }
                self.searchResults = result.items
            } catch {
                guard !Task.isCancelled else { return }
                self.errorMessage = error.localizedDescription
                self.searchResults = []
            }
            self.isLoading = false
        }
    }

    func toggleUserSelection(_ user: User) {
        if selectedUserIDs.contains(user.id) {
            selectedUserIDs.remove(user.id)
            selectedUsers.removeAll { $0.id == user.id }
        } else {
            selectedUserIDs.insert(user.id)
            selectedUsers.append(user)
        }
    }

    func createGroupChat() async -> Chat? {
        guard canCreate else { return nil }
        isCreating = true
        errorMessage = nil
        defer { isCreating = false }

        do {
            return try await chatService.createGroupChat(
                userIDs: Array(selectedUserIDs),
                title: title.trimmingCharacters(in: .whitespacesAndNewlines),
                pictureFileID: nil
            )
        } catch {
            errorMessage = error.localizedDescription
            return nil
        }
    }
}
