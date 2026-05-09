//
//  StickersMapper.swift
//  BFCore
//
//  Маппинг стикеров: DTO ↔ Domain ↔ GRDB Record.
//

import Foundation
import BFNetworking
import GRDB

public enum StickersMapper {

    // MARK: - DTO → Domain

    public static func toDomain(_ dto: StickerPackInfoDTO) -> StickerPack {
        StickerPack(
            id: dto.id,
            creatorUserID: dto.creatorUserID,
            coverStickerID: dto.coverStickerID,
            name: dto.name,
            description: dto.description,
            createdAt: dto.createdAt,
            stickerCount: Int(dto.stickerCount)
        )
    }

    public static func toDomain(_ dto: StickerInfoDTO) -> Sticker {
        Sticker(
            id: dto.id,
            stickerPackID: dto.stickerPackID,
            fileID: dto.fileID,
            previewFileID: dto.previewFileID,
            emoji: dto.emoji,
            addedAt: dto.addedAt,
            fileURL: dto.fileURL,
            previewURL: dto.previewURL
        )
    }

    // MARK: - Domain → Record

    static func toRecord(_ pack: StickerPack, updatedAt: Date = Date()) -> CachedStickerPackRecord {
        CachedStickerPackRecord(
            id: pack.id,
            creatorUserId: pack.creatorUserID,
            coverStickerId: pack.coverStickerID,
            name: pack.name,
            description: pack.description,
            createdAt: Int64(pack.createdAt.timeIntervalSince1970),
            stickerCount: pack.stickerCount,
            updatedAt: Int64(updatedAt.timeIntervalSince1970)
        )
    }

    static func toRecord(_ sticker: Sticker) -> CachedStickerRecord {
        CachedStickerRecord(
            id: sticker.id,
            packId: sticker.stickerPackID,
            fileId: sticker.fileID,
            previewFileId: sticker.previewFileID,
            emoji: sticker.emoji,
            addedAt: Int64(sticker.addedAt.timeIntervalSince1970),
            fileUrl: sticker.fileURL.isEmpty ? nil : sticker.fileURL,
            previewUrl: sticker.previewURL.isEmpty ? nil : sticker.previewURL
        )
    }

    // MARK: - Record → Domain

    static func toDomain(_ record: CachedStickerPackRecord) -> StickerPack {
        StickerPack(
            id: record.id,
            creatorUserID: record.creatorUserId,
            coverStickerID: record.coverStickerId,
            name: record.name,
            description: record.description,
            createdAt: Date(timeIntervalSince1970: TimeInterval(record.createdAt)),
            stickerCount: record.stickerCount
        )
    }

    static func toDomain(_ record: CachedStickerRecord) -> Sticker {
        Sticker(
            id: record.id,
            stickerPackID: record.packId,
            fileID: record.fileId,
            previewFileID: record.previewFileId,
            emoji: record.emoji,
            addedAt: Date(timeIntervalSince1970: TimeInterval(record.addedAt)),
            fileURL: record.fileUrl ?? "",
            previewURL: record.previewUrl ?? ""
        )
    }
}
