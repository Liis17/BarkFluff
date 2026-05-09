//
//  UserSearchViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel для поиска пользователей
//

import SwiftUI
import Observation
import BFCore

@Observable
final class UserSearchViewModel {

    // MARK: - State

    var searchQuery: String = ""
    var searchResults: [User] = []
    var isLoading: Bool = false
    var errorMessage: String?

    // MARK: - Dependencies

    private let userService: UserServiceProtocol
    private var searchTask: Task<Void, Never>?

    // MARK: - Init

    init(userService: UserServiceProtocol) {
        self.userService = userService
    }

    // MARK: - Actions

    /// Поиск с debounce 300мс. Минимальная длина — 3 символа.
    func search() {
        searchTask?.cancel()

        let trimmed = searchQuery.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            searchResults = []
            isLoading = false
            errorMessage = nil
            return
        }

        guard trimmed.count >= 3 else {
            searchResults = []
            isLoading = false
            return
        }

        isLoading = true
        errorMessage = nil
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

    func clearSearch() {
        searchTask?.cancel()
        searchQuery = ""
        searchResults = []
        errorMessage = nil
    }
}
