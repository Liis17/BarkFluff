//
//  ForwardChatPickerViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel для модального окна выбора чатов при пересылке сообщения.
//  Поддерживает множественный выбор и опциональный комментарий.
//

import SwiftUI
import Observation
import BFCore

/// Результат пакетной отправки пересылки.
struct ForwardResult: Sendable {
    let success: Int
    let failure: Int
    var total: Int { success + failure }
}

@Observable
@MainActor
final class ForwardChatPickerViewModel {
    /// Полный список чатов (snapshot из ChatListViewModel).
    let allChats: [Chat]
    var searchText: String = ""
    var commentText: String = ""
    var selectedChatIDs: Set<String> = []
    var isSending: Bool = false
    var errorMessage: String?

    private let messageService: MessageServiceProtocol
    private let messageID: Int64

    init(
        allChats: [Chat],
        messageService: MessageServiceProtocol,
        messageID: Int64
    ) {
        self.allChats = allChats
        self.messageService = messageService
        self.messageID = messageID
    }

    /// Отфильтрованный по поиску список (источник пересылки не исключаем —
    /// пользователь имеет право переслать обратно в тот же чат).
    var filteredChats: [Chat] {
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !query.isEmpty else { return allChats }
        return allChats.filter { $0.title.lowercased().contains(query) }
    }

    /// Переключить выбор чата.
    func toggle(chatID: String) {
        if selectedChatIDs.contains(chatID) {
            selectedChatIDs.remove(chatID)
        } else {
            selectedChatIDs.insert(chatID)
        }
    }

    var canSend: Bool {
        !selectedChatIDs.isEmpty && !isSending
    }

    /// Параллельно переслать сообщение во все выбранные чаты.
    func forwardToSelected() async -> ForwardResult {
        let chatIDs = Array(selectedChatIDs)
        guard !chatIDs.isEmpty else { return ForwardResult(success: 0, failure: 0) }

        isSending = true
        errorMessage = nil
        defer { isSending = false }

        let trimmedComment = commentText.trimmingCharacters(in: .whitespacesAndNewlines)
        let messageID = self.messageID
        let service = self.messageService

        let successes: Int = await withTaskGroup(of: Bool.self) { group in
            for id in chatIDs {
                group.addTask {
                    do {
                        _ = try await service.sendMessage(
                            chatID: id,
                            userID: nil,
                            text: trimmedComment,
                            fileIDs: [],
                            forwardedMessageID: messageID
                        )
                        return true
                    } catch {
                        return false
                    }
                }
            }
            var ok = 0
            for await result in group where result { ok += 1 }
            return ok
        }

        let failures = chatIDs.count - successes
        if successes == 0 && failures > 0 {
            errorMessage = String(localized: "conversation.errors.forward_failed")
        }
        return ForwardResult(success: successes, failure: failures)
    }
}
