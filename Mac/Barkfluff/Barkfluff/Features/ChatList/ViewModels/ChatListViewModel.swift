//
//  ChatListViewModel.swift
//  Barkfluff
//
//  ViewModel для списка чатов
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

    /// Статусы онлайн для отображаемых чатов (userID → status)
    var onlineStatuses: [Int64: OnlineStatus] = [:]

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
    private var onlineStatusTask: Task<Void, Never>?

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

            // Запускаем отслеживание онлайн-статусов для загруженных чатов
            await startOnlineStatusTracking()
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

            // Добавляем новых пользователей к отслеживаемым (инкрементально)
            let newUserIDs = newChats.compactMap { chat -> Int64? in
                guard !chat.isGroupChat else { return nil }
                return chat.otherUserID(excluding: currentUserID)
            }
            await addToOnlineStatusTracking(newUserIDs)
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
        onlineStatusTask?.cancel()
        newMessagesTask = nil
        readEventsTask = nil
        connectionEventsTask = nil
        onlineStatusTask = nil
    }

    // MARK: - Online Status

    /// Начать отслеживание онлайн-статусов для всех DM чатов
    private func startOnlineStatusTracking() async {
        // Собираем ID пользователей из DM чатов
        let userIDs = collectUserIDsFromChats()

        // Запускаем сервис с начальным списком
        await onlineStatusService.start(initialUserIDs: userIDs)

        // Подписываемся на события изменения статусов
        startListeningForOnlineStatusEvents()

        // Заполняем начальные статусы из кеша
        // (события из start() могут быть потеряны, т.к. continuation ещё не создан)
        for userID in userIDs {
            let status = await onlineStatusService.getStatus(for: userID)
            onlineStatuses[userID] = status
        }
    }

    /// Подписаться на события изменения статусов
    private func startListeningForOnlineStatusEvents() {
        onlineStatusTask = Task { [weak self] in
            let stream = await self?.onlineStatusService.getStatusEventsStream()
            guard let stream else { return }

            for await event in stream {
                await MainActor.run {
                    self?.onlineStatuses[event.userID] = event.status
                }
            }
        }
    }

    /// Добавить новых пользователей к отслеживаемым (инкрементально)
    private func addToOnlineStatusTracking(_ newUserIDs: [Int64]) async {
        guard !newUserIDs.isEmpty else { return }

        // Получаем текущий список отслеживаемых
        let currentTracked = await onlineStatusService.getTrackedUserIDs()

        // Фильтруем только новых (которых ещё нет в отслеживаемых)
        let usersToAdd = newUserIDs.filter { !currentTracked.contains($0) }

        guard !usersToAdd.isEmpty else { return }

        // Добавляем инкрементально
        await onlineStatusService.addToTracking(usersToAdd)
    }

    /// Собрать ID пользователей из DM чатов
    private func collectUserIDsFromChats() -> [Int64] {
        allChats.compactMap { chat -> Int64? in
            guard !chat.isGroupChat else { return nil }
            return chat.otherUserID(excluding: currentUserID)
        }
    }

    /// Получить статус для конкретного чата (для DM — статус собеседника)
    func onlineStatus(for chat: Chat) -> OnlineStatus {
        guard !chat.isGroupChat, let otherUserID = chat.otherUserID(excluding: currentUserID) else {
            return .unknown
        }
        return onlineStatuses[otherUserID] ?? .unknown
    }

    // MARK: - Event Handling

    private func handleNewMessage(_ event: BFCore.NewMessageEvent) {
        if let index = allChats.firstIndex(where: { $0.id == event.chatID }) {
            allChats[index].lastMessage = event.message
            // Увеличиваем unreadCount только если сообщение НЕ от текущего пользователя
            // И чат НЕ открыт сейчас (активный чат)
            if event.message.senderID != currentUserID && !isChatActive(event.chatID) {
                allChats[index].unreadCount += 1
            }
            // Move to top
            let chat = allChats.remove(at: index)
            allChats.insert(chat, at: 0)
            applyFilter()
        } else {
            // Новый чат — добавляем отправителя в отслеживаемые
            let senderID = event.message.senderID
            if senderID != currentUserID {
                Task { await self.addToOnlineStatusTracking([senderID]) }
            }
            // И перезагружаем список чатов
            Task { await loadChats() }
        }
    }

    /// Проверить, открыт ли чат сейчас (активный)
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
            // If current user read, zero unread
            if event.readBy.contains(currentUserID) {
                allChats[index].unreadCount = 0
            }
            applyFilter()
        }
    }

    // MARK: - Mark as Read

    /// Локально пометить чат как прочитанный (вызывается при открытии чата)
    func markChatAsReadLocally(chatID: String) {
        if let index = allChats.firstIndex(where: { $0.id == chatID }) {
            allChats[index].unreadCount = 0
            applyFilter()
        }
    }

    /// Обновить последнее сообщение в чате (вызывается при отправке сообщения)
    func updateLastMessage(chatID: String, message: BFCore.Message) {
        if let index = allChats.firstIndex(where: { $0.id == chatID }) {
            allChats[index].lastMessage = message
            // Переместить чат наверх
            let chat = allChats.remove(at: index)
            allChats.insert(chat, at: 0)
            applyFilter()
        }
    }

    // MARK: - Open Conversation

    /// Открыть диалог с пользователем из результатов поиска
    func openConversation(with user: User, coordinator: AppCoordinator) async {
        // 1. Проверяем локальный список чатов
        if let existingChat = allChats.first(where: { chat in
            !chat.isGroupChat && chat.otherUserID(excluding: currentUserID) == user.id
        }) {
            coordinator.selectedChat = existingChat
            searchText = ""
            searchResults = []
            applyFilter()
            return
        }

        // 2. Проверяем на сервере (чат может быть не загружен из-за пагинации)
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
                coordinator.selectedChat = chat
                searchText = ""
                searchResults = []
                applyFilter()
                return
            }
        } catch {
            // Ошибка сервера — переходим к созданию нового диалога
        }

        // 3. Чата нет — создаём placeholder для нового диалога
        let placeholderChat = Chat.newConversationPlaceholder(with: user)
        coordinator.selectedChat = placeholderChat
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
