//
//  InMemoryCache.swift
//  BFCore
//
//  In-memory кеш с TTL
//

import Foundation

/// In-memory кеш с поддержкой TTL (time-to-live)
public actor InMemoryCache<Key: Hashable & Sendable, Value: Sendable>: CacheProtocol {

    private struct CacheEntry: Sendable {
        let value: Value
        let expiresAt: Date?

        var isExpired: Bool {
            guard let expiresAt else { return false }
            return Date() > expiresAt
        }
    }

    private var storage: [Key: CacheEntry] = [:]
    private let defaultTTL: TimeInterval?

    /// Создать кеш без TTL
    public init() {
        self.defaultTTL = nil
    }

    /// Создать кеш с дефолтным TTL
    public init(defaultTTL: TimeInterval) {
        self.defaultTTL = defaultTTL
    }

    // MARK: - CacheProtocol

    public func get(_ key: Key) -> Value? {
        guard let entry = storage[key] else { return nil }

        if entry.isExpired {
            storage.removeValue(forKey: key)
            return nil
        }

        return entry.value
    }

    public func set(_ value: Value, for key: Key) {
        let expiresAt = defaultTTL.map { Date().addingTimeInterval($0) }
        storage[key] = CacheEntry(value: value, expiresAt: expiresAt)
    }

    /// Сохранить значение с кастомным TTL
    public func set(_ value: Value, for key: Key, ttl: TimeInterval) {
        let expiresAt = Date().addingTimeInterval(ttl)
        storage[key] = CacheEntry(value: value, expiresAt: expiresAt)
    }

    public func remove(_ key: Key) {
        storage.removeValue(forKey: key)
    }

    public func removeAll() {
        storage.removeAll()
    }

    public var count: Int {
        storage.count
    }

    // MARK: - Additional methods

    /// Удалить все просроченные записи
    public func removeExpired() {
        storage = storage.filter { !$0.value.isExpired }
    }

    /// Проверить наличие ключа (без проверки TTL)
    public func contains(_ key: Key) -> Bool {
        guard let entry = storage[key] else { return false }
        return !entry.isExpired
    }

    /// Получить все ключи
    public var keys: [Key] {
        Array(storage.keys)
    }
}

// MARK: - Специализированные кеши

/// Кеш пользователей
public actor UserCache {
    private let cache = InMemoryCache<Int64, User>()

    public init() {}

    public func getUser(userID: Int64) async -> User? {
        await cache.get(userID)
    }

    public func setUser(_ user: User) async {
        await cache.set(user, for: user.id)
    }

    public func removeUser(userID: Int64) async {
        await cache.remove(userID)
    }

    public func removeAll() async {
        await cache.removeAll()
    }
}

/// Кеш чатов
public actor ChatCache {
    private let cache = InMemoryCache<String, Chat>()

    public init() {}

    public func getChat(chatID: String) async -> Chat? {
        await cache.get(chatID)
    }

    public func setChat(_ chat: Chat) async {
        await cache.set(chat, for: chat.id)
    }

    public func removeChat(chatID: String) async {
        await cache.remove(chatID)
    }

    public func removeAll() async {
        await cache.removeAll()
    }
}
