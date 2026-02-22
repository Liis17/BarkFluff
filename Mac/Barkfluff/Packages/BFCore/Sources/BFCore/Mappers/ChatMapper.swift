//
//  ChatMapper.swift
//  BFCore
//
//  Маппинг чатов Proto ↔ Domain
//

import Foundation
import BFProto

/// Маппер для Chat и связанных типов
public enum ChatMapper {

    /// Преобразовать Proto Chat в Domain Chat
    public static func toDomain(_ proto: Barkfluff_Messages_Chat) -> Chat {
        Chat(
            id: proto.id,
            title: proto.title,
            pictureURL: proto.picture.isEmpty ? nil : proto.picture,
            isGroupChat: proto.isGroupChat,
            lastMessage: proto.hasLastMessage ? MessageMapper.toDomain(proto.lastMessage) : nil,
            unreadCount: proto.countUnread
        )
    }

    /// Преобразовать Proto DetailedChatMemberInfo в Domain DetailedChatMember
    public static func toDomain(_ proto: Barkfluff_Messages_ListChatMembersResponse.DetailedChatMemberInfo) -> DetailedChatMember {
        DetailedChatMember(
            userID: proto.generalInfo.userID,
            username: "",
            firstName: proto.firstName,
            lastName: proto.lastName,
            profilePictureURL: nil,
            profilePicturePreviewURL: nil,
            role: .member // По умолчанию member, т.к. в proto нет role
        )
    }
}
