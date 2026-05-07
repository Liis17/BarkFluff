//
//  SerializationDTO.swift
//  BFCore
//
//  Codable DTO для сериализации доменных моделей в SQLite (BLOB JSON).
//  Используются `LocalChatRepository` и `LocalMessageRepository`.
//

import Foundation

struct MessageDTO: Codable {
    let id: Int64
    let chatID: String
    let senderID: Int64
    let senderName: String?
    let text: String
    let attachments: [MessageAttachmentDTO]
    let sentAt: Int64 // milliseconds since epoch
    let readBy: [Int64]
    let isSystem: Bool

    init(message: Message) {
        self.id = message.id
        self.chatID = message.chatID
        self.senderID = message.senderID
        self.senderName = message.senderName
        self.text = message.content.text
        self.attachments = message.content.attachments.map(MessageAttachmentDTO.init(attachment:))
        self.sentAt = Int64(message.sentAt.timeIntervalSince1970 * 1000)
        self.readBy = message.readBy
        self.isSystem = message.isSystem
    }

    func toMessage() -> Message {
        Message(
            id: id,
            chatID: chatID,
            senderID: senderID,
            senderName: senderName,
            content: MessageContent(
                text: text,
                attachments: attachments.map { $0.toAttachment() }
            ),
            sentAt: Date(timeIntervalSince1970: TimeInterval(sentAt) / 1000),
            readBy: readBy,
            isSystem: isSystem,
            sendingState: .sent
        )
    }
}

struct MessageAttachmentDTO: Codable {
    let id: Int64
    let typeRaw: String
    let fileID: String
    let fileName: String
    let fileSize: Int64
    let previewURL: String?
    let previewFileID: String?
    let forwarded: ForwardedMessagePayloadDTO?

    init(attachment: MessageAttachment) {
        self.id = attachment.id
        self.typeRaw = attachment.type.rawValue
        self.fileID = attachment.fileID
        self.fileName = attachment.fileName
        self.fileSize = attachment.fileSize
        self.previewURL = attachment.previewURL
        self.previewFileID = attachment.previewFileID
        self.forwarded = attachment.forwarded.map(ForwardedMessagePayloadDTO.init(payload:))
    }

    func toAttachment() -> MessageAttachment {
        MessageAttachment(
            id: id,
            type: AttachmentType(rawValue: typeRaw) ?? .document,
            fileID: fileID,
            fileName: fileName,
            fileSize: fileSize,
            previewURL: previewURL,
            previewFileID: previewFileID,
            forwarded: forwarded?.toPayload()
        )
    }
}

struct ForwardedMessagePayloadDTO: Codable {
    let authorName: String
    let originalMessageID: Int64
    let text: String
    let attachments: [MessageAttachmentDTO]

    init(payload: ForwardedMessagePayload) {
        self.authorName = payload.authorName
        self.originalMessageID = payload.originalMessageID
        self.text = payload.text
        self.attachments = payload.attachments.map(MessageAttachmentDTO.init(attachment:))
    }

    func toPayload() -> ForwardedMessagePayload {
        ForwardedMessagePayload(
            authorName: authorName,
            originalMessageID: originalMessageID,
            text: text,
            attachments: attachments.map { $0.toAttachment() }
        )
    }
}
