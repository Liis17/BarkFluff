//
//  ForwardChatPickerViewModel.swift
//  Barkfluff
//
//  ViewModel для модального окна выбора чата при пересылке сообщения
//

import SwiftUI
import Observation
import BFCore

@Observable
@MainActor
final class ForwardChatPickerViewModel {
    /// Полный список чатов (snapshot из ChatListViewModel)
    let allChats: [Chat]
    var searchText: String = ""
    var isSending: Bool = false
    var errorMessage: String?

    private let messageService: MessageServiceProtocol
    private let messageID: Int64
    private let sourceChatID: String

    init(
        allChats: [Chat],
        messageService: MessageServiceProtocol,
        messageID: Int64,
        sourceChatID: String
    ) {
        self.allChats = allChats
        self.messageService = messageService
        self.messageID = messageID
        self.sourceChatID = sourceChatID
    }

    /// Чаты для отображения. Источник пересылки исключаем — для него работает «Ответить».
    var filteredChats: [Chat] {
        let candidates = allChats.filter { $0.id != sourceChatID }
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !query.isEmpty else { return candidates }
        return candidates.filter { $0.title.lowercased().contains(query) }
    }

    /// Переслать сообщение в целевой чат. Возвращает результат успеха для UI.
    func forward(to targetChat: Chat) async -> Bool {
        isSending = true
        errorMessage = nil
        defer { isSending = false }

        do {
            _ = try await messageService.sendMessage(
                chatID: targetChat.id,
                userID: nil,
                text: "",
                fileIDs: [],
                forwardedMessageID: messageID
            )
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }
}
