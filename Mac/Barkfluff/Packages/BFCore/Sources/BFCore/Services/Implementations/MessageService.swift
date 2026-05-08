//
//  MessageService.swift
//  BFCore
//
//  Реализация сервиса сообщений
//

import Foundation
import BFNetworking

/// Реализация сервиса сообщений
public actor MessageService: MessageServiceProtocol {

    private let messagesRepository: MessagesRepositoryProtocol
    private let updatesRepository: UpdatesRepositoryProtocol

    public init(
        messagesRepository: MessagesRepositoryProtocol,
        updatesRepository: UpdatesRepositoryProtocol
    ) {
        self.messagesRepository = messagesRepository
        self.updatesRepository = updatesRepository
    }

    // MARK: - MessageServiceProtocol

    public func listMessages(
        chatID: String,
        fromMessageID: Int64,
        offsetBefore: Int32,
        offsetAfter: Int32
    ) async throws -> [Message] {
        let messages = try await messagesRepository.listMessages(
            chatID: chatID,
            fromMessageID: fromMessageID,
            offsetBefore: offsetBefore,
            offsetAfter: offsetAfter
        )
        return messages.map { toDomainMessage($0) }
    }

    public func sendMessage(
        chatID: String?,
        userID: Int64?,
        text: String,
        fileIDs: [String],
        forwardedMessageID: Int64?
    ) async throws -> Message {
        let messageInfo = try await messagesRepository.sendMessage(
            chatID: chatID,
            userID: userID,
            text: text,
            fileIDs: fileIDs,
            forwardedMessageID: forwardedMessageID
        )
        return toDomainMessage(messageInfo)
    }

    public func markAsRead(messageIDs: [Int64]) async throws {
        try await messagesRepository.markAsRead(messageIDs: messageIDs)
    }

    public func editMessage(
        chatID: String,
        messageID: Int64,
        text: String,
        fileIDs: [String]
    ) async throws -> Message {
        let info = try await messagesRepository.editMessage(
            messageID: messageID,
            text: text,
            fileIDs: fileIDs
        )
        // Backend не возвращает chat_id в shared.Message — подменяем его здесь.
        let domain = toDomainMessage(info)
        return Message(
            id: domain.id,
            chatID: chatID,
            senderID: domain.senderID,
            senderName: domain.senderName,
            content: domain.content,
            sentAt: domain.sentAt,
            readBy: domain.readBy,
            isSystem: domain.isSystem,
            sendingState: domain.sendingState,
            localID: domain.localID,
            uploadProgress: domain.uploadProgress,
            isEdited: true,
            editedAt: domain.editedAt ?? Date()
        )
    }

    public func deleteMessage(messageID: Int64) async throws {
        try await messagesRepository.deleteMessage(messageID: messageID)
    }

    public func subscribeToNewMessages() async throws -> AsyncThrowingStream<NewMessageEvent, Error> {
        let stream = try await updatesRepository.subscribeNewMessages()
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await event in stream {
                    let domainEvent = NewMessageEvent(
                        chatID: event.chatID,
                        message: toDomainMessage(event.message)
                    )
                    continuation.yield(domainEvent)
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func subscribeToReadEvents() async throws -> AsyncThrowingStream<MessageReadEvent, Error> {
        let stream = try await updatesRepository.subscribeMessagesRead()
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await event in stream {
                    let domainEvent = MessageReadEvent(
                        chatID: event.chatID,
                        messageID: event.messageID,
                        readBy: event.readBy
                    )
                    continuation.yield(domainEvent)
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func subscribeToEditedEvents() async throws -> AsyncThrowingStream<MessageEditedEvent, Error> {
        let stream = try await updatesRepository.subscribeMessagesEdited()
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await event in stream {
                    var domain = toDomainMessage(event.message)
                    // chatID из event, не из info (info.chatID может быть пустым).
                    domain = Message(
                        id: domain.id,
                        chatID: event.chatID,
                        senderID: domain.senderID,
                        senderName: domain.senderName,
                        content: domain.content,
                        sentAt: domain.sentAt,
                        readBy: domain.readBy,
                        isSystem: domain.isSystem,
                        sendingState: domain.sendingState,
                        localID: domain.localID,
                        uploadProgress: domain.uploadProgress,
                        isEdited: domain.isEdited,
                        editedAt: domain.editedAt
                    )
                    continuation.yield(MessageEditedEvent(chatID: event.chatID, message: domain))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func subscribeToDeletedEvents() async throws -> AsyncThrowingStream<MessageDeletedEvent, Error> {
        let stream = try await updatesRepository.subscribeMessagesDeleted()
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await event in stream {
                    continuation.yield(MessageDeletedEvent(chatID: event.chatID, messageID: event.messageID))
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    // MARK: - Private helpers

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
