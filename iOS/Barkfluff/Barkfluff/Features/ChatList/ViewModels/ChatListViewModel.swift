//
//  ChatListViewModel.swift
//  Barkfluff
//
//  ViewModel для списка чатов (iOS версия)
//

import SwiftUI
import Observation
import BFCore

@Observable
final class ChatListViewModel {
    var chats: [Chat] = []
    var isLoading = false
    var errorMessage: String?
    var searchText = ""
    var searchResults: [User] = []
    var isSearching = false

    /// Замыкание для проверки, открыт ли чат сейчас (активный)
    var isActiveChatChecker: ((String) -> Bool)?

    private var allChats: [Chat] = []
    private var totalCount: Int32 = 0
    private var currentUserID: Int64 = 0

    private let chatService: ChatServiceProtocol
    private let userService: UserServiceProtocol
    private let updatesService: UpdatesServiceProtocol
    private let onlineStatusService: OnlineStatusServiceProtocol

    private var newMessagesTask: Task<Void, Never>?
    private var readEventsTask: Task<Void, Never>?
    private var connectionEventsTask: Task<Void, Never>?
    private var searchTask: Task<Void, Never>?

    init(
        chatService: ChatServiceProtocol,
        userService: UserServiceProtocol,
        updatesService: UpdatesServiceProtocol,
        onlineStatusService: OnlineStatusServiceProtocol,
        currentUserID: Int64
    ) {
        self.chatService = chatService
        self.userService = userService
        self.updatesService = updatesService
        self.onlineStatusService = onlineStatusService
        self.currentUserID = currentUserID
    }

    // MARK: - Loading

