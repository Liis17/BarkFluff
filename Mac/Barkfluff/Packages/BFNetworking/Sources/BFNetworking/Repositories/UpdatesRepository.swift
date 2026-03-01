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
                                let attachments = msg.content.attachments.map { att in
                                    MessageAttachmentInfo(
                                        id: att.id,
                                        type: self.mapAttachmentType(att.type),
                                        fileID: att.fileID,
                                        previewURL: att.previewURL.isEmpty ? nil : att.previewURL,
                                        fileName: att.fileName,
                                        fileSize: att.attachmentSize
                                    )
                                }
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

    // MARK: - Helpers

    private nonisolated func mapAttachmentType(_ type: Barkfluff_Shared_MessageAttachmentType) -> AttachmentType {
        switch type {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        default: return .document
        }
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

    private var newMessagesContinuation: AsyncStream<NewMessageEvent>.Continuation?
    private var messagesReadContinuation: AsyncStream<MessageReadEvent>.Continuation?
    private var connectionContinuation: AsyncStream<ConnectionEvent>.Continuation?

    public private(set) var newMessages: AsyncStream<NewMessageEvent>
    public private(set) var messageReads: AsyncStream<MessageReadEvent>
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
        newMessagesTask = nil
        messagesReadTask = nil
        newMessagesContinuation?.finish()
        messagesReadContinuation?.finish()
        connectionContinuation?.finish()
        print("✅ [UpdatesStreamManager] Streams stopped")
    }

    // MARK: - Private

    private func emitConnectionEvent(_ event: ConnectionEvent) {
        connectionContinuation?.yield(event)
    }
}
