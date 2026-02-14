//
//  UserSearchViewModel.swift
//  Barkfluff
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

    // MARK: - Init

    init(userService: UserServiceProtocol) {
        self.userService = userService
    }

    // MARK: - Actions

    func search() async {
        guard !searchQuery.isEmpty else {
            searchResults = []
            return
        }

        isLoading = true
        errorMessage = nil
        defer { isLoading = false }

        do {
            let result = try await userService.searchUsers(
                query: searchQuery,
                offset: 0,
                size: 20
            )
            searchResults = result.items
        } catch {
            errorMessage = error.localizedDescription
            searchResults = []
        }
    }

    func clearSearch() {
        searchQuery = ""
        searchResults = []
        errorMessage = nil
    }
}