    func loadChats() async {
        isLoading = true
        errorMessage = nil

        do {
            let result = try await chatService.listChats(offset: 0, size: PaginationHelper.defaultChatsPageSize)
            allChats = result.items
            totalCount = result.totalCount
            applyFilter()

            // Прогрев кеша онлайн-статусов для DM-чатов. Ref-counted tracking
            // делают сами ChatRowView через .task(id:) когда строки появляются в List.
            let userIDs = collectUserIDsFromChats()
            await onlineStatusService.start(initialUserIDs: userIDs)
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    func loadMoreChats() async {
        guard !isLoading, Int32(allChats.count) < totalCount else { return }

        let offset = PaginationHelper.calculateOffset(currentCount: allChats.count, pageSize: PaginationHelper.defaultChatsPageSize)

        do {
            let result = try await chatService.listChats(offset: offset, size: PaginationHelper.defaultChatsPageSize)
            let newChats = result.items
            allChats.append(contentsOf: newChats)
            totalCount = result.totalCount
            applyFilter()

            // Tracking новых юзеров делают ChatRowView через .task(id:) когда
            // их строки появляются в SwiftUI List — отдельная регистрация не нужна.
        } catch {
            // Silently fail for pagination
        }
    }

    func refresh() async {
        await loadChats()
    }

    // MARK: - Real-time Updates

    func startListeningForUpdates() async {
        await updatesService.start()

        newMessagesTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getNewMessagesStream()
            for await event in stream {
                await MainActor.run {
                    self.handleNewMessage(event)
                }
            }
        }

        readEventsTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getReadEventsStream()
            for await event in stream {
                await MainActor.run {
                    self.handleMessageRead(event)
                }
            }
        }

        connectionEventsTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getConnectionEventsStream()
            for await event in stream {
                await MainActor.run {
                    self.handleConnectionEvent(event)
                }
            }
        }
    }

    func stopListeningForUpdates() {
        newMessagesTask?.cancel()
        readEventsTask?.cancel()
        connectionEventsTask?.cancel()
        newMessagesTask = nil
        readEventsTask = nil
        connectionEventsTask = nil
    }

    // MARK: - Online Status

    /// Собрать ID пользователей из DM чатов (для warmup кеша при старте).
    private func collectUserIDsFromChats() -> [Int64] {
        allChats.compactMap { chat -> Int64? in
            guard !chat.isGroupChat else { return nil }
            return chat.otherUserID(excluding: currentUserID)
        }
    }

    // MARK: - Event Handling

    private func handleNewMessage(_ event: BFCore.NewMessageEvent) {
        if let index = allChats.firstIndex(where: { $0.id == event.chatID }) {
            allChats[index].lastMessage = event.message
            if event.message.senderID != currentUserID && !isChatActive(event.chatID) {
                allChats[index].unreadCount += 1
            }
            let chat = allChats.remove(at: index)
            allChats.insert(chat, at: 0)
            applyFilter()
        } else {
            // Новый чат — перезагружаем список. Tracking нового собеседника
            // произойдёт автоматически когда его row появится в List.
            Task { await loadChats() }
        }
    }

    private func isChatActive(_ chatID: String) -> Bool {
        return isActiveChatChecker?(chatID) ?? false
    }

    private func handleConnectionEvent(_ event: BFCore.UpdatesConnectionEvent) {
        switch event {
        case .reconnected:
            Task { await loadChats() }
        case .connectionLost:
            break
        }
    }

    private func handleMessageRead(_ event: BFCore.MessageReadEvent) {
        if let index = allChats.firstIndex(where: { $0.id == event.chatID }) {
            if var msg = allChats[index].lastMessage, msg.id == event.messageID {
                msg.readBy = event.readBy
                allChats[index].lastMessage = msg
            }
            if event.readBy.contains(currentUserID) {
                allChats[index].unreadCount = 0
            }
            applyFilter()
        }
    }

    // MARK: - Mark as Read

    func markChatAsReadLocally(chatID: String) {
        if let index = allChats.firstIndex(where: { $0.id == chatID }) {
            allChats[index].unreadCount = 0
            applyFilter()
        }
    }

    func updateLastMessage(chatID: String, message: BFCore.Message) {
        if let index = allChats.firstIndex(where: { $0.id == chatID }) {
            allChats[index].lastMessage = message
            let chat = allChats.remove(at: index)
            allChats.insert(chat, at: 0)
            applyFilter()
        }
    }

    // MARK: - Open Conversation

    func openConversation(with user: User, coordinator: AppCoordinator) async {
        if let existingChat = allChats.first(where: { chat in
            !chat.isGroupChat && chat.otherUserID(excluding: currentUserID) == user.id
        }) {
            coordinator.openChat(existingChat)
            searchText = ""
            searchResults = []
            applyFilter()
            return
        }

        do {
            if let chatID = try await chatService.getPersonChatId(userID: user.id) {
                let chat = Chat(
                    id: chatID,
                    title: user.displayName,
                    pictureURL: user.profilePicturePreviewURL,
                    isGroupChat: false,
                    members: [
                        ChatMember(
                            userID: user.id,
                            username: user.username,
                            firstName: user.firstName,
                            lastName: user.lastName,
                            profilePictureURL: user.profilePicturePreviewURL,
                            role: .member
                        )
                    ]
                )
                coordinator.openChat(chat)
                searchText = ""
                searchResults = []
                applyFilter()
                return
            }
        } catch {
            // Ошибка сервера — переходим к созданию нового диалога
        }

        let placeholderChat = Chat.newConversationPlaceholder(with: user)
        coordinator.openChat(placeholderChat)
        searchText = ""
        searchResults = []
        applyFilter()
    }

    // MARK: - Search

    func onSearchTextChanged() {
        searchTask?.cancel()

        if searchText.isEmpty {
            searchResults = []
            isSearching = false
            applyFilter()
            return
        }

        applyFilter()

        guard searchText.count >= 3 else {
            searchResults = []
            isSearching = false
            return
        }

        isSearching = true
        let query = searchText
        searchTask = Task { [weak self] in
            guard let self else { return }
            try? await Task.sleep(for: .milliseconds(300))
            guard !Task.isCancelled else { return }

            do {
                let result = try await self.userService.searchUsers(
                    query: query,
                    offset: 0,
                    size: PaginationHelper.defaultSearchPageSize
                )
                guard !Task.isCancelled else { return }
                await MainActor.run {
                    self.searchResults = result.items
                    self.isSearching = false
                }
            } catch {
                guard !Task.isCancelled else { return }
                await MainActor.run {
                    self.isSearching = false
                }
            }
        }
    }

    // MARK: - Private

    private func applyFilter() {
        if searchText.isEmpty {
            chats = allChats
        } else {
            chats = allChats.filter { chat in
                chat.title.localizedCaseInsensitiveContains(searchText)
            }
        }
    }
}
