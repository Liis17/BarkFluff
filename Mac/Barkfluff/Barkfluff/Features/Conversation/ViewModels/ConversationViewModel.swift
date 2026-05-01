//
//  ConversationViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана переписки
//

import SwiftUI
import Observation
import BFCore
import AppKit

@Observable
final class ConversationViewModel {
    // MARK: - Properties

    let chat: Chat
    var messages: [Message] = []
    var isLoading = false
    var isLoadingMore = false
    var errorMessage: String?
    var hasMoreMessages = true

    /// ID первого непрочитанного сообщения (для скролла при открытии чата)
    var firstUnreadMessageID: Int64?

    /// Онлайн-статус собеседника (только для DM)
    var otherUserOnlineStatus: OnlineStatus = .unknown

    /// Состояние отправки вложений
    var isSendingAttachments = false

    /// Прогресс загрузки каждого файла (attachmentID → 0.0...1.0)
    var uploadProgress: [UUID: Double] = [:]

    /// Ошибка загрузки файла
    var uploadError: String?

    /// Замыкание для уведомления об отправке сообщения (обновление lastMessage в списке чатов)
    var onMessageSent: ((Message) -> Void)?

    /// Замыкание для уведомления о разрешении нового диалога (получен реальный chatID)
    var onChatResolved: ((Chat) -> Void)?

    /// ID подтверждённых сообщений (для дедупликации с real-time стримом)
    private var confirmedMessageIDs: Set<Int64> = []

    /// Счётчик отрицательных ID для pending-сообщений
    private static var pendingIDCounter: Int64 = 0

    private static func nextPendingID() -> Int64 {
        pendingIDCounter -= 1
        return pendingIDCounter
    }

    private let messageService: MessageServiceProtocol
    private let updatesService: UpdatesServiceProtocol
    private let onlineStatusService: OnlineStatusServiceProtocol
    private let fileService: FileServiceProtocol
    private let chatService: ChatServiceProtocol?
    let currentUserID: Int64

    /// ID пользователя для нового диалога (когда чат ещё не создан на сервере)
    private let targetUserID: Int64?

    /// Является ли это новым диалогом (placeholder-чат)
    var isNewConversation: Bool {
        chat.isNewConversation
    }

    // Task management для стримов
    private var newMessagesTask: Task<Void, Never>?
    private var readEventsTask: Task<Void, Never>?
    private var onlineStatusTask: Task<Void, Never>?

    // Сгруппированные элементы для отображения
    var listItems: [MessageListItem] {
        MessageGrouper.groupMessages(messages, currentUserID: currentUserID)
    }

    // MARK: - Init

    init(
        chat: Chat,
        messageService: MessageServiceProtocol,
        updatesService: UpdatesServiceProtocol,
        onlineStatusService: OnlineStatusServiceProtocol,
        fileService: FileServiceProtocol,
        currentUserID: Int64,
        chatService: ChatServiceProtocol? = nil
    ) {
        self.chat = chat
        self.messageService = messageService
        self.updatesService = updatesService
        self.onlineStatusService = onlineStatusService
        self.fileService = fileService
        self.currentUserID = currentUserID
        self.chatService = chatService
        self.targetUserID = chat.newConversationUserID
    }

    // MARK: - Loading

