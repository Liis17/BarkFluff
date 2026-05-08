//
//  UpdatesRepository.swift
//  BFNetworking
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

public actor UpdatesRepository: UpdatesRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func subscribeNewMessages() async throws -> AsyncThrowingStream<NewMessageEvent, Error> {
        let req = Barkfluff_Updates_SubscribeNewMessagesRequest()

        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withAuthorizedClient(for: .updates) { client in
                        let updatesClient = Barkfluff_Updates_UpdatesApi.Client(wrapping: client)
                        try await updatesClient.subscribeNewMessages(req) { response in
                            for try await event in response.messages {
                                let chatID = event.chatID
                                let msg = event.message

                                // Логируем что приходит с бэкенда
                                print("📡 [UpdatesRepository] Received event - chatID: \(chatID), msgID: \(msg.id), text: \(msg.content.text.prefix(30)), attachments: \(msg.content.attachments.count)")

                                let sentAt: Date
                                if msg.hasSentAt {
                                    sentAt = Date(timeIntervalSince1970: TimeInterval(msg.sentAt.seconds) + TimeInterval(msg.sentAt.nanos) / 1_000_000_000)
                                } else {
                                    sentAt = Date()
                                }
                                let attachments = msg.content.attachments.map { self.mapAttachment($0) }
                                let messageInfo = MessageInfo(
                                    id: msg.id,
                                    chatID: chatID,
                                    senderID: msg.senderID,
                                    senderName: nil,
                                    content: msg.content.text,
                                    attachments: attachments,
                                    sentAt: sentAt,
                                    readBy: msg.readBy,
                                    isSystem: msg.type == .system
                                )
                                continuation.yield(NewMessageEvent(chatID: chatID, message: messageInfo))
                            }
                            continuation.finish()
                        }
                    }
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    public func subscribeMessagesRead() async throws -> AsyncThrowingStream<MessageReadEvent, Error> {
        let req = Barkfluff_Updates_SubscribeMessagesReadRequest()

        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withAuthorizedClient(for: .updates) { client in
                        let updatesClient = Barkfluff_Updates_UpdatesApi.Client(wrapping: client)
                        try await updatesClient.subscribeMessagesRead(req) { response in
                            for try await event in response.messages {
                                continuation.yield(MessageReadEvent(
                                    chatID: event.chatID,
                                    messageID: event.messageID,
                                    readBy: event.newReadBy
                                ))
                            }
                            continuation.finish()
                        }
                    }
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    public func subscribeMessagesEdited() async throws -> AsyncThrowingStream<MessageEditedEventDTO, Error> {
        let req = Barkfluff_Updates_SubscribeMessagesEditedRequest()

        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withAuthorizedClient(for: .updates) { client in
                        let updatesClient = Barkfluff_Updates_UpdatesApi.Client(wrapping: client)
                        try await updatesClient.subscribeMessagesEdited(req) { response in
                            for try await event in response.messages {
                                let chatID = event.chatID
                                let msg = event.message

                                let sentAt: Date
                                if msg.hasSentAt {
                                    sentAt = Date(
                                        timeIntervalSince1970: TimeInterval(msg.sentAt.seconds)
                                            + TimeInterval(msg.sentAt.nanos) / 1_000_000_000
                                    )
                                } else {
                                    sentAt = Date()
                                }
                                let editedAt: Date?
                                if msg.hasEditedAt {
                                    editedAt = Date(
                                        timeIntervalSince1970: TimeInterval(msg.editedAt.seconds)
                                            + TimeInterval(msg.editedAt.nanos) / 1_000_000_000
                                    )
                                } else {
                                    editedAt = nil
                                }
                                let attachments = msg.content.attachments.map { self.mapAttachment($0) }
                                let messageInfo = MessageInfo(
                                    id: msg.id,
                                    chatID: chatID,
                                    senderID: msg.senderID,
                                    senderName: nil,
                                    content: msg.content.text,
                                    attachments: attachments,
                                    sentAt: sentAt,
                                    readBy: msg.readBy,
                                    isSystem: msg.type == .system,
                                    isEdited: msg.isEdited,
                                    editedAt: editedAt
                                )
                                continuation.yield(MessageEditedEventDTO(chatID: chatID, message: messageInfo))
                            }
                            continuation.finish()
                        }
                    }
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    public func subscribeMessagesDeleted() async throws -> AsyncThrowingStream<MessageDeletedEventDTO, Error> {
        let req = Barkfluff_Updates_SubscribeMessagesDeletedRequest()

        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withAuthorizedClient(for: .updates) { client in
                        let updatesClient = Barkfluff_Updates_UpdatesApi.Client(wrapping: client)
                        try await updatesClient.subscribeMessagesDeleted(req) { response in
                            for try await event in response.messages {
                                continuation.yield(MessageDeletedEventDTO(
                                    chatID: event.chatID,
                                    messageID: event.messageID
                                ))
                            }
                            continuation.finish()
                        }
                    }
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    // MARK: - Helpers

    private nonisolated func mapAttachmentType(_ type: Barkfluff_Shared_MessageAttachmentType) -> AttachmentType {
        switch type {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        case .forwardedMessage: return .forwardedMessage
        default: return .document
        }
    }

    private nonisolated func mapAttachment(_ att: Barkfluff_Shared_MessageAttachment) -> MessageAttachmentInfo {
        let forwarded: ForwardedMessageDTO?
        if att.hasForwardedMessage {
            let fwd = att.forwardedMessage
            forwarded = ForwardedMessageDTO(
                authorName: fwd.authorName,
                originalMessageID: fwd.originalMessageID,
                text: fwd.text,
                attachments: fwd.attachments.map { mapInnerAttachment($0) }
            )
        } else {
            forwarded = nil
        }
        return MessageAttachmentInfo(
            id: att.id,
            type: mapAttachmentType(att.type),
            fileID: att.fileID,
            previewURL: att.previewURL.isEmpty ? nil : att.previewURL,
            fileName: att.fileName,
            fileSize: att.attachmentSize,
            forwarded: forwarded
        )
    }

    private nonisolated func mapInnerAttachment(_ att: Barkfluff_Shared_MessageAttachment) -> MessageAttachmentInfo {
        MessageAttachmentInfo(
            id: att.id,
            type: mapAttachmentType(att.type),
            fileID: att.fileID,
            previewURL: att.previewURL.isEmpty ? nil : att.previewURL,
            fileName: att.fileName,
            fileSize: att.attachmentSize,
            forwarded: nil
        )
    }
}

// MARK: - UpdatesStreamManager

public actor UpdatesStreamManager {

    /// Событие подключения
    public enum ConnectionEvent: Sendable {
        case connectionLost
        case reconnected
    }

    private let updatesRepository: UpdatesRepositoryProtocol
    private let tokenRefreshCoordinator: TokenRefreshCoordinator?

    private var newMessagesTask: Task<Void, Never>?
    private var messagesReadTask: Task<Void, Never>?
    private var messagesEditedTask: Task<Void, Never>?
    private var messagesDeletedTask: Task<Void, Never>?

    private var newMessagesContinuation: AsyncStream<NewMessageEvent>.Continuation?
    private var messagesReadContinuation: AsyncStream<MessageReadEvent>.Continuation?
    private var messagesEditedContinuation: AsyncStream<MessageEditedEventDTO>.Continuation?
    private var messagesDeletedContinuation: AsyncStream<MessageDeletedEventDTO>.Continuation?
    private var connectionContinuation: AsyncStream<ConnectionEvent>.Continuation?

    public private(set) var newMessages: AsyncStream<NewMessageEvent>
    public private(set) var messageReads: AsyncStream<MessageReadEvent>
    public private(set) var messagesEdited: AsyncStream<MessageEditedEventDTO>
    public private(set) var messagesDeleted: AsyncStream<MessageDeletedEventDTO>
    public private(set) var connectionEvents: AsyncStream<ConnectionEvent>

    private var isRunning = false
    private static let maxBackoff: TimeInterval = 30

    public init(updatesRepository: UpdatesRepositoryProtocol, tokenRefreshCoordinator: TokenRefreshCoordinator? = nil) {
        self.updatesRepository = updatesRepository
        self.tokenRefreshCoordinator = tokenRefreshCoordinator

        var newMsgCont: AsyncStream<NewMessageEvent>.Continuation!
        self.newMessages = AsyncStream { continuation in
            newMsgCont = continuation
        }
        self.newMessagesContinuation = newMsgCont

        var readCont: AsyncStream<MessageReadEvent>.Continuation!
        self.messageReads = AsyncStream { continuation in
            readCont = continuation
        }
        self.messagesReadContinuation = readCont

        var editedCont: AsyncStream<MessageEditedEventDTO>.Continuation!
        self.messagesEdited = AsyncStream { continuation in
            editedCont = continuation
        }
        self.messagesEditedContinuation = editedCont

        var deletedCont: AsyncStream<MessageDeletedEventDTO>.Continuation!
        self.messagesDeleted = AsyncStream { continuation in
            deletedCont = continuation
        }
        self.messagesDeletedContinuation = deletedCont

        var connCont: AsyncStream<ConnectionEvent>.Continuation!
        self.connectionEvents = AsyncStream { continuation in
            connCont = continuation
        }
        self.connectionContinuation = connCont
    }

    public func start() async {
        guard !isRunning else { return }
        isRunning = true
        print("🔄 [UpdatesStreamManager] Starting streams...")

        // Пересоздаём потоки: после stop() continuations мертвы, новые Task-и не смогут
        // forward-ить события в старые завершённые потоки.
        var newMsgCont: AsyncStream<NewMessageEvent>.Continuation!
        newMessages = AsyncStream { continuation in newMsgCont = continuation }
        newMessagesContinuation = newMsgCont

        var readCont: AsyncStream<MessageReadEvent>.Continuation!
        messageReads = AsyncStream { continuation in readCont = continuation }
        messagesReadContinuation = readCont

        var editedCont: AsyncStream<MessageEditedEventDTO>.Continuation!
        messagesEdited = AsyncStream { continuation in editedCont = continuation }
        messagesEditedContinuation = editedCont

        var deletedCont: AsyncStream<MessageDeletedEventDTO>.Continuation!
        messagesDeleted = AsyncStream { continuation in deletedCont = continuation }
        messagesDeletedContinuation = deletedCont

        var connCont: AsyncStream<ConnectionEvent>.Continuation!
        connectionEvents = AsyncStream { continuation in connCont = continuation }
        connectionContinuation = connCont

        newMessagesTask = Task { [weak self] in
            guard let self else { return }
            var backoff: TimeInterval = 1
            var hasConnectedBefore = false
            var consecutiveErrors = 0

            while !Task.isCancelled {
                do {
                    print("📡 [UpdatesStreamManager] Connecting to newMessages stream (attempt \(consecutiveErrors + 1))...")
                    let stream = try await self.updatesRepository.subscribeNewMessages()
                    backoff = 1
                    consecutiveErrors = 0
                    print("✅ [UpdatesStreamManager] newMessages stream connected")
                    if hasConnectedBefore {
                        await self.emitConnectionEvent(.reconnected)
                        print("🔄 [UpdatesStreamManager] Reconnected event sent")
                    }
                    hasConnectedBefore = true
                    for try await event in stream {
                        let cont = await self.newMessagesContinuation
                        cont?.yield(event)
                    }
                    // Stream ended normally — treat as connection lost
                    if !Task.isCancelled, hasConnectedBefore {
                        print("⚠️ [UpdatesStreamManager] newMessages stream ended normally, treating as connection lost")
                        await self.emitConnectionEvent(.connectionLost)
                    }
                } catch {
                    if Task.isCancelled { break }
                    consecutiveErrors += 1

                    let isAuthError = self.isAuthenticationError(error)
                    print("❌ [UpdatesStreamManager] newMessages stream error (#\(consecutiveErrors)): \(error.localizedDescription)")
                    print("   isAuthError: \(isAuthError), backoff: \(backoff)s")

                    // Если ошибка авторизации — пробуем обновить токен
                    if isAuthError {
                        print("🔐 [UpdatesStreamManager] Attempting token refresh due to auth error...")
                        if let coordinator = await self.tokenRefreshCoordinator {
                            do {
                                _ = try await coordinator.refreshAccessToken()
                                print("✅ [UpdatesStreamManager] Token refreshed successfully")
                            } catch {
                                print("❌ [UpdatesStreamManager] Token refresh failed: \(error.localizedDescription)")
                            }
                        } else {
                            print("⚠️ [UpdatesStreamManager] No tokenRefreshCoordinator available")
                        }
                    }

                    if hasConnectedBefore {
                        await self.emitConnectionEvent(.connectionLost)
                    }
                    hasConnectedBefore = true
                    try? await Task.sleep(for: .seconds(backoff))
                    backoff = min(backoff * 2, UpdatesStreamManager.maxBackoff)
                }
            }
            print("🛑 [UpdatesStreamManager] newMessages task cancelled")
        }

        messagesReadTask = Task { [weak self] in
            guard let self else { return }
            var backoff: TimeInterval = 1
            var consecutiveErrors = 0

            while !Task.isCancelled {
                do {
                    print("📡 [UpdatesStreamManager] Connecting to messagesRead stream (attempt \(consecutiveErrors + 1))...")
                    let stream = try await self.updatesRepository.subscribeMessagesRead()
                    backoff = 1
                    consecutiveErrors = 0
                    print("✅ [UpdatesStreamManager] messagesRead stream connected")
                    for try await event in stream {
                        let cont = await self.messagesReadContinuation
                        cont?.yield(event)
                    }
                    print("⚠️ [UpdatesStreamManager] messagesRead stream ended normally")
                } catch {
                    if Task.isCancelled { break }
                    consecutiveErrors += 1

                    let isAuthError = self.isAuthenticationError(error)
                    print("❌ [UpdatesStreamManager] messagesRead stream error (#\(consecutiveErrors)): \(error.localizedDescription)")
                    print("   isAuthError: \(isAuthError), backoff: \(backoff)s")

                    // Если ошибка авторизации — пробуем обновить токен
                    if isAuthError {
                        print("🔐 [UpdatesStreamManager] Attempting token refresh due to auth error...")
                        if let coordinator = await self.tokenRefreshCoordinator {
                            do {
                                _ = try await coordinator.refreshAccessToken()
                                print("✅ [UpdatesStreamManager] Token refreshed successfully")
                            } catch {
                                print("❌ [UpdatesStreamManager] Token refresh failed: \(error.localizedDescription)")
                            }
                        }
                    }

                    try? await Task.sleep(for: .seconds(backoff))
                    backoff = min(backoff * 2, UpdatesStreamManager.maxBackoff)
                }
            }
            print("🛑 [UpdatesStreamManager] messagesRead task cancelled")
        }

        messagesEditedTask = Task { [weak self] in
            guard let self else { return }
            var backoff: TimeInterval = 1
            var consecutiveErrors = 0

            while !Task.isCancelled {
                do {
                    print("📡 [UpdatesStreamManager] Connecting to messagesEdited stream (attempt \(consecutiveErrors + 1))...")
                    let stream = try await self.updatesRepository.subscribeMessagesEdited()
                    backoff = 1
                    consecutiveErrors = 0
                    print("✅ [UpdatesStreamManager] messagesEdited stream connected")
                    for try await event in stream {
                        let cont = await self.messagesEditedContinuation
                        cont?.yield(event)
                    }
                    print("⚠️ [UpdatesStreamManager] messagesEdited stream ended normally")
                } catch {
                    if Task.isCancelled { break }
                    consecutiveErrors += 1

                    let isAuthError = self.isAuthenticationError(error)
                    print("❌ [UpdatesStreamManager] messagesEdited stream error (#\(consecutiveErrors)): \(error.localizedDescription)")
                    print("   isAuthError: \(isAuthError), backoff: \(backoff)s")

                    if isAuthError, let coordinator = await self.tokenRefreshCoordinator {
                        do {
                            _ = try await coordinator.refreshAccessToken()
                            print("✅ [UpdatesStreamManager] Token refreshed successfully")
                        } catch {
                            print("❌ [UpdatesStreamManager] Token refresh failed: \(error.localizedDescription)")
                        }
                    }

                    try? await Task.sleep(for: .seconds(backoff))
                    backoff = min(backoff * 2, UpdatesStreamManager.maxBackoff)
                }
            }
            print("🛑 [UpdatesStreamManager] messagesEdited task cancelled")
        }

        messagesDeletedTask = Task { [weak self] in
            guard let self else { return }
            var backoff: TimeInterval = 1
            var consecutiveErrors = 0

            while !Task.isCancelled {
                do {
                    print("📡 [UpdatesStreamManager] Connecting to messagesDeleted stream (attempt \(consecutiveErrors + 1))...")
                    let stream = try await self.updatesRepository.subscribeMessagesDeleted()
                    backoff = 1
                    consecutiveErrors = 0
                    print("✅ [UpdatesStreamManager] messagesDeleted stream connected")
                    for try await event in stream {
                        let cont = await self.messagesDeletedContinuation
                        cont?.yield(event)
                    }
                    print("⚠️ [UpdatesStreamManager] messagesDeleted stream ended normally")
                } catch {
                    if Task.isCancelled { break }
                    consecutiveErrors += 1

                    let isAuthError = self.isAuthenticationError(error)
                    print("❌ [UpdatesStreamManager] messagesDeleted stream error (#\(consecutiveErrors)): \(error.localizedDescription)")
                    print("   isAuthError: \(isAuthError), backoff: \(backoff)s")

                    if isAuthError, let coordinator = await self.tokenRefreshCoordinator {
                        do {
                            _ = try await coordinator.refreshAccessToken()
                            print("✅ [UpdatesStreamManager] Token refreshed successfully")
                        } catch {
                            print("❌ [UpdatesStreamManager] Token refresh failed: \(error.localizedDescription)")
                        }
                    }

                    try? await Task.sleep(for: .seconds(backoff))
                    backoff = min(backoff * 2, UpdatesStreamManager.maxBackoff)
                }
            }
            print("🛑 [UpdatesStreamManager] messagesDeleted task cancelled")
        }
    }

    // MARK: - Error Detection

    private nonisolated func isAuthenticationError(_ error: Error) -> Bool {
        let errorString = String(describing: error).lowercased()
        return errorString.contains("unauthenticated") ||
               errorString.contains("unauthorized") ||
               errorString.contains("401") ||
               errorString.contains("token") && errorString.contains("invalid") ||
               errorString.contains("token") && errorString.contains("expired")
    }

    public func stop() {
        print("🛑 [UpdatesStreamManager] Stopping streams...")
        isRunning = false
        newMessagesTask?.cancel()
        messagesReadTask?.cancel()
        messagesEditedTask?.cancel()
        messagesDeletedTask?.cancel()
        newMessagesTask = nil
        messagesReadTask = nil
        messagesEditedTask = nil
        messagesDeletedTask = nil
        newMessagesContinuation?.finish()
        messagesReadContinuation?.finish()
        messagesEditedContinuation?.finish()
        messagesDeletedContinuation?.finish()
        connectionContinuation?.finish()
        print("✅ [UpdatesStreamManager] Streams stopped")
    }

    // MARK: - Private

    private func emitConnectionEvent(_ event: ConnectionEvent) {
        connectionContinuation?.yield(event)
    }
}
