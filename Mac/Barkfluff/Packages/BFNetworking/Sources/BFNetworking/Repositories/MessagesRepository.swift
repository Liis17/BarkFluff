//
//  MessagesRepository.swift
//  BFNetworking
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

public actor MessagesRepository: MessagesRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func listChats(offset: Int32, size: Int32) async throws -> (chats: [ChatInfo], totalCount: Int32) {
        var request = Barkfluff_Messages_ListChatsRequest()
        var pagination = Barkfluff_Shared_PageRequest()
        pagination.offset = offset
        pagination.size = size
        request.pagination = pagination
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.listChats(req)

                let chats = response.chats.map { chat in
                    ChatInfo(
                        id: chat.id,
                        title: chat.title,
                        pictureURL: chat.picture.isEmpty ? nil : chat.picture,
                        isGroupChat: chat.isGroupChat,
                        lastMessage: chat.hasLastMessage ? self.mapMessage(chat.lastMessage, chatID: chat.id) : nil,
                        unreadCount: chat.countUnread,
                        members: chat.members.map { self.mapChatMember($0) }
                    )
                }
                return (chats: chats, totalCount: response.totalCount)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func listMessages(chatID: String, fromMessageID: Int64, offsetBefore: Int32, offsetAfter: Int32) async throws -> [MessageInfo] {
        var request = Barkfluff_Messages_ListMessagesRequest()
        request.chatID = chatID
        request.fromMessageID = fromMessageID
        request.offsetBefore = offsetBefore
        request.offsetAfter = offsetAfter
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.listMessages(req)
                return response.messages.map { self.mapMessage($0, chatID: chatID) }
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func sendMessage(chatID: String?, userID: Int64?, text: String, fileIDs: [String], forwardedMessageID: Int64?) async throws -> MessageInfo {
        var request = Barkfluff_Messages_SendMessageRequest()
        if let chatID {
            request.sourceID = .chatID(chatID)
        } else if let userID {
            request.sourceID = .userID(userID)
        }
        var outgoing = Barkfluff_Messages_OutgoingMessage()
        outgoing.text = text
        outgoing.filesIds = fileIDs
        if let forwardedMessageID {
            outgoing.forwardedMessageID = forwardedMessageID
        }
        request.message = outgoing
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.sendMessage(req)
                let resolvedChatID = chatID ?? ""
                return self.mapMessage(response.message, chatID: resolvedChatID)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func createGroupChat(userIDs: [Int64], title: String, pictureFileID: String?) async throws -> ChatInfo {
        var request = Barkfluff_Messages_CreateGroupChatRequest()
        request.userIds = userIDs
        request.title = title
        request.pictureFileID = pictureFileID ?? ""
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.createGroupChat(req)
                let chat = response.createdChat
                return ChatInfo(
                    id: chat.id,
                    title: chat.title,
                    pictureURL: chat.picture.isEmpty ? nil : chat.picture,
                    isGroupChat: chat.isGroupChat,
                    lastMessage: chat.hasLastMessage ? self.mapMessage(chat.lastMessage, chatID: chat.id) : nil,
                    unreadCount: chat.countUnread,
                    members: chat.members.map { self.mapChatMember($0) }
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func kickUser(chatID: String, userID: Int64) async throws {
        var request = Barkfluff_Messages_KickUserRequest()
        request.chatID = chatID
        request.userID = userID
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                _ = try await messagesClient.kickUser(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func markAsRead(messageIDs: [Int64]) async throws {
        var request = Barkfluff_Messages_MarkAsReadRequest()
        request.messageIds = messageIDs
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                _ = try await messagesClient.markAsRead(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func listChatMembers(chatID: String, offset: Int32, size: Int32) async throws -> (members: [ChatMemberInfo], totalCount: Int32) {
        var request = Barkfluff_Messages_ListChatMembersRequest()
        request.chatID = chatID
        var pagination = Barkfluff_Shared_PageRequest()
        pagination.offset = offset
        pagination.size = size
        request.pagination = pagination
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.listChatMembers(req)
                let members = response.chatMembers.map { member in
                    ChatMemberInfo(
                        userID: member.generalInfo.userID,
                        username: "",
                        firstName: member.firstName,
                        lastName: member.lastName,
                        profilePictureURL: nil,
                        role: .member
                    )
                }
                return (members: members, totalCount: response.totalCount)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func listChatAttachments(
        chatID: String,
        offset: Int32,
        size: Int32,
        filter: AttachmentType?,
        sortDescending: Bool
    ) async throws -> (attachments: [ChatAttachmentInfo], totalCount: Int32) {
        var request = Barkfluff_Messages_ListChatAttachmentsRequest()
        request.chatID = chatID

        var pagination = Barkfluff_Shared_PageRequest()
        pagination.offset = offset
        pagination.size = size
        request.pagination = pagination

        // Маппинг фильтра
        request.attachmentType = mapAttachmentTypeToProto(filter)
        request.sortDescending = sortDescending

        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.listChatAttachments(req)

                let attachments = response.attachments.compactMap { info -> ChatAttachmentInfo? in
                    guard info.hasAttachment else { return nil }
                    let att = info.attachment

                    // Пропускаем неизвестные типы
                    let type = mapAttachmentTypeFromProto(att.type)
                    guard type != nil else { return nil }

                    let sentAt: Date
                    if info.hasSentAt {
                        sentAt = Date(
                            timeIntervalSince1970: TimeInterval(info.sentAt.seconds)
                                + TimeInterval(info.sentAt.nanos) / 1_000_000_000
                        )
                    } else {
                        sentAt = Date()
                    }

                    return ChatAttachmentInfo(
                        id: att.id,
                        messageID: info.messageID,
                        type: type!,
                        fileID: att.fileID,
                        previewURL: att.previewURL.isEmpty ? nil : att.previewURL,
                        previewFileID: att.previewFileID.isEmpty ? nil : att.previewFileID,
                        fileName: att.fileName,
                        fileSize: att.attachmentSize,
                        sentAt: sentAt,
                        senderID: info.senderID
                    )
                }

                return (attachments: attachments, totalCount: response.totalCount)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func getPersonChatId(userID: Int64) async throws -> String? {
        var request = Barkfluff_Messages_GetPersonChatIdRequest()
        request.userID = userID
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.getPersonChatId(req)
                return response.chatID.isEmpty ? nil : response.chatID
            }
        } catch let error as RPCError {
            if error.code == .notFound {
                return nil
            }
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Private Attachment Type Mapping

    private nonisolated func mapAttachmentTypeToProto(_ type: AttachmentType?) -> Barkfluff_Shared_MessageAttachmentType {
        guard let type else { return .unknown } // unknown = все типы
        switch type {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        case .audio: return .unknown
        case .voice: return .unknown
        case .sticker: return .unknown
        case .forwardedMessage: return .unknown
        }
    }

    private nonisolated func mapAttachmentTypeFromProto(_ type: Barkfluff_Shared_MessageAttachmentType) -> AttachmentType? {
        switch type {
        case .image: return .image
        case .video: return .video
        case .gif: return .gif
        case .document: return .document
        case .forwardedMessage: return .forwardedMessage
        default: return nil // unknown и другие - пропускаем
        }
    }

    // MARK: - Mapping Helpers

    private nonisolated func mapMessage(_ msg: Barkfluff_Shared_Message, chatID: String) -> MessageInfo {
        let sentAt: Date
        if msg.hasSentAt {
            sentAt = Date(timeIntervalSince1970: TimeInterval(msg.sentAt.seconds) + TimeInterval(msg.sentAt.nanos) / 1_000_000_000)
        } else {
            sentAt = Date()
        }

        let attachments = msg.content.attachments.map { mapAttachment($0) }

        return MessageInfo(
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

    /// Маппинг attachment внутри ForwardedMessage — рекурсия запрещена контрактом backend,
    /// поэтому никогда не читаем `hasForwardedMessage` повторно.
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

    private nonisolated func mapChatMember(_ member: Barkfluff_Messages_ChatMember) -> ChatMemberInfo {
        ChatMemberInfo(
            userID: member.userID,
            username: "",
            firstName: "",
            lastName: "",
            profilePictureURL: nil,
            role: .member
        )
    }

}
