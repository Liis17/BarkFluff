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
    /// Подписчики на события подключения
    private var connectionSubscribers: [UUID: AsyncStream<UpdatesConnectionEvent>.Continuation] = [:]

    private var forwardNewMessagesTask: Task<Void, Never>?
    private var forwardReadEventsTask: Task<Void, Never>?
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
        forwardConnectionTask?.cancel()
        forwardNewMessagesTask = nil
        forwardReadEventsTask = nil
        forwardConnectionTask = nil
        await streamManager.stop()

        for (_, continuation) in newMessagesSubscribers { continuation.finish() }
        newMessagesSubscribers.removeAll()

        for (_, continuation) in readEventsSubscribers { continuation.finish() }
        readEventsSubscribers.removeAll()

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

    // MARK: - Subscriber Cleanup

    private func removeNewMessagesSubscriber(_ id: UUID) {
        newMessagesSubscribers.removeValue(forKey: id)
    }

    private func removeReadEventsSubscriber(_ id: UUID) {
        readEventsSubscribers.removeValue(forKey: id)
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
            isSystem: info.isSystem
        )
    }

    private func toDomainAttachment(_ info: MessageAttachmentInfo) -> MessageAttachment {
        MessageAttachment(
            id: info.id,
            type: toDomainAttachmentType(info.type),
            fileID: info.fileID,
            fileName: info.fileName,
            fileSize: info.fileSize,
            previewURL: info.previewURL
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
        }
    }
}
