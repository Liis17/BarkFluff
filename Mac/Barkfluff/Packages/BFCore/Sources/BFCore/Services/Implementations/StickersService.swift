//
//  StickersService.swift
//  BFCore
//
//  Сервис стикерпаков: in-memory кеш + GRDB upsert + префетч превью.
//

import Foundation
import BFNetworking
import GRDB

public actor StickersService: StickersServiceProtocol {

    private let repository: StickersRepositoryProtocol
    private let mediaCacheManager: MediaCacheManager
    private let database: Database

    /// Список паков (если уже загружен).
    private var packsCache: [StickerPack]?
    /// Содержимое паков по id.
    private var packContentCache: [String: (pack: StickerPack, stickers: [Sticker])] = [:]

    public init(
        repository: StickersRepositoryProtocol,
        mediaCacheManager: MediaCacheManager,
        database: Database
    ) {
        self.repository = repository
        self.mediaCacheManager = mediaCacheManager
        self.database = database
    }

    // MARK: - Public API

    public func listPacks(forceRefresh: Bool = false) async throws -> [StickerPack] {
        if !forceRefresh, let cached = packsCache {
            return cached
        }

        // 1. Если памяти ничего нет — пробуем GRDB.
        if !forceRefresh, packsCache == nil {
            if let local = try? await loadPacksFromDB(), !local.isEmpty {
                packsCache = local
                // Восстанавливаем содержимое паков в in-memory из GRDB,
                // чтобы UI мгновенно показал обложки и сетку.
                await restorePackContentsFromDB(packs: local)
                // Фоново обновим с сервера.
                Task.detached { [weak self] in
                    _ = try? await self?.refreshPacksFromServer()
                }
                return local
            }
        }

        // 2. Идём в сеть.
        return try await refreshPacksFromServer()
    }

    public func getPack(id: String, forceRefresh: Bool = false) async throws -> (pack: StickerPack, stickers: [Sticker]) {
        if !forceRefresh, let cached = packContentCache[id] {
            return cached
        }

        // GRDB fallback.
        if !forceRefresh {
            if let local = try? await loadPackFromDB(id: id), !local.stickers.isEmpty {
                packContentCache[id] = local
                Task.detached { [weak self] in
                    _ = try? await self?.refreshPackContent(id: id)
                }
                return local
            }
        }

        return try await refreshPackContent(id: id)
    }

    public func sticker(byID id: String) async -> Sticker? {
        for (_, content) in packContentCache {
            if let s = content.stickers.first(where: { $0.id == id }) {
                return s
            }
        }
        // Пробуем GRDB.
        return try? await database.dbPool.read { db in
            try CachedStickerRecord
                .filter(CachedStickerRecord.Columns.id == id)
                .fetchOne(db)
                .map(StickersMapper.toDomain)
        }
    }

    public func prefetchStickerImages(_ stickers: [Sticker]) async {
        let ids = stickers
            .map { $0.previewFileID.isEmpty ? $0.fileID : $0.previewFileID }
            .filter { !$0.isEmpty }
        await mediaCacheManager.prefetch(fileIDs: ids, type: .sticker)
    }

    public func clearCache() async {
        packsCache = nil
        packContentCache.removeAll()
    }

    // MARK: - Private — Network

    @discardableResult
    private func refreshPacksFromServer() async throws -> [StickerPack] {
        let page = try await repository.listStickerPacks(offset: 0, size: 200)
        let packs = page.packs.map(StickersMapper.toDomain)
        packsCache = packs
        try? await persistPacks(packs)

        // Префетчим содержимое всех паков параллельно — это даёт обложки в табах
        // и быстрый показ сетки при открытии любого пака.
        await withTaskGroup(of: Void.self) { group in
            for pack in packs {
                group.addTask { [weak self] in
                    _ = try? await self?.refreshPackContent(id: pack.id)
                }
            }
        }
        return packs
    }

    @discardableResult
    private func refreshPackContent(id: String) async throws -> (pack: StickerPack, stickers: [Sticker]) {
        let content = try await repository.getStickerPack(packID: id)
        let pack = StickersMapper.toDomain(content.pack)
        let stickers = content.stickers.map(StickersMapper.toDomain)
        let entry = (pack: pack, stickers: stickers)
        packContentCache[id] = entry
        try? await persistPackContent(pack: pack, stickers: stickers)
        return entry
    }

    // MARK: - Private — GRDB

    private func loadPacksFromDB() async throws -> [StickerPack] {
        try await database.dbPool.read { db in
            try CachedStickerPackRecord
                .order(CachedStickerPackRecord.Columns.createdAt.asc)
                .fetchAll(db)
                .map(StickersMapper.toDomain)
        }
    }

    private func loadPackFromDB(id: String) async throws -> (pack: StickerPack, stickers: [Sticker])? {
        try await database.dbPool.read { db in
            guard let packRecord = try CachedStickerPackRecord
                .filter(CachedStickerPackRecord.Columns.id == id)
                .fetchOne(db)
            else { return nil }
            let pack = StickersMapper.toDomain(packRecord)
            let stickers = try CachedStickerRecord
                .filter(CachedStickerRecord.Columns.packId == id)
                .order(CachedStickerRecord.Columns.addedAt.asc)
                .fetchAll(db)
                .map(StickersMapper.toDomain)
            return (pack, stickers)
        }
    }

    private func restorePackContentsFromDB(packs: [StickerPack]) async {
        for pack in packs {
            if let local = try? await loadPackFromDB(id: pack.id), !local.stickers.isEmpty {
                packContentCache[pack.id] = local
            }
        }
    }

    private func persistPacks(_ packs: [StickerPack]) async throws {
        let now = Date()
        try await database.dbPool.write { db in
            for pack in packs {
                try StickersMapper.toRecord(pack, updatedAt: now).upsert(db)
            }
        }
    }

    private func persistPackContent(pack: StickerPack, stickers: [Sticker]) async throws {
        let now = Date()
        try await database.dbPool.write { db in
            try StickersMapper.toRecord(pack, updatedAt: now).upsert(db)
            // Удаляем старые стикеры пака и пишем актуальный набор.
            try CachedStickerRecord
                .filter(CachedStickerRecord.Columns.packId == pack.id)
                .deleteAll(db)
            for sticker in stickers {
                try StickersMapper.toRecord(sticker).upsert(db)
            }
        }
    }
}
