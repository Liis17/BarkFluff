//
//  GroupChatViewModel.swift
//  Barkfluff
//
//  ViewModel для группового чата
//

import SwiftUI
import Observation
import BFCore

@Observable
final class GroupChatViewModel {

    // MARK: - State

    var title: String = ""
    var selectedUserIDs: Set<Int64> = []
    var searchQuery: String = ""
    var searchResults: [User] = []
    var isLoading: Bool = false
    var errorMessage: String?

    // MARK: - Dependencies

    private let chatService: ChatServiceProtocol
    private let userService: UserServiceProtocol

    // MARK: - Init

    init(chatService: ChatServiceProtocol, userService: UserServiceProtocol) {
        self.chatService = chatService
        self.userService = userService
    }

    // MARK: - Actions

    func searchUsers() async {
        guard !searchQuery.isEmpty else {
            searchResults = []
            return
        }

        isLoading = true
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
        }
    }

    func toggleUserSelection(_ userID: Int64) {
        if selectedUserIDs.contains(userID) {
            selectedUserIDs.remove(userID)
        } else {
            selectedUserIDs.insert(userID)
        }
    }

    func createGroupChat() async throws -> Chat {
        guard !title.isEmpty, !selectedUserIDs.isEmpty else {
            throw BFError.unknown(message: "Название и участники обязательны")
        }

        return try await chatService.createGroupChat(
            userIDs: Array(selectedUserIDs),
            title: title,
            pictureFileID: nil
        )
    }
}
