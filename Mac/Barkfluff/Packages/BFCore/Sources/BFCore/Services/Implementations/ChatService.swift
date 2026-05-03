//
//  ChatService.swift
//  BFCore
//
//  Реализация сервиса чатов
//

import Foundation
import BFNetworking

/// Реализация сервиса чатов
public actor ChatService: ChatServiceProtocol {

    private let messagesRepository: MessagesRepositoryProtocol

    public init(messagesRepository: MessagesRepositoryProtocol) {
        self.messagesRepository = messagesRepository
    }

    // MARK: - ChatServiceProtocol

    public func listChats(offset: Int32, size: Int32) async throws -> PagedResult<Chat> {
        let result = try await messagesRepository.listChats(offset: offset, size: size)
        let chats = result.chats.map { chatInfo in
            Chat(
                id: chatInfo.id,
                title: chatInfo.title,
                pictureURL: chatInfo.pictureURL,
                isGroupChat: chatInfo.isGroupChat,
                lastMessage: chatInfo.lastMessage.map { toDomainMessage($0) },
                unreadCount: chatInfo.unreadCount,
                members: chatInfo.members.map { toDomainMember($0) }
            )
        }.sorted { lhs, rhs in
            let lhsDate = lhs.lastMessage?.sentAt ?? .distantPast
            let rhsDate = rhs.lastMessage?.sentAt ?? .distantPast
            return lhsDate > rhsDate
        }
        return PagedResult(items: chats, totalCount: result.totalCount, offset: offset, size: size)
    }

    public func listChatMembers(chatID: String, offset: Int32, size: Int32) async throws -> PagedResult<DetailedChatMember> {
        let result = try await messagesRepository.listChatMembers(chatID: chatID, offset: offset, size: size)
        let members = result.members.map { member in
            DetailedChatMember(
                userID: member.userID,
                username: member.username,
                firstName: member.firstName,
                lastName: member.lastName,
                profilePictureURL: member.profilePictureURL,
                profilePicturePreviewURL: nil,
                role: toDomainRole(member.role)
            )
        }
        return PagedResult(items: members, totalCount: result.totalCount, offset: offset, size: size)
    }

    public func createGroupChat(userIDs: [Int64], title: String, pictureFileID: String?) async throws -> Chat {
        let chatInfo = try await messagesRepository.createGroupChat(
            userIDs: userIDs,
            title: title,
            pictureFileID: pictureFileID
        )
        return Chat(
            id: chatInfo.id,
            title: chatInfo.title,
            pictureURL: chatInfo.pictureURL,
            isGroupChat: chatInfo.isGroupChat,
            lastMessage: chatInfo.lastMessage.map { toDomainMessage($0) },
            unreadCount: chatInfo.unreadCount,
            members: chatInfo.members.map { toDomainMember($0) }
        )
    }

    public func kickUser(chatID: String, userID: Int64) async throws {
        try await messagesRepository.kickUser(chatID: chatID, userID: userID)
    }

    public func getPersonChatId(userID: Int64) async throws -> String? {
        try await messagesRepository.getPersonChatId(userID: userID)
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
            isSystem: info.isSystem
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

    private func toDomainRole(_ role: BFNetworking.ChatMemberRole) -> ChatMemberRole {
        switch role {
        case .owner: return .owner
        case .admin: return .admin
        case .member: return .member
        }
    }

    private func toDomainMember(_ info: ChatMemberInfo) -> ChatMember {
        ChatMember(
            userID: info.userID,
            username: info.username,
            firstName: info.firstName,
            lastName: info.lastName,
            profilePictureURL: info.profilePictureURL,
            role: toDomainRole(info.role)
        )
    }
}
