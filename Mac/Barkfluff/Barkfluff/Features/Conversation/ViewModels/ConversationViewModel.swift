//
//  ConversationViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана переписки
//

import SwiftUI
import Observation
import BFCore

@Observable
final class ConversationViewModel {
    // MARK: - Properties

    let chat: Chat
    var messages: [Message] = []
    var isLoading = false
    var isLoadingMore = false
    var errorMessage: String?
    var hasMoreMessages = true

    private let messageService: MessageServiceProtocol
    private let updatesService: UpdatesServiceProtocol
    let currentUserID: Int64

    // Task management для стримов
    private var newMessagesTask: Task<Void, Never>?
    private var readEventsTask: Task<Void, Never>?

    // Сгруппированные элементы для отображения
    var listItems: [MessageListItem] {
        MessageGrouper.groupMessages(messages, currentUserID: currentUserID)
    }

    // MARK: - Init

    init(
        chat: Chat,
        messageService: MessageServiceProtocol,
        updatesService: UpdatesServiceProtocol,
        currentUserID: Int64
    ) {
        self.chat = chat
        self.messageService = messageService
        self.updatesService = updatesService
        self.currentUserID = currentUserID
    }

    // MARK: - Loading

    /// Загрузить сообщения чата
    func loadMessages() async {
        guard !isLoading else { return }

        isLoading = true
        errorMessage = nil

        do {
            let loadedMessages = try await messageService.listMessages(
                chatID: chat.id,
                fromMessageID: 0,
                offsetBefore: PaginationHelper.defaultMessagesPageSize,
                offsetAfter: 0
            )

            messages = loadedMessages.sorted { $0.sentAt < $1.sentAt }
            hasMoreMessages = loadedMessages.count >= PaginationHelper.defaultMessagesPageSize

            // Пометить как прочитанные
            await markVisibleMessagesAsRead()
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    /// Загрузить старые сообщения (пагинация)
    func loadMoreMessages() async {
        guard !isLoadingMore, !isLoading, hasMoreMessages, let firstMessage = messages.first else { return }

        isLoadingMore = true

        do {
            let olderMessages = try await messageService.listMessages(
                chatID: chat.id,
                fromMessageID: firstMessage.id,
                offsetBefore: PaginationHelper.defaultMessagesPageSize,
                offsetAfter: 0
            )

            if olderMessages.isEmpty {
                hasMoreMessages = false
            } else {
                // Добавляем старые сообщения в начало
                messages.insert(contentsOf: olderMessages.sorted { $0.sentAt < $1.sentAt }, at: 0)
                hasMoreMessages = olderMessages.count >= PaginationHelper.defaultMessagesPageSize
            }
        } catch {
            // Silently fail for pagination
        }

        isLoadingMore = false
    }

    // MARK: - Sending

    /// Отправить сообщение
    func sendMessage(text: String, fileIDs: [String] = []) async {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !fileIDs.isEmpty else { return }

        do {
            let message = try await messageService.sendMessage(
                chatID: chat.id,
                userID: nil,
                text: text,
                fileIDs: fileIDs
            )
            messages.append(message)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Real-time Updates

    /// Начать прослушивание обновлений
    func startListeningForUpdates() async {
        await updatesService.start()

        // Слушаем новые сообщения
        newMessagesTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getNewMessagesStream()
            for await event in stream {
                await MainActor.run {
                    self.handleNewMessage(event)
                }
            }
        }

        // Слушаем события прочтения
        readEventsTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getReadEventsStream()
            for await event in stream {
                await MainActor.run {
                    self.handleMessageRead(event)
                }
            }
        }
    }

    /// Остановить прослушивание обновлений
    func stopListeningForUpdates() {
        newMessagesTask?.cancel()
        readEventsTask?.cancel()
        newMessagesTask = nil
        readEventsTask = nil
    }

    // MARK: - Event Handling

    private func handleNewMessage(_ event: NewMessageEvent) {
        // Проверяем, что сообщение для этого чата
        guard event.chatID == chat.id else { return }

        // Проверяем, нет ли уже такого сообщения
        guard !messages.contains(where: { $0.id == event.message.id }) else { return }

        // Добавляем сообщение
        messages.append(event.message)

        // Пометить как прочитанное, если чат открыт
        Task {
            await markMessageAsRead(event.message)
        }
    }

    private func handleMessageRead(_ event: MessageReadEvent) {
        // Проверяем, что событие для этого чата
        guard event.chatID == chat.id else { return }

        // Обновляем статус прочтения
        if let index = messages.firstIndex(where: { $0.id == event.messageID }) {
            messages[index].readBy = event.readBy
        }
    }

    // MARK: - Read Status

    private func markVisibleMessagesAsRead() async {
        // Помечаем последние сообщения как прочитанные
        let unreadMessages = messages.suffix(10).filter { !$0.readBy.contains(currentUserID) }
        guard !unreadMessages.isEmpty else { return }

        do {
            try await messageService.markAsRead(messageIDs: unreadMessages.map { $0.id })
        } catch {
            // Silently fail
        }
    }

    private func markMessageAsRead(_ message: Message) async {
        guard !message.readBy.contains(currentUserID) else { return }

        do {
            try await messageService.markAsRead(messageIDs: [message.id])
        } catch {
            // Silently fail
        }
    }

}
