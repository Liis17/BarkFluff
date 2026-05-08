//
//  UpdatesService.swift
//  BFCore
//
//  Реализация сервиса обновлений
//

import Foundation
import BFNetworking

/// Реализация сервиса real-time обновлений
public actor UpdatesService: UpdatesServiceProtocol {

    private let streamManager: UpdatesStreamManager

    /// Подписчики на новые сообщения (ключ — UUID подписки)
    private var newMessagesSubscribers: [UUID: AsyncStream<NewMessageEvent>.Continuation] = [:]
    /// Подписчики на события прочтения
    private var readEventsSubscribers: [UUID: AsyncStream<MessageReadEvent>.Continuation] = [:]
    /// Подписчики на события редактирования
    private var editedMessagesSubscribers: [UUID: AsyncStream<MessageEditedEvent>.Continuation] = [:]
    /// Подписчики на события удаления
    private var deletedMessagesSubscribers: [UUID: AsyncStream<MessageDeletedEvent>.Continuation] = [:]
    /// Подписчики на события подключения
    private var connectionSubscribers: [UUID: AsyncStream<UpdatesConnectionEvent>.Continuation] = [:]

    private var forwardNewMessagesTask: Task<Void, Never>?
    private var forwardReadEventsTask: Task<Void, Never>?
    private var forwardEditedMessagesTask: Task<Void, Never>?
    private var forwardDeletedMessagesTask: Task<Void, Never>?
    private var forwardConnectionTask: Task<Void, Never>?

    private var isActiveValue = false

    public init(updatesRepository: UpdatesRepositoryProtocol, streamManager: UpdatesStreamManager) {
        self.streamManager = streamManager
    }

    // MARK: - UpdatesServiceProtocol

    public func getNewMessagesStream() async -> AsyncStream<NewMessageEvent> {
        let id = UUID()
        let stream = AsyncStream<NewMessageEvent> { continuation in
            self.newMessagesSubscribers[id] = continuation
            continuation.onTermination = { @Sendable _ in
                Task { await self.removeNewMessagesSubscriber(id) }
            }
        }
        return stream
    }

    public func getReadEventsStream() async -> AsyncStream<MessageReadEvent> {
        let id = UUID()
        let stream = AsyncStream<MessageReadEvent> { continuation in
            self.readEventsSubscribers[id] = continuation
            continuation.onTermination = { @Sendable _ in
                Task { await self.removeReadEventsSubscriber(id) }
            }
        }
        return stream
    }

    public func getEditedMessagesStream() async -> AsyncStream<MessageEditedEvent> {
        let id = UUID()
        let stream = AsyncStream<MessageEditedEvent> { continuation in
            self.editedMessagesSubscribers[id] = continuation
            continuation.onTermination = { @Sendable _ in
                Task { await self.removeEditedMessagesSubscriber(id) }
            }
        }
        return stream
    }

    public func getDeletedMessagesStream() async -> AsyncStream<MessageDeletedEvent> {
        let id = UUID()
        let stream = AsyncStream<MessageDeletedEvent> { continuation in
            self.deletedMessagesSubscribers[id] = continuation
            continuation.onTermination = { @Sendable _ in
                Task { await self.removeDeletedMessagesSubscriber(id) }
            }
        }
        return stream
    }

    public func getConnectionEventsStream() async -> AsyncStream<UpdatesConnectionEvent> {
        let id = UUID()
        let stream = AsyncStream<UpdatesConnectionEvent> { continuation in
            self.connectionSubscribers[id] = continuation
            continuation.onTermination = { @Sendable _ in
                Task { await self.removeConnectionSubscriber(id) }
            }
        }
        return stream
    }

    public func isActive() async -> Bool {
        isActiveValue
    }

    public func start() async {
        guard !isActiveValue else { return }
        isActiveValue = true

        // Запускаем стрим менеджер (единственное место, создающее gRPC стримы)
        await streamManager.start()

        // Подписываемся на StreamManager стримы и прокидываем в доменные потоки
        let managerNewMessages = await streamManager.newMessages
        forwardNewMessagesTask = Task { [weak self] in
            for await event in managerNewMessages {
                guard let self else { return }
                print("🔄 [UpdatesService] Forwarding event msgID=\(event.message.id), attachments=\(event.message.attachments.count)")
                let domainEvent = await self.toDomainNewMessageEvent(event)
                let subscriberCount = await self.newMessagesSubscribers.count
                print("🔄 [UpdatesService] Broadcasting to \(subscriberCount) subscribers...")
                await self.broadcastNewMessage(domainEvent)
                print("✅ [UpdatesService] Broadcasted event msgID=\(event.message.id)")
            }
        }

        let managerReadEvents = await streamManager.messageReads
        forwardReadEventsTask = Task { [weak self] in
            for await event in managerReadEvents {
                guard let self else { return }
                let domainEvent = MessageReadEvent(
                    chatID: event.chatID,
                    messageID: event.messageID,
                    readBy: event.readBy
                )
                await self.broadcastReadEvent(domainEvent)
            }
        }

        let managerEdited = await streamManager.messagesEdited
        forwardEditedMessagesTask = Task { [weak self] in
            for await event in managerEdited {
                guard let self else { return }
                let domain = await self.toDomainEditedEvent(event)
                await self.broadcastEditedMessage(domain)
            }
        }

        let managerDeleted = await streamManager.messagesDeleted
        forwardDeletedMessagesTask = Task { [weak self] in
            for await event in managerDeleted {
                guard let self else { return }
                let domain = MessageDeletedEvent(chatID: event.chatID, messageID: event.messageID)
                await self.broadcastDeletedMessage(domain)
            }
        }

        let managerConnectionEvents = await streamManager.connectionEvents
        forwardConnectionTask = Task { [weak self] in
            for await event in managerConnectionEvents {
                guard let self else { return }
                let domainEvent: UpdatesConnectionEvent
                switch event {
                case .connectionLost: domainEvent = .connectionLost
                case .reconnected: domainEvent = .reconnected
                }
                await self.broadcastConnectionEvent(domainEvent)
            }
        }
    }

    public func stop() async {
        isActiveValue = false
        forwardNewMessagesTask?.cancel()
        forwardReadEventsTask?.cancel()
        forwardEditedMessagesTask?.cancel()
        forwardDeletedMessagesTask?.cancel()
        forwardConnectionTask?.cancel()
        forwardNewMessagesTask = nil
        forwardReadEventsTask = nil
        forwardEditedMessagesTask = nil
        forwardDeletedMessagesTask = nil
        forwardConnectionTask = nil
        await streamManager.stop()

        for (_, continuation) in newMessagesSubscribers { continuation.finish() }
        newMessagesSubscribers.removeAll()

        for (_, continuation) in readEventsSubscribers { continuation.finish() }
        readEventsSubscribers.removeAll()

        for (_, continuation) in editedMessagesSubscribers { continuation.finish() }
        editedMessagesSubscribers.removeAll()

        for (_, continuation) in deletedMessagesSubscribers { continuation.finish() }
        deletedMessagesSubscribers.removeAll()

        for (_, continuation) in connectionSubscribers { continuation.finish() }
        connectionSubscribers.removeAll()
    }

    // MARK: - Broadcast

    private func broadcastNewMessage(_ event: NewMessageEvent) {
        for (_, continuation) in newMessagesSubscribers {
            continuation.yield(event)
        }
    }

    private func broadcastReadEvent(_ event: MessageReadEvent) {
        for (_, continuation) in readEventsSubscribers {
            continuation.yield(event)
        }
    }

    private func broadcastConnectionEvent(_ event: UpdatesConnectionEvent) {
        for (_, continuation) in connectionSubscribers {
            continuation.yield(event)
        }
    }

    private func broadcastEditedMessage(_ event: MessageEditedEvent) {
        for (_, continuation) in editedMessagesSubscribers {
            continuation.yield(event)
        }
    }

    private func broadcastDeletedMessage(_ event: MessageDeletedEvent) {
        for (_, continuation) in deletedMessagesSubscribers {
            continuation.yield(event)
        }
    }

    // MARK: - Subscriber Cleanup

    private func removeNewMessagesSubscriber(_ id: UUID) {
        newMessagesSubscribers.removeValue(forKey: id)
    }

    private func removeReadEventsSubscriber(_ id: UUID) {
        readEventsSubscribers.removeValue(forKey: id)
    }

    private func removeEditedMessagesSubscriber(_ id: UUID) {
        editedMessagesSubscribers.removeValue(forKey: id)
    }

    private func removeDeletedMessagesSubscriber(_ id: UUID) {
        deletedMessagesSubscribers.removeValue(forKey: id)
    }

    private func removeConnectionSubscriber(_ id: UUID) {
        connectionSubscribers.removeValue(forKey: id)
    }

    // MARK: - Private

    private func toDomainNewMessageEvent(_ event: BFNetworking.NewMessageEvent) -> NewMessageEvent {
        NewMessageEvent(
            chatID: event.chatID,
            message: toDomainMessage(event.message)
        )
    }

    private func toDomainEditedEvent(_ event: BFNetworking.MessageEditedEventDTO) -> MessageEditedEvent {
        // Подменяем chatID, т.к. info из event.message может его не содержать.
        let raw = toDomainMessage(event.message)
        let domain = Message(
            id: raw.id,
            chatID: event.chatID,
            senderID: raw.senderID,
            senderName: raw.senderName,
            content: raw.content,
            sentAt: raw.sentAt,
            readBy: raw.readBy,
            isSystem: raw.isSystem,
            sendingState: raw.sendingState,
            localID: raw.localID,
            uploadProgress: raw.uploadProgress,
            isEdited: raw.isEdited,
            editedAt: raw.editedAt
        )
        return MessageEditedEvent(chatID: event.chatID, message: domain)
    }

    private func toDomainMessage(_ info: MessageInfo) -> Message {
        Message(
            id: info.id,
            chatID: info.chatID,
            senderID: info.senderID,
            senderName: info.senderName,
            content: MessageContent(
                text: info.content,
                attachments: info.attachments.map { toDomainAttachment($0) }
            ),
            sentAt: info.sentAt,
            readBy: info.readBy,
            isSystem: info.isSystem,
            isEdited: info.isEdited,
            editedAt: info.editedAt
        )
    }

    private func toDomainAttachment(_ info: MessageAttachmentInfo) -> MessageAttachment {
        let forwarded = info.forwarded.map { dto in
            ForwardedMessagePayload(
                authorName: dto.authorName,
                originalMessageID: dto.originalMessageID,
                text: dto.text,
                attachments: dto.attachments.map { toDomainAttachment($0) }
            )
        }
        return MessageAttachment(
            id: info.id,
            type: toDomainAttachmentType(info.type),
            fileID: info.fileID,
            fileName: info.fileName,
            fileSize: info.fileSize,
            previewURL: info.previewURL,
            forwarded: forwarded
        )
    }

    private func toDomainAttachmentType(_ type: BFNetworking.AttachmentType) -> AttachmentType {
        switch type {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        case .audio: return .audio
        case .voice: return .voice
        case .sticker: return .sticker
        case .forwardedMessage: return .forwardedMessage
        }
    }
}
