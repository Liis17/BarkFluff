//
//  MessageMapper.swift
//  BFCore
//
//  Маппинг сообщений Proto ↔ Domain
//

import Foundation
import BFProto

/// Маппер для Message и связанных типов
public enum MessageMapper {

    /// Преобразовать Proto Message в Domain Message
    public static func toDomain(_ proto: Barkfluff_Shared_Message) -> Message {
        Message(
            id: proto.id,
            chatID: "", // Не содержится в shared.Message
            senderID: proto.senderID,
            senderName: nil,
            content: toDomain(proto.content),
            sentAt: proto.sentAt.date,
            readBy: proto.readBy,
            isSystem: proto.type == .system
        )
    }

    /// Преобразовать Proto MessageContent в Domain MessageContent
    public static func toDomain(_ proto: Barkfluff_Shared_MessageContent) -> MessageContent {
        MessageContent(
            text: proto.text,
            attachments: proto.attachments.map { toDomain($0) }
        )
    }

    /// Преобразовать Proto MessageAttachment в Domain MessageAttachment
    public static func toDomain(_ proto: Barkfluff_Shared_MessageAttachment) -> MessageAttachment {
        MessageAttachment(
            id: proto.id,
            type: toDomain(proto.type),
            fileID: proto.fileID,
            fileName: proto.fileName,
            fileSize: proto.attachmentSize,
            previewURL: proto.previewURL.isEmpty ? nil : proto.previewURL,
            previewFileID: proto.previewFileID.isEmpty ? nil : proto.previewFileID
        )
    }

    /// Преобразовать Proto AttachmentType в Domain AttachmentType
    public static func toDomain(_ proto: Barkfluff_Shared_MessageAttachmentType) -> AttachmentType {
        switch proto {
        case .image:
            return .image
        case .video:
            return .video
        case .gif:
            return .gif
        case .document:
            return .document
        case .unknown, .UNRECOGNIZED:
            return .document
        }
    }
}
