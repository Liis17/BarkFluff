//
//  SharedMediaServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса общего медиа
//

import Foundation

/// Протокол сервиса для получения общего медиа в чате
public protocol SharedMediaServiceProtocol: Sendable {
    /// Загрузить shared media из чата
    /// - Parameters:
    ///   - chatID: ID чата
    ///   - offset: Смещение для пагинации
    ///   - filter: фильтр по типу
    /// - Returns: кортеж (items, hasMore) - массив элементов и флаг наличия ещё данных
    func loadSharedMedia(
        chatID: String,
        offset: Int32,
        filter: SharedMediaFilter
    ) async throws -> (items: [SharedMediaItem], hasMore: Bool)
}
