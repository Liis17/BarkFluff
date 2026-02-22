//
//  Token.swift
//  BFCore
//
//  Модели для файлов и хранилища
//

import Foundation

/// Информация о загрузке файла
public struct FileUploadInfo: Hashable, Sendable {
    public let fileID: String
    public let uploadURL: String
    public let expiresIn: Int64

    public init(fileID: String, uploadURL: String, expiresIn: Int64) {
        self.fileID = fileID
        self.uploadURL = uploadURL
        self.expiresIn = expiresIn
    }
}

/// Результат проверки файла по хешу
public struct FileCheckResult: Hashable, Sendable {
    public let exists: Bool
    public let fileID: String?

    public init(exists: Bool, fileID: String? = nil) {
        self.exists = exists
        self.fileID = fileID
    }
}

/// Информация о хранилище пользователя
public struct StorageInfo: Hashable, Sendable {
    public let usedBytes: Int64
    public let limitBytes: Int64

    public init(usedBytes: Int64, limitBytes: Int64) {
        self.usedBytes = usedBytes
        self.limitBytes = limitBytes
    }

    /// Процент использованного места
    public var usedPercentage: Double {
        guard limitBytes > 0 else { return 0 }
        return Double(usedBytes) / Double(limitBytes) * 100
    }

    /// Форматированный размер использованного места
    public var formattedUsed: String {
        ByteCountFormatter.string(fromByteCount: usedBytes, countStyle: .file)
    }

    /// Форматированный лимит
    public var formattedLimit: String {
        ByteCountFormatter.string(fromByteCount: limitBytes, countStyle: .file)
    }

    /// Доступно байт
    public var availableBytes: Int64 {
        max(0, limitBytes - usedBytes)
    }
}
