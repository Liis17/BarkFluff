//
//  CacheStats.swift
//  BFCore
//

import Foundation

/// Снимок статистики дискового кеша.
public struct CacheStats: Sendable {
    /// Размер на диске по каждому типу (байты).
    public let bytesByType: [CachedFileType: Int64]

    /// Сумма по всем типам (байты).
    public var totalBytes: Int64 {
        bytesByType.values.reduce(0, +)
    }

    public init(bytesByType: [CachedFileType: Int64]) {
        self.bytesByType = bytesByType
    }

    public static let empty = CacheStats(bytesByType: [:])

    /// Доля типа от общего размера (0...1). Для пустого кеша — 0.
    public func fraction(for type: CachedFileType) -> Double {
        guard totalBytes > 0 else { return 0 }
        return Double(bytesByType[type] ?? 0) / Double(totalBytes)
    }
}
