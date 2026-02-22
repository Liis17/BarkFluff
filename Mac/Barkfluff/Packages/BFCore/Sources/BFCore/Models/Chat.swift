//
//  Chat.swift
//  BFCore
//
//  Доменные модели чата
//

import Foundation

/// Чат (диалог или группа)
public struct Chat: Identifiable, Hashable, Sendable {
    public let id: String
    public var title: String
    public var pictureURL: String?
    public var isGroupChat: Bool
    public var lastMessage: Message?
    public var unreadCount: Int64
    public var members: [ChatMember]

    public init(
        id: String,
        title: String,
        pictureURL: String? = nil,
        isGroupChat: Bool,
        lastMessage: Message? = nil,
        unreadCount: Int64 = 0,
        members: [ChatMember] = []
    ) {
        self.id = id
        self.title = title
        self.pictureURL = pictureURL
        self.isGroupChat = isGroupChat
        self.lastMessage = lastMessage
        self.unreadCount = unreadCount
        self.members = members
    }

    /// Инициалы для аватара на основе названия чата
    public var avatarInitials: String {
        let words = title.split(separator: " ")
        if words.count >= 2 {
            let first = words[0].first.map(String.init) ?? ""
            let second = words[1].first.map(String.init) ?? ""
            return first + second
        }
        return String(title.prefix(2))
    }

    /// Превью последнего сообщения
    public var lastMessagePreview: String? {
        guard let msg = lastMessage else { return nil }
        if msg.isSystem { return msg.content.text }
        if msg.content.hasText { return msg.content.text }
        if msg.content.hasAttachments, let attachment = msg.content.attachments.first {
            return attachment.type.previewText(fileName: attachment.fileName)
        }
        return nil
    }

    /// Дата последнего сообщения
    public var lastMessageDate: Date? {
        lastMessage?.sentAt
    }

    /// Есть ли непрочитанные сообщения
    public var hasUnread: Bool {
        unreadCount > 0
    }
}

/// Участник чата (краткая информация)
public struct ChatMember: Identifiable, Hashable, Sendable {
    public var id: Int64 { userID }

    public let userID: Int64
    public let username: String
    public let firstName: String
    public let lastName: String
    public let profilePictureURL: String?
    public let role: ChatMemberRole

    public init(
        userID: Int64,
        username: String,
        firstName: String,
        lastName: String,
        profilePictureURL: String? = nil,
        role: ChatMemberRole
    ) {
        self.userID = userID
        self.username = username
        self.firstName = firstName
        self.lastName = lastName
        self.profilePictureURL = profilePictureURL
        self.role = role
    }

    public var displayName: String {
        "\(firstName) \(lastName)"
    }

    public var initials: String {
        let first = firstName.first.map(String.init) ?? ""
        let last = lastName.first.map(String.init) ?? ""
        return first + last
    }
}

/// Роль участника чата
public enum ChatMemberRole: String, Sendable, Codable, CaseIterable {
    case owner
    case admin
    case member

    public var displayName: String {
        switch self {
        case .owner: return "Владелец"
        case .admin: return "Администратор"
        case .member: return "Участник"
        }
    }
}

/// Расширенная информация об участнике чата
public struct DetailedChatMember: Identifiable, Hashable, Sendable {
    public var id: Int64 { userID }

    public let userID: Int64
    public let username: String
    public let firstName: String
    public let lastName: String
    public let profilePictureURL: String?
    public let profilePicturePreviewURL: String?
    public let role: ChatMemberRole

    public init(
        userID: Int64,
        username: String,
        firstName: String,
        lastName: String,
        profilePictureURL: String? = nil,
        profilePicturePreviewURL: String? = nil,
        role: ChatMemberRole
    ) {
        self.userID = userID
        self.username = username
        self.firstName = firstName
        self.lastName = lastName
        self.profilePictureURL = profilePictureURL
        self.profilePicturePreviewURL = profilePicturePreviewURL
        self.role = role
    }

    public var displayName: String {
        "\(firstName) \(lastName)"
    }

    public var initials: String {
        let first = firstName.first.map(String.init) ?? ""
        let last = lastName.first.map(String.init) ?? ""
        return first + last
    }
}
