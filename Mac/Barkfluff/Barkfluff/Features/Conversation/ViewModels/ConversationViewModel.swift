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
    /// Идёт фоновое обновление сообщений с сервера, когда кэш уже показан
    /// (stale-while-revalidate). Используется для индикатора «Обновление…».
    var isRefreshing = false
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

    /// Сообщение, на которое сейчас формируется ответ. Превью показывается над полем ввода.
    /// Очищается после успешной отправки или нажатия × в превью.
    var pendingReply: Message?

    /// Сообщение, которое сейчас редактируется. Превью с заголовком «Редактирование сообщения»
    /// показывается над полем ввода. Edit и Reply — взаимоисключающие.
    var editingMessage: Message?

    /// fileIDs оригинального сообщения для передачи в editMessage (без forwarded-вложений).
    /// На macOS нет UI редактирования вложений — сохраняем оригинальные.
    private var editingOriginalFileIDs: [String] = []

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
    private let localMessageRepository: LocalMessageRepository?
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
    private var editedMessagesTask: Task<Void, Never>?
    private var deletedMessagesTask: Task<Void, Never>?
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
        chatService: ChatServiceProtocol? = nil,
        localMessageRepository: LocalMessageRepository? = nil
    ) {
        self.chat = chat
        self.messageService = messageService
        self.updatesService = updatesService
        self.onlineStatusService = onlineStatusService
        self.fileService = fileService
        self.currentUserID = currentUserID
        self.chatService = chatService
        self.localMessageRepository = localMessageRepository
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

        // 1. Stale: показать кешированные сообщения сразу.
        var hasCachedData = false
        if messages.isEmpty, let local = localMessageRepository {
            if let cached = try? await local.loadCachedMessages(chatID: chat.id), !cached.isEmpty {
                messages = cached
                hasCachedData = true
            }
        }

        if hasCachedData {
            isRefreshing = true
        } else {
            isLoading = true
        }
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
            let serverMessages = uniqueMessages.sorted { $0.sentAt < $1.sentAt }
            hasMoreMessages = loadedMessages.count >= PaginationHelper.defaultMessagesPageSize

            // Слияние: сохраняем pending (id < 0, локальные) + берём актуальные с сервера.
            let pending = messages.filter { $0.id < 0 }
            messages = serverMessages + pending

            // 2. Сохраняем актуальный снимок в БД.
            if let local = localMessageRepository {
                try? await local.upsertMessages(serverMessages, chatID: chat.id)
            }

            // Определяем первое непрочитанное сообщение (до пометки прочитанными)
            firstUnreadMessageID = messages.first(where: {
                $0.senderID != currentUserID && !$0.readBy.contains(currentUserID)
            })?.id

            // Если первое непрочитанное близко к началу списка — подгружаем старые для контекста
            if let unreadID = firstUnreadMessageID,
               let unreadIndex = messages.firstIndex(where: { $0.id == unreadID }),
               unreadIndex < 5, hasMoreMessages {
                isLoading = false
                isRefreshing = false
                await loadMoreMessages()
            }

            // Пометить как прочитанные
            await markVisibleMessagesAsRead()
        } catch {
            // Ошибку показываем только если кеш пустой; иначе кэш виден в UI.
            if messages.isEmpty {
                errorMessage = error.localizedDescription
            }
        }

        isLoading = false
        isRefreshing = false
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

        // Snapshot pending-reply и сразу очищаем — UI обновится синхронно с инпутом
        let replyTarget = pendingReply
        if replyTarget != nil { clearPendingReply() }

        // Для новых диалогов — старый flow без optimistic. Reply в новом диалоге невозможен.
        if isNewConversation, let userID = targetUserID {
            do {
                let message = try await messageService.sendMessage(
                    chatID: nil,
                    userID: userID,
                    text: text,
                    fileIDs: fileIDs,
                    forwardedMessageID: nil
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

        let replyPlaceholder = replyTarget.map { Self.makeForwardedPlaceholder(from: $0) }
        let pendingAttachments: [MessageAttachment] = replyPlaceholder.map { [$0] } ?? []

        let pendingMessage = Message(
            id: pendingID,
            chatID: chat.id,
            senderID: currentUserID,
            content: MessageContent(text: trimmedText, attachments: pendingAttachments),
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
                fileIDs: fileIDs,
                forwardedMessageID: replyTarget?.id
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

    /// Отправить чистый стикер (без текста). Файл стикера уже на сервере — отправляем
    /// его fileID через обычный sendMessage, как это делают веб- и Android-клиенты.
    func sendSticker(_ sticker: Sticker) async {
        let stickerFileID = sticker.fileID
        guard !stickerFileID.isEmpty else { return }

        // В новом диалоге — flow без optimistic, как в sendMessage.
        if isNewConversation, let userID = targetUserID {
            do {
                let confirmed = try await messageService.sendMessage(
                    chatID: nil,
                    userID: userID,
                    text: "",
                    fileIDs: [stickerFileID],
                    forwardedMessageID: nil
                )
                await resolveNewConversation(firstMessage: confirmed)
            } catch {
                errorMessage = error.localizedDescription
            }
            return
        }

        let localID = UUID().uuidString
        let pendingID = Self.nextPendingID()

        // Локальное превью стикера: previewURL подсунем в presignedURLHint
        // через MessageAttachment.previewURL.
        let previewURL = sticker.previewURL.isEmpty ? sticker.fileURL : sticker.previewURL
        let localAttachment = MessageAttachment(
            id: -Int64.random(in: 1...Int64.max / 2),
            type: .sticker,
            fileID: stickerFileID,
            fileName: "\(sticker.id).webp",
            fileSize: 0,
            previewURL: previewURL.isEmpty ? nil : previewURL,
            previewFileID: sticker.previewFileID.isEmpty ? nil : sticker.previewFileID
        )
        let pending = Message(
            id: pendingID,
            chatID: chat.id,
            senderID: currentUserID,
            content: MessageContent(text: "", attachments: [localAttachment]),
            sentAt: Date(),
            sendingState: .sending,
            localID: localID
        )

        withAnimation(.spring(duration: 0.3)) {
            messages.append(pending)
        }

        do {
            let confirmed = try await messageService.sendMessage(
                chatID: chat.id,
                userID: nil,
                text: "",
                fileIDs: [stickerFileID],
                forwardedMessageID: nil
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
        // Snapshot pending-reply — даже если pipeline async, превью убираем сразу.
        let replyTarget = pendingReply
        if replyTarget != nil { clearPendingReply() }

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
                    var fileName = attachment.displayName
                    uploadProgress[attachment.id] = 0.1
                    if attachment.uploadFileType == .messageAttachmentImage {
                        if let optimized = optimizeImage(data) {
                            data = optimized
                            fileName = jpegFileName(from: fileName)
                        }
                    }
                    let fileID = try await fileService.uploadFile(
                        data: data, fileName: fileName, fileType: attachment.uploadFileType
                    )
                    fileIDs.append(fileID)
                    uploadProgress[attachment.id] = 1.0
                }
                let trimmedText = text.trimmingCharacters(in: .whitespacesAndNewlines)
                let message = try await messageService.sendMessage(
                    chatID: nil, userID: userID, text: trimmedText, fileIDs: fileIDs, forwardedMessageID: replyTarget?.id
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
        var localAttachments = hasAttachments
            ? Self.createLocalAttachments(from: attachments)
            : []
        // Добавляем placeholder ответа в начало, чтобы цитата отрисовалась сразу
        if let replyTarget {
            localAttachments.insert(Self.makeForwardedPlaceholder(from: replyTarget), at: 0)
        }

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
                var fileName = attachment.displayName

                if attachment.uploadFileType == .messageAttachmentImage {
                    if let optimized = optimizeImage(data) {
                        data = optimized
                        fileName = jpegFileName(from: fileName)
                    }
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
                    data: data, fileName: fileName, fileType: attachment.uploadFileType
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
                chatID: chat.id, userID: nil, text: trimmedText, fileIDs: fileIDs, forwardedMessageID: replyTarget?.id
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

    // MARK: - Reply / Forward

    /// Установить сообщение, на которое формируется ответ.
    /// Превью покажется над полем ввода; следующая отправка прикрепит forwardedMessageID.
    func setPendingReply(_ message: Message) {
        // В новом диалоге чата на сервере нет — реальный id оригинала тоже отрицательный.
        guard !isNewConversation, message.id > 0 else { return }
        if editingMessage != nil { cancelEdit() }
        pendingReply = message
    }

    /// Очистить состояние ответа.
    func clearPendingReply() {
        pendingReply = nil
    }

    // MARK: - Edit / Delete

    /// Войти в режим редактирования сообщения. Только для своих, отправленных сообщений.
    func enterEditMode(_ message: Message) {
        guard !isNewConversation,
              message.senderID == currentUserID,
              message.id > 0 else { return }
        if pendingReply != nil { clearPendingReply() }
        editingOriginalFileIDs = message.content.attachments
            .filter { $0.type != .forwardedMessage }
            .map { $0.fileID }
            .filter { !$0.isEmpty }
        editingMessage = message
    }

    /// Выйти из режима редактирования.
    func cancelEdit() {
        editingMessage = nil
        editingOriginalFileIDs = []
    }

    /// Применить редактирование. Текст приходит из инпута ConversationView.
    func submitEdit(text: String) async {
        guard let original = editingMessage else { return }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty || !editingOriginalFileIDs.isEmpty else { return }

        let messageID = original.id
        let fileIDs = editingOriginalFileIDs

        // Optimistic локальное обновление
        if let idx = messages.firstIndex(where: { $0.id == messageID }) {
            messages[idx].content.text = trimmed
            messages[idx].isEdited = true
            messages[idx].editedAt = Date()
        }

        cancelEdit()

        do {
            let confirmed = try await messageService.editMessage(
                chatID: chat.id,
                messageID: messageID,
                text: trimmed,
                fileIDs: fileIDs
            )
            applyEditedMessage(confirmed)
            if let local = localMessageRepository {
                try? await local.update(confirmed)
            }
        } catch {
            errorMessage = error.localizedDescription
            // Откат: перезагружаем список (простейший корректный путь).
            await loadMessages()
        }
    }

    /// Удалить сообщение (с серверной отправкой и optimistic remove).
    func deleteMessage(messageID: Int64) async {
        let snapshot = messages
        withAnimation(.spring(duration: 0.25)) {
            messages.removeAll { $0.id == messageID }
        }

        do {
            try await messageService.deleteMessage(messageID: messageID)
            if let local = localMessageRepository {
                try? await local.delete(messageID: messageID, chatID: chat.id)
            }
        } catch {
            messages = snapshot
            errorMessage = error.localizedDescription
        }
    }

    /// Локальный placeholder forwarded-attachment для optimistic UI до подтверждения сервером.
    private static func makeForwardedPlaceholder(from original: Message) -> MessageAttachment {
        let snippet = String(original.content.text.prefix(80))
        return MessageAttachment(
            id: -Int64.random(in: 1...Int64.max / 2),
            type: .forwardedMessage,
            fileID: "",
            fileName: "",
            fileSize: 0,
            forwarded: ForwardedMessagePayload(
                authorName: original.senderName ?? String(localized: "common.unknown_user"),
                originalMessageID: original.id,
                text: snippet,
                attachments: []
            )
        )
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
                    chatID: chat.id, userID: nil, text: text, fileIDs: [], forwardedMessageID: nil
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
            errorMessage = String(localized: "conversation.errors.chat_info_failed")
        }
    }

    // MARK: - Image Optimization

    /// Параметры обработки изображений согласно dd.md (раздел «Изображения»):
    /// макс. 2500 px по длинной стороне, JPEG 90%, итоговый размер ≤ 2 МБ.
    private static let maxImageDimension: CGFloat = 2500
    private static let maxImageBytes: Int = 2 * 1024 * 1024
    /// Стартуем с 0.9 (целевое качество). При превышении 2 МБ — пошагово вниз.
    private static let jpegQualitySteps: [CGFloat] = [0.9, 0.8, 0.7, 0.6, 0.5]

    /// Оптимизация изображения перед отправкой:
    /// - ресайз до 2500 px по большей стороне с сохранением пропорций;
    /// - JPEG 90%, при превышении 2 МБ — пошаговое снижение качества до укладывания в лимит.
    private func optimizeImage(_ data: Data) -> Data? {
        guard let source = NSImage(data: data) else { return nil }

        let size = source.size
        let scaled: NSImage
        if size.width > Self.maxImageDimension || size.height > Self.maxImageDimension {
            let ratio = min(Self.maxImageDimension / size.width, Self.maxImageDimension / size.height)
            let newSize = CGSize(width: size.width * ratio, height: size.height * ratio)
            scaled = NSImage(size: newSize)
            scaled.lockFocus()
            source.draw(in: NSRect(origin: .zero, size: newSize),
                        from: NSRect(origin: .zero, size: size),
                        operation: .copy,
                        fraction: 1.0)
            scaled.unlockFocus()
        } else {
            scaled = source
        }

        var lastEncoded: Data?
        for quality in Self.jpegQualitySteps {
            guard let encoded = scaled.jpegData(compressionQuality: quality) else { continue }
            lastEncoded = encoded
            if encoded.count <= Self.maxImageBytes { return encoded }
        }
        return lastEncoded ?? data
    }

    /// Заменяет расширение в имени файла на `.jpg` — после `optimizeImage`
    /// байты гарантированно JPEG, и Content-Type на multipart-загрузке должен совпасть.
    private func jpegFileName(from original: String) -> String {
        let stem = (original as NSString).deletingPathExtension
        return stem.isEmpty ? "image.jpg" : "\(stem).jpg"
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

        // Слушаем события редактирования
        editedMessagesTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getEditedMessagesStream()
            for await event in stream {
                await MainActor.run {
                    self.handleEditedMessage(event)
                }
            }
        }

        // Слушаем события удаления
        deletedMessagesTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getDeletedMessagesStream()
            for await event in stream {
                await MainActor.run {
                    self.handleDeletedMessage(event)
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
        editedMessagesTask?.cancel()
        deletedMessagesTask?.cancel()
        onlineStatusTask?.cancel()
        newMessagesTask = nil
        readEventsTask = nil
        editedMessagesTask = nil
        deletedMessagesTask = nil
        onlineStatusTask = nil

        // Парный untrack для track из startListeningForOnlineStatus.
        if !chat.isGroupChat, let otherUserID = chat.otherUserID(excluding: currentUserID) {
            let service = onlineStatusService
            Task { await service.untrack(otherUserID) }
        }
    }

    // MARK: - Online Status

    private func startListeningForOnlineStatus() async {
        guard !chat.isGroupChat, let otherUserID = chat.otherUserID(excluding: currentUserID) else { return }

        // Snapshot из кеша — мгновенный показ.
        let status = await onlineStatusService.currentStatus(for: otherUserID)
        await MainActor.run { self.otherUserOnlineStatus = status }

        // Track + per-user stream. track обязателен парный untrack — делаем
        // в stopListeningForUpdates(). При reconnect / переоткрытии чата
        // refcount корректно балансируется.
        await onlineStatusService.track(otherUserID)

        // Свежее значение из кеша после track (track делает authoritative fetch).
        let refreshed = await onlineStatusService.currentStatus(for: otherUserID)
        await MainActor.run { self.otherUserOnlineStatus = refreshed }

        // Отменяем предыдущую задачу — предотвращает утечку подписчиков.
        onlineStatusTask?.cancel()
        onlineStatusTask = Task { [weak self, onlineStatusService] in
            let stream = await onlineStatusService.statusStream(for: otherUserID)
            for await newStatus in stream {
                guard let self else { break }
                await MainActor.run { self.otherUserOnlineStatus = newStatus }
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
            persistIncoming(event.message)
            return
        }

        // Чужие сообщения — добавляем с анимацией
        withAnimation(.spring(duration: 0.3)) {
            messages.append(event.message)
        }
        persistIncoming(event.message)

        Task {
            await markMessageAsRead(event.message)
        }
    }

    private func persistIncoming(_ message: Message) {
        guard let local = localMessageRepository else { return }
        Task { try? await local.append(message) }
    }

    private func handleMessageRead(_ event: MessageReadEvent) {
        // Проверяем, что событие для этого чата
        guard event.chatID == chat.id else { return }

        // Обновляем статус прочтения
        if let index = messages.firstIndex(where: { $0.id == event.messageID }) {
            messages[index].readBy = event.readBy
        }
    }

    private func handleEditedMessage(_ event: MessageEditedEvent) {
        guard event.chatID == chat.id else { return }
        applyEditedMessage(event.message)
        if let local = localMessageRepository {
            let updated = event.message
            Task { try? await local.update(updated) }
        }
    }

    /// Применить пришедшее обновление, сохраняя локальные state-поля (localID, sendingState).
    private func applyEditedMessage(_ updated: Message) {
        guard let idx = messages.firstIndex(where: { $0.id == updated.id }) else { return }
        let existing = messages[idx]
        let merged = Message(
            id: updated.id,
            chatID: existing.chatID,
            senderID: updated.senderID,
            senderName: updated.senderName ?? existing.senderName,
            content: updated.content,
            sentAt: updated.sentAt,
            readBy: updated.readBy,
            isSystem: updated.isSystem,
            sendingState: existing.sendingState,
            localID: existing.localID,
            uploadProgress: existing.uploadProgress,
            isEdited: updated.isEdited,
            editedAt: updated.editedAt
        )
        messages[idx] = merged
    }

    private func handleDeletedMessage(_ event: MessageDeletedEvent) {
        guard event.chatID == chat.id else { return }
        let messageID = event.messageID
        withAnimation(.spring(duration: 0.25)) {
            messages.removeAll { $0.id == messageID }
        }
        if let local = localMessageRepository {
            let chatID = chat.id
            Task { try? await local.delete(messageID: messageID, chatID: chatID) }
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
//
// `jpegData(compressionQuality:)` вынесен в общий extension
// `DesignSystem/Extensions/NSImage+JPEG.swift`, см. там.
