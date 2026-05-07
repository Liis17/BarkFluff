//
//  DTOs.swift
//  BFNetworking
//
//  Data Transfer Objects для репозиториев
//

import Foundation

// MARK: - Auth Tokens

/// Пара токенов (после Auth)
public struct AuthTokens: Sendable, Codable {
    public let accessToken: TokenInfo
    public let refreshToken: TokenInfo

    public init(accessToken: TokenInfo, refreshToken: TokenInfo) {
        self.accessToken = accessToken
        self.refreshToken = refreshToken
    }
}

/// Отдельный токен с датой истечения
public struct TokenInfo: Sendable, Codable, Hashable {
    public let value: String
    public let expirationDate: Date

    public init(value: String, expirationDate: Date) {
        self.value = value
        self.expirationDate = expirationDate
    }
}

// MARK: - Session

public struct SessionInfo: Sendable, Identifiable {
    public let id: Int64
    public let createdAt: Date
    public let expirationAt: Date
    public let deviceId: String
    public let originalName: String
    public let customName: String
    public let appName: String
    public let operationSystem: String
    public let location: String

    /// Отображаемое имя (кастомное если задано, иначе оригинальное)
    public var displayName: String {
        customName.isEmpty ? originalName : customName
    }
}

// MARK: - OTP

public struct OTPSetupInfo: Sendable {
    /// Base64-encoded QR code image
    public let qrCodeBase64: String
    /// Secret code for manual entry
    public let secretCode: String
}

public struct OTPStatus: Sendable {
    public let enabled: Bool
    public let setupInProgress: Bool
}

// MARK: - User

public struct UserInfo: Sendable, Identifiable {
    public let id: Int64
    public let firstName: String
    public let lastName: String
    public let username: String
    public let email: String?
    public let bio: String?
    public let profilePictureURL: String?
    public let profilePicturePreviewURL: String?
    public let registrationDate: Date
}

public struct UserBadgeInfo: Sendable, Identifiable {
    public let id: String
    public let name: String
    public let description: String
    public let iconURL: String?
}

// MARK: - Chat

public struct ChatInfo: Sendable, Identifiable {
    public let id: String
    public let title: String
    public let pictureURL: String?
    public let isGroupChat: Bool
    public let lastMessage: MessageInfo?
    public let unreadCount: Int64
    public let members: [ChatMemberInfo]

    public init(
        id: String,
        title: String,
        pictureURL: String?,
        isGroupChat: Bool,
        lastMessage: MessageInfo?,
        unreadCount: Int64,
        members: [ChatMemberInfo] = []
    ) {
        self.id = id
        self.title = title
        self.pictureURL = pictureURL
        self.isGroupChat = isGroupChat
        self.lastMessage = lastMessage
        self.unreadCount = unreadCount
        self.members = members
    }
}

public struct MessageInfo: Sendable, Identifiable {
    public let id: Int64
    public let chatID: String
    public let senderID: Int64
    public let senderName: String?
    public let content: String
    public let attachments: [MessageAttachmentInfo]
    public let sentAt: Date
    public let readBy: [Int64]
    public let isSystem: Bool
}

public struct MessageAttachmentInfo: Sendable, Identifiable {
    public let id: Int64
    public let type: AttachmentType
    public let fileID: String
    public let previewURL: String?
    public let fileName: String
    public let fileSize: Int64
    public let forwarded: ForwardedMessageDTO?

    public init(
        id: Int64,
        type: AttachmentType,
        fileID: String,
        previewURL: String?,
        fileName: String,
        fileSize: Int64,
        forwarded: ForwardedMessageDTO? = nil
    ) {
        self.id = id
        self.type = type
        self.fileID = fileID
        self.previewURL = previewURL
        self.fileName = fileName
        self.fileSize = fileSize
        self.forwarded = forwarded
    }
}

/// Сетевой снимок пересланного сообщения. Заполняется только при `type == .forwardedMessage`.
public struct ForwardedMessageDTO: Sendable {
    public let authorName: String
    public let originalMessageID: Int64
    public let text: String
    public let attachments: [MessageAttachmentInfo]

    public init(
        authorName: String,
        originalMessageID: Int64,
        text: String,
        attachments: [MessageAttachmentInfo]
    ) {
        self.authorName = authorName
        self.originalMessageID = originalMessageID
        self.text = text
        self.attachments = attachments
    }
}

public enum AttachmentType: Sendable, Codable {
    case image
    case video
    case gif
    case document
    case audio
    case voice
    case sticker
    case forwardedMessage
}

public struct ChatMemberInfo: Sendable {
    public let userID: Int64
    public let username: String
    public let firstName: String
    public let lastName: String
    public let profilePictureURL: String?
    public let role: ChatMemberRole
}

public enum ChatMemberRole: Sendable, Codable {
    case owner
    case admin
    case member
}

/// Информация о вложении в чате (для ListChatAttachments)
public struct ChatAttachmentInfo: Sendable, Identifiable {
    public let id: Int64               // ID вложения
    public let messageID: Int64        // ID сообщения
    public let type: AttachmentType    // Тип вложения
    public let fileID: String          // ID файла
    public let previewURL: String?     // URL превью (если есть)
    public let previewFileID: String?  // ID файла превью
    public let fileName: String        // Оригинальное имя файла
    public let fileSize: Int64         // Размер в байтах
    public let sentAt: Date            // Дата отправки
    public let senderID: Int64         // ID отправителя

