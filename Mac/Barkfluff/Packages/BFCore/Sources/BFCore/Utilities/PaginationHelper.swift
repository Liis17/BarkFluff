//
//  PaginationHelper.swift
//  BFCore
//
//  Утилиты для пагинации
//

import Foundation

/// Результат пагинированного запроса
public struct PagedResult<T: Sendable>: Sendable {
    public let items: [T]
    public let totalCount: Int32
    public let offset: Int32
    public let size: Int32

    public init(items: [T], totalCount: Int32, offset: Int32, size: Int32) {
        self.items = items
        self.totalCount = totalCount
        self.offset = offset
        self.size = size
    }

    /// Есть ли ещё элементы для загрузки
    public var hasMore: Bool {
        Int32(items.count) < totalCount && offset + size < totalCount
    }

    /// Следующий offset для загрузки
    public var nextOffset: Int32 {
        offset + size
    }
}

/// Утилиты для пагинации
public enum PaginationHelper {
    /// Размер страницы по умолчанию для чатов
    public static let defaultChatsPageSize: Int32 = 30

    /// Размер страницы по умолчанию для сообщений
    public static let defaultMessagesPageSize: Int32 = 50

    /// Размер страницы по умолчанию для поиска пользователей
    public static let defaultSearchPageSize: Int32 = 20

    /// Рассчитать offset на основе текущего количества элементов
    public static func calculateOffset(currentCount: Int, pageSize: Int32) -> Int32 {
        Int32(currentCount)
    }
}
