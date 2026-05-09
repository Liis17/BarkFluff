//
//  Sticker.swift
//  BFCore
//
//  Доменные модели стикеров и стикерпаков
//

import Foundation

/// Набор стикеров (стикерпак)
public struct StickerPack: Identifiable, Hashable, Sendable {
    public let id: String
    public let creatorUserID: Int64
    /// ID стикера-обложки внутри пака. Для отрисовки нужно найти Sticker с таким id
    /// в содержимом пака (см. StickersService.getPack).
    public let coverStickerID: String
    public let name: String
    public let description: String
    public let createdAt: Date
    public let stickerCount: Int

    public init(
        id: String,
        creatorUserID: Int64,
        coverStickerID: String,
        name: String,
        description: String,
        createdAt: Date,
        stickerCount: Int
    ) {
        self.id = id
        self.creatorUserID = creatorUserID
        self.coverStickerID = coverStickerID
        self.name = name
        self.description = description
        self.createdAt = createdAt
        self.stickerCount = stickerCount
    }
}

/// Отдельный стикер. `fileID` идёт в sendMessage(fileIDs:),
/// `previewFileID`/`previewURL` используются для отрисовки в пикере.
public struct Sticker: Identifiable, Hashable, Sendable {
    public let id: String
    public let stickerPackID: String
    public let fileID: String
    public let previewFileID: String
    /// Эмодзи-тег (для поиска и подсказок).
    public let emoji: String
    public let addedAt: Date
    /// Presigned URL основного файла. Может протухнуть — используется как hint для MediaCacheManager.
    public let fileURL: String
    /// Presigned URL превью. Аналогично — hint.
    public let previewURL: String

    public init(
        id: String,
        stickerPackID: String,
        fileID: String,
        previewFileID: String,
        emoji: String,
        addedAt: Date,
        fileURL: String,
        previewURL: String
    ) {
        self.id = id
        self.stickerPackID = stickerPackID
        self.fileID = fileID
        self.previewFileID = previewFileID
        self.emoji = emoji
        self.addedAt = addedAt
        self.fileURL = fileURL
        self.previewURL = previewURL
    }
}