    /// Загрузить сообщения чата
    func loadMessages() async {
        // Для нового диалога нечего загружать
        guard !isNewConversation else {
            isLoading = false
            hasMoreMessages = false
            return
        }

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

            // Убираем дубликаты по ID
            var seenIDs = Set<Int64>()
            let uniqueMessages = loadedMessages.filter { seenIDs.insert($0.id).inserted }
            messages = uniqueMessages.sorted { $0.sentAt < $1.sentAt }
            hasMoreMessages = loadedMessages.count >= PaginationHelper.defaultMessagesPageSize

            // Определяем первое непрочитанное сообщение (до пометки прочитанными)
            firstUnreadMessageID = messages.first(where: {
                $0.senderID != currentUserID && !$0.readBy.contains(currentUserID)
            })?.id

            // Если первое непрочитанное близко к началу списка — подгружаем старые для контекста
            if let unreadID = firstUnreadMessageID,
               let unreadIndex = messages.firstIndex(where: { $0.id == unreadID }),
               unreadIndex < 5, hasMoreMessages {
                isLoading = false
                await loadMoreMessages()
            }

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
                // Добавляем старые сообщения в начало, исключая дубликаты
                let existingIDs = Set(messages.map { $0.id })
                let newMessages = olderMessages.filter { !existingIDs.contains($0.id) }
                messages.insert(contentsOf: newMessages.sorted { $0.sentAt < $1.sentAt }, at: 0)
                hasMoreMessages = olderMessages.count >= PaginationHelper.defaultMessagesPageSize
            }
        } catch {
            // Silently fail for pagination
        }

        isLoadingMore = false

        // Помечаем загруженные старые сообщения как прочитанные
        await markVisibleMessagesAsRead()
    }

    // MARK: - Sending

    /// Отправить сообщение
    func sendMessage(text: String, fileIDs: [String] = []) async {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !fileIDs.isEmpty else { return }

        // Для новых диалогов — старый flow без optimistic
        if isNewConversation, let userID = targetUserID {
            do {
                let message = try await messageService.sendMessage(
                    chatID: nil,
                    userID: userID,
                    text: text,
                    fileIDs: fileIDs
                )
                await resolveNewConversation(firstMessage: message)
            } catch {
                errorMessage = error.localizedDescription
            }
            return
        }

        // Optimistic: создаём pending-сообщение
        let localID = UUID().uuidString
        let pendingID = Self.nextPendingID()
        let trimmedText = text.trimmingCharacters(in: .whitespacesAndNewlines)

        let pendingMessage = Message(
            id: pendingID,
            chatID: chat.id,
            senderID: currentUserID,
            content: MessageContent(text: trimmedText),
            sentAt: Date(),
            sendingState: .sending,
            localID: localID
        )

        withAnimation(.spring(duration: 0.3)) {
            messages.append(pendingMessage)
        }

        do {
            let confirmed = try await messageService.sendMessage(
                chatID: chat.id,
                userID: nil,
                text: trimmedText,
                fileIDs: fileIDs
            )
            replacePendingWithConfirmed(localID: localID, confirmed: confirmed)
            onMessageSent?(confirmed)
            await markVisibleMessagesAsRead()
        } catch {
            updatePendingMessage(localID: localID) { msg in
                msg.sendingState = .failed(error.localizedDescription)
            }
        }
    }

    /// Отправка сообщения с вложениями
    func sendMessageWithAttachments(
        text: String,
        attachments: [SelectedAttachment]
    ) async {
        // Для новых диалогов — старый flow без optimistic
        if isNewConversation, let userID = targetUserID {
            guard !isSendingAttachments else { return }
            isSendingAttachments = true
            uploadError = nil
            uploadProgress = [:]

            do {
                var fileIDs: [String] = []
                for attachment in attachments {
                    uploadProgress[attachment.id] = 0.0
                    var data = try attachment.loadData()
                    uploadProgress[attachment.id] = 0.1
                    if attachment.uploadFileType == .messageAttachmentImage {
                        if let optimized = optimizeImage(data) { data = optimized }
                    }
                    let fileID = try await fileService.uploadFile(
                        data: data, fileName: attachment.displayName, fileType: attachment.uploadFileType
                    )
                    fileIDs.append(fileID)
                    uploadProgress[attachment.id] = 1.0
                }
                let trimmedText = text.trimmingCharacters(in: .whitespacesAndNewlines)
                let message = try await messageService.sendMessage(
                    chatID: nil, userID: userID, text: trimmedText, fileIDs: fileIDs
                )
                await resolveNewConversation(firstMessage: message)
                uploadProgress.removeAll()
            } catch {
                uploadError = error.localizedDescription
                uploadProgress.removeAll()
            }
            isSendingAttachments = false
            return
        }

        // Optimistic flow для существующих чатов
        let localID = UUID().uuidString
        let pendingID = Self.nextPendingID()
        let trimmedText = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let hasAttachments = !attachments.isEmpty

        // Создаём локальные превью вложений для немедленного отображения
        let localAttachments = hasAttachments
            ? Self.createLocalAttachments(from: attachments)
            : []

        let pendingMessage = Message(
            id: pendingID,
            chatID: chat.id,
            senderID: currentUserID,
            content: MessageContent(text: trimmedText, attachments: localAttachments),
            sentAt: Date(),
            sendingState: .sending,
            localID: localID,
            uploadProgress: hasAttachments ? 0.0 : nil
        )

        withAnimation(.spring(duration: 0.3)) {
            messages.append(pendingMessage)
        }

        do {
            // 1. Загрузить файлы с плавной симуляцией прогресса
            var fileIDs: [String] = []
            let totalAttachments = Double(attachments.count)

            for (index, attachment) in attachments.enumerated() {
                var data = try attachment.loadData()

                if attachment.uploadFileType == .messageAttachmentImage {
                    if let optimized = optimizeImage(data) { data = optimized }
                }

                let baseProgress = Double(index) / totalAttachments
                let targetProgress = Double(index + 1) / totalAttachments * 0.9 // 90% на загрузку файлов

                // Запускаем плавную симуляцию прогресса на время загрузки
                let progressTask = Task { @MainActor [weak self] in
                    guard let self else { return }
                    var current = baseProgress * 0.9 + 0.02
                    while !Task.isCancelled && current < targetProgress {
                        try? await Task.sleep(for: .milliseconds(150))
                        current += Double.random(in: 0.01...0.03)
                        current = min(current, targetProgress - 0.02)
                        self.updatePendingMessage(localID: localID) { msg in
                            msg.uploadProgress = current
                        }
                    }
                }

                let fileID = try await fileService.uploadFile(
                    data: data, fileName: attachment.displayName, fileType: attachment.uploadFileType
                )
                fileIDs.append(fileID)
                progressTask.cancel()

                updatePendingMessage(localID: localID) { msg in
                    msg.uploadProgress = targetProgress
                }
            }

            // 2. Отправить сообщение с fileIDs
            updatePendingMessage(localID: localID) { msg in
                msg.uploadProgress = 0.95
            }

            let confirmed = try await messageService.sendMessage(
                chatID: chat.id, userID: nil, text: trimmedText, fileIDs: fileIDs
            )

            // 3. Заменить pending на confirmed
            replacePendingWithConfirmed(localID: localID, confirmed: confirmed)
            onMessageSent?(confirmed)
            await markVisibleMessagesAsRead()

        } catch {
            updatePendingMessage(localID: localID) { msg in
                msg.sendingState = .failed(error.localizedDescription)
                msg.uploadProgress = nil
            }
        }
    }

    // MARK: - Local Attachment Previews

    /// Создать локальные MessageAttachment из SelectedAttachment для мгновенного отображения
    private static func createLocalAttachments(from attachments: [SelectedAttachment]) -> [MessageAttachment] {
        attachments.enumerated().compactMap { index, attachment in
            let localURL: String
            switch attachment {
            case .fileURL(let url, _, _):
                localURL = url.absoluteString
            case .imageData(let data, let fileName, _):
                let tempURL = FileManager.default.temporaryDirectory
                    .appendingPathComponent("pending_\(UUID().uuidString)_\(fileName)")
                try? data.write(to: tempURL)
                localURL = tempURL.absoluteString
            }

            let type: AttachmentType
            if attachment.isVideo {
                type = .video
            } else if attachment.fileExtension == "gif" {
                type = .gif
            } else if attachment.isMedia {
                type = .image
            } else {
                type = .document
            }

            return MessageAttachment(
                id: Int64(-(index + 1)),
                type: type,
                fileID: "",
                fileName: attachment.displayName,
                fileSize: attachment.fileSize ?? 0,
                previewURL: localURL
            )
        }
    }

    // MARK: - Pending Message Helpers

    /// Обновить pending-сообщение по localID
    private func updatePendingMessage(localID: String, update: (inout Message) -> Void) {
        if let index = messages.firstIndex(where: { $0.localID == localID }) {
            update(&messages[index])
        }
    }

    /// Заменить pending на confirmed, сохраняя localID для стабильной identity
    private func replacePendingWithConfirmed(localID: String, confirmed: Message) {
        confirmedMessageIDs.insert(confirmed.id)

        // Удалить дубликат, если стрим уже добавил это сообщение
        if let streamIndex = messages.firstIndex(where: { $0.id == confirmed.id && $0.localID == nil }) {
            messages.remove(at: streamIndex)
        }

        // Заменить pending in-place
        if let index = messages.firstIndex(where: { $0.localID == localID }) {
            var updatedMessage = confirmed
            updatedMessage.localID = localID  // Сохраняем localID для стабильной SwiftUI identity
            messages[index] = updatedMessage
        }
    }

    /// Повтор отправки неудавшегося сообщения (text-only)
    func retryFailedMessage(localID: String) {
        guard let index = messages.firstIndex(where: { $0.localID == localID }),
              case .failed = messages[index].sendingState else { return }

        let text = messages[index].content.text
        messages[index].sendingState = .sending

        Task {
            do {
                let confirmed = try await messageService.sendMessage(
                    chatID: chat.id, userID: nil, text: text, fileIDs: []
                )
                replacePendingWithConfirmed(localID: localID, confirmed: confirmed)
                onMessageSent?(confirmed)
                await markVisibleMessagesAsRead()
            } catch {
                updatePendingMessage(localID: localID) { msg in
                    msg.sendingState = .failed(error.localizedDescription)
                }
            }
        }
    }

    /// Удалить неотправленное сообщение
    func deleteFailedMessage(localID: String) {
        withAnimation(.spring(duration: 0.3)) {
            messages.removeAll { $0.localID == localID }
        }
    }

    // MARK: - New Conversation Resolution

    /// Разрешить новый диалог: получить реальный chatID и переключиться на него
    private func resolveNewConversation(firstMessage: Message) async {
        guard let userID = targetUserID, let chatService else { return }

        do {
            if let realChatID = try await chatService.getPersonChatId(userID: userID) {
                let resolvedChat = Chat(
                    id: realChatID,
                    title: chat.title,
                    pictureURL: chat.pictureURL,
                    isGroupChat: false,
                    lastMessage: firstMessage,
                    members: chat.members
                )
                await MainActor.run {
                    onChatResolved?(resolvedChat)
                }
            }
        } catch {
            errorMessage = "Не удалось получить информацию о чате"
        }
    }

    // MARK: - Image Optimization

    /// Оптимизация изображения перед отправкой
    /// - Ресайз до max 2048px по большей стороне
    /// - Сжатие JPEG с качеством 0.85
    /// - Возврат оптимизированных данных
    private func optimizeImage(_ data: Data) -> Data? {
        guard let image = NSImage(data: data) else { return nil }

        let maxDimension: CGFloat = 2048
        let size = image.size

        // Нужен ли ресайз?
        guard size.width > maxDimension || size.height > maxDimension else {
            // Просто пережимаем в JPEG
            return image.jpegData(compressionQuality: 0.85) ?? data
        }

        let ratio = min(maxDimension / size.width, maxDimension / size.height)
        let newSize = CGSize(width: size.width * ratio, height: size.height * ratio)

        let resizedImage = NSImage(size: newSize)
        resizedImage.lockFocus()
        image.draw(in: NSRect(origin: .zero, size: newSize),
                   from: NSRect(origin: .zero, size: size),
                   operation: .copy,
                   fraction: 1.0)
        resizedImage.unlockFocus()

        return resizedImage.jpegData(compressionQuality: 0.85) ?? data
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

        // Подписываемся на онлайн-статус собеседника (только для DM)
        await startListeningForOnlineStatus()
    }

    /// Остановить прослушивание обновлений
    func stopListeningForUpdates() {
        newMessagesTask?.cancel()
        readEventsTask?.cancel()
        onlineStatusTask?.cancel()
        newMessagesTask = nil
        readEventsTask = nil
        onlineStatusTask = nil
    }

    // MARK: - Online Status

    private func startListeningForOnlineStatus() async {
        guard !chat.isGroupChat, let otherUserID = chat.otherUserID(excluding: currentUserID) else { return }

        let status = await onlineStatusService.getStatus(for: otherUserID)
        await MainActor.run { self.otherUserOnlineStatus = status }

        let stream = await onlineStatusService.getStatusEventsStream()

        // Отменяем предыдущую задачу перед заменой — предотвращает утечку подписчиков
        onlineStatusTask?.cancel()
        onlineStatusTask = Task { [weak self] in
            for await event in stream {
                guard let self else { break }
                if event.userID == otherUserID {
                    await MainActor.run {
                        self.otherUserOnlineStatus = event.status
                    }
                }
            }
        }
    }

    // MARK: - Event Handling

    private func handleNewMessage(_ event: NewMessageEvent) {
        // Проверяем, что сообщение для этого чата
        guard event.chatID == chat.id else { return }

        // Дедупликация: send response уже обработал это сообщение
        if confirmedMessageIDs.contains(event.message.id) { return }

        // Проверяем, нет ли уже такого сообщения (по серверному ID)
        guard !messages.contains(where: { $0.id == event.message.id && $0.localID == nil }) else { return }

        // Своё сообщение из стрима: если есть pending — пропускаем,
        // replacePendingWithConfirmed обработает при получении ответа от sendMessage
        if event.message.senderID == currentUserID {
            let hasPending = messages.contains { $0.localID != nil && $0.senderID == currentUserID }
            if hasPending {
                return
            }
            // Своё сообщение с другого устройства — добавляем без анимации
            messages.append(event.message)
            return
        }

        // Чужие сообщения — добавляем с анимацией
        withAnimation(.spring(duration: 0.3)) {
            messages.append(event.message)
        }

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

    /// Публичный метод для повторного прочтения (вызывается при возврате фокуса в приложение)
    func markAllAsReadIfNeeded() async {
        await markVisibleMessagesAsRead()
    }

    private func markVisibleMessagesAsRead() async {
        // Помечаем ВСЕ непрочитанные сообщения как прочитанные (исключая pending)
        let unreadMessages = messages.filter { $0.id > 0 && !$0.readBy.contains(currentUserID) && $0.senderID != currentUserID }
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

// MARK: - NSImage Extension

extension NSImage {
    /// Конвертация NSImage в JPEG Data с заданным качеством сжатия
    func jpegData(compressionQuality: CGFloat) -> Data? {
        guard let tiffData = tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiffData) else { return nil }
        return bitmap.representation(using: .jpeg, properties: [.compressionFactor: compressionQuality])
    }
}