    public init(
        id: Int64,
        messageID: Int64,
        type: AttachmentType,
        fileID: String,
        previewURL: String?,
        previewFileID: String?,
        fileName: String,
        fileSize: Int64,
        sentAt: Date,
        senderID: Int64
    ) {
        self.id = id
        self.messageID = messageID
        self.type = type
        self.fileID = fileID
        self.previewURL = previewURL
        self.previewFileID = previewFileID
        self.fileName = fileName
        self.fileSize = fileSize
        self.sentAt = sentAt
        self.senderID = senderID
    }
}

// MARK: - Files

/// Тип файла для загрузки (соответствует proto enum UploadFileType)
public enum UploadFileType: Sendable, Codable {
    case unknown
    case userAvatar
    case messageAttachmentImage
    case messageAttachmentVideo
    case messageAttachmentGif
    case messageAttachmentDocument
    case chatPicture
    case messageAttachmentAudio
    case messageAttachmentVoice
    case messageAttachmentSticker
    case userProfilePoster

    /// Определяет тип файла по расширению
    public static func from(extension ext: String) -> UploadFileType {
        switch ext.lowercased() {
        case "jpg", "jpeg", "png", "webp", "heic", "bmp":
            return .messageAttachmentImage
        case "gif":
            return .messageAttachmentGif
        case "mp4", "mov", "avi", "mkv", "webm":
            return .messageAttachmentVideo
        default:
            return .messageAttachmentDocument
        }
    }
}

/// Информация о загрузке файла
public struct FileUploadInfo: Sendable {
    public let fileID: String
    public let uploadURL: String
    public let expiresIn: Int64
}

/// Результат проверки хеша файла
public struct FileCheckResult: Sendable {
    public let exists: Bool
    public let fileID: String?
}

/// Информация о хранилище пользователя
public struct StorageInfo: Sendable {
    public let usedBytes: Int64
    public let limitBytes: Int64
    /// Использованное пространство по типам файлов (байты).
    /// Может быть пустым, если сервер не вернул breakdown.
    public let usedByType: [UploadFileType: Int64]

    public init(
        usedBytes: Int64,
        limitBytes: Int64,
        usedByType: [UploadFileType: Int64] = [:]
    ) {
        self.usedBytes = usedBytes
        self.limitBytes = limitBytes
        self.usedByType = usedByType
    }

    /// Доступное место в байтах
    public var availableBytes: Int64 {
        max(0, limitBytes - usedBytes)
    }

    /// Проверяет, поместится ли файл указанного размера
    public func canFit(fileSize: Int64) -> Bool {
        availableBytes >= fileSize
    }
}

/// Информация о файле для скачивания
public struct FileDownloadInfo: Sendable {
    public let fileID: String
    public let url: String
    public let previewURL: String?

    public init(fileID: String, url: String, previewURL: String? = nil) {
        self.fileID = fileID
        self.url = url
        self.previewURL = previewURL
    }
}

// MARK: - FileType (deprecated alias)

/// Deprecated: используйте UploadFileType
@available(*, deprecated, renamed: "UploadFileType")
public typealias FileType = UploadFileType

// MARK: - Events

public struct NewMessageEvent: Sendable {
    public let chatID: String
    public let message: MessageInfo
}

public struct MessageReadEvent: Sendable {
    public let chatID: String
    public let messageID: Int64
    public let readBy: [Int64]
}

// MARK: - FastAuth

public enum FastAuthType: Sendable, Codable {
    case qr
    case code
}

public struct FastAuthTokenInfo: Sendable {
    public let id: String
    public let token: String
    public let expiresAt: Date
}

public enum FastAuthStatus: Sendable, Codable {
    case pending
    case accepted
    case rejected
    case expired
}

public struct FastAuthResult: Sendable {
    public let fastAuthID: String
    public let status: FastAuthStatus
    public let accessToken: String?
    public let refreshToken: String?
}

public struct FastAuthRequest: Sendable {
    public let id: String
    public let deviceName: String
    public let deviceType: String
    public let requestedAt: Date
}

public struct ConnectDeviceStatus: Sendable {
    public let deviceID: String
    public let status: String
}

public struct ConnectedDeviceInfo: Sendable, Identifiable {
    public let id: String
    public let deviceName: String
    public let deviceType: String
    public let connectedAt: Date
}

// MARK: - Navigator

/// Информация о сервере из Navigator
public struct NavigatorServerInfo: Sendable, Identifiable, Hashable {
    /// Уникальный ID для SwiftUI (на основе beacon адреса)
    public var id: String { "\(host):\(port)" }

    /// Название сервера (внутреннее)
    public let name: String

    /// Описание сервера
    public let description: String

    /// Количество зарегистрированных аккаунтов
    public let accountsCount: Int64

    /// Хост Beacon-сервера
    public let host: String

    /// Порт Beacon-сервера
    public let port: Int

    /// Публичное имя сервера (для отображения)
    public let serverPublicName: String

    /// Отображаемое имя — публичное если задано, иначе внутреннее
    public var displayName: String {
        serverPublicName.isEmpty ? name : serverPublicName
    }

    /// Форматированный адрес Beacon для отображения
    public var beaconAddress: String {
        "\(host):\(port)"
    }

    public init(
        name: String,
        description: String,
        accountsCount: Int64,
        host: String,
        port: Int,
        serverPublicName: String
    ) {
        self.name = name
        self.description = description
        self.accountsCount = accountsCount
        self.host = host
        self.port = port
        self.serverPublicName = serverPublicName
    }
}
