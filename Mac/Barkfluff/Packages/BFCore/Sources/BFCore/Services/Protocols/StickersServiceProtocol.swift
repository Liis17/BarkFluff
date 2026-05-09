//
//  StickersServiceProtocol.swift
//  BFCore
//

import Foundation

/// Сервис стикеров: список паков и стикеры внутри них.
/// Кеширует данные in-memory + персистентно (GRDB).
public protocol StickersServiceProtocol: Sendable {
    /// Список всех доступных стикерпаков.
    /// При `forceRefresh = false` сначала отдаёт из кеша; иначе всегда идёт в сеть.
    func listPacks(forceRefresh: Bool) async throws -> [StickerPack]

    /// Содержимое стикерпака: пак + все его стикеры.
    func getPack(id: String, forceRefresh: Bool) async throws -> (pack: StickerPack, stickers: [Sticker])

    /// Найти стикер по id во всех закешированных паках. Используется
    /// для восстановления списка «недавних» стикеров по их ID из UserDefaults.
    func sticker(byID id: String) async -> Sticker?

    /// Прогреть кеш изображений (превью стикеров) через MediaCacheManager.
    func prefetchStickerImages(_ stickers: [Sticker]) async

    /// Очистить in-memory кеш. GRDB-таблицы чистятся через Database.truncateAll().
    func clearCache() async
}
