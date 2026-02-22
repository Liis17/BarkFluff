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

    private var newMessagesContinuation: AsyncStream<NewMessageEvent>.Continuation?
    private var readEventsContinuation: AsyncStream<MessageReadEvent>.Continuation?
    private var connectionContinuation: AsyncStream<UpdatesConnectionEvent>.Continuation?

    private var newMessagesStream: AsyncStream<NewMessageEvent>?
    private var readEventsStream: AsyncStream<MessageReadEvent>?
    private var connectionEventsStream: AsyncStream<UpdatesConnectionEvent>?

    private var forwardNewMessagesTask: Task<Void, Never>?
    private var forwardReadEventsTask: Task<Void, Never>?
    private var forwardConnectionTask: Task<Void, Never>?

    private var isActiveValue = false

    public init(updatesRepository: UpdatesRepositoryProtocol, streamManager: UpdatesStreamManager) {
        self.streamManager = streamManager
    }

    // MARK: - UpdatesServiceProtocol

    public func getNewMessagesStream() async -> AsyncStream<NewMessageEvent> {
        if let existing = newMessagesStream {
            return existing
        }

        let stream = AsyncStream<NewMessageEvent> { continuation in
            self.newMessagesContinuation = continuation
        }
        newMessagesStream = stream
        return stream
    }

    public func getReadEventsStream() async -> AsyncStream<MessageReadEvent> {
        if let existing = readEventsStream {
            return existing
        }

        let stream = AsyncStream<MessageReadEvent> { continuation in
            self.readEventsContinuation = continuation
        }
        readEventsStream = stream
        return stream
    }

    public func getConnectionEventsStream() async -> AsyncStream<UpdatesConnectionEvent> {
        if let existing = connectionEventsStream {
            return existing
        }

        let stream = AsyncStream<UpdatesConnectionEvent> { continuation in
            self.connectionContinuation = continuation
        }
        connectionEventsStream = stream
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
                let domainEvent = await self.toDomainNewMessageEvent(event)
                await self.newMessagesContinuation?.yield(domainEvent)
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
                await self.readEventsContinuation?.yield(domainEvent)
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
                await self.connectionContinuation?.yield(domainEvent)
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
        newMessagesContinuation?.finish()
        readEventsContinuation?.finish()
        connectionContinuation?.finish()
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
        }
    }
}
