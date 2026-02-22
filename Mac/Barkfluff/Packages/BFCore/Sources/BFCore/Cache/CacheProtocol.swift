//
//  CacheProtocol.swift
//  BFCore
//
//  Протокол кеша
//

import Foundation

/// Протокол для кеша
public protocol CacheProtocol: Actor, Sendable {
    associatedtype Key: Hashable & Sendable
    associatedtype Value: Sendable

    /// Получить значение по ключу
    func get(_ key: Key) -> Value?

    /// Сохранить значение
    func set(_ value: Value, for key: Key)

    /// Удалить значение
    func remove(_ key: Key)

    /// Очистить кеш
    func removeAll()

    /// Количество элементов
    var count: Int { get }
}
