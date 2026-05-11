//
//  UserDefaultsTokenProvider.swift
//  BFNetworking
//
//  Хранение токенов в UserDefaults с in-memory кэшем
//  НЕ требует пароля keychain
//

import Foundation

/// Ключи для UserDefaults
private enum UDKeys {
    static let prefix = "com.barkfluff.tokens."
    static let accessToken = prefix + "access_token"
    static let accessTokenExpiresAt = prefix + "access_token_expires_at"
    static let refreshToken = prefix + "refresh_token"
    static let refreshTokenExpiresAt = prefix + "refresh_token_expires_at"
    static let serverHost = prefix + "server_host"
    static let serverPort = prefix + "server_port"
    static let deviceId = prefix + "device_id"
}

/// Потокобезопасный in-memory кэш
private struct TokenCache: Sendable {
    var accessToken: String?
    var accessTokenExpiresAt: Date?
    var refreshToken: String?
    var refreshTokenExpiresAt: Date?
    var serverHost: String?
    var serverPort: Int?
    var deviceId: String?
    var isLoaded: Bool = false
}

/// Провайдер токенов с хранением в UserDefaults.
/// ВАЖНО: Менее безопасно чем Keychain, но НЕ требует пароля.
/// Использует in-memory кэш для минимизации I/O.
public actor UserDefaultsTokenProvider: TokenProvider {

    /// UserDefaults для хранения (можно переопределить для тестов)
    private let userDefaults: UserDefaults

    /// In-memory кэш для избежания частых чтений
    private var cache: TokenCache = TokenCache()

    /// Дата-форматтер для ISO8601
    private let dateFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    // MARK: - Init

    public init(userDefaults: UserDefaults = .standard) {
        self.userDefaults = userDefaults
    }

    // MARK: - Cache Management

    /// Загружает все данные в кэш (лениво, один раз)
    private func ensureCacheLoaded() {
        guard !cache.isLoaded else { return }

        cache = TokenCache(
            accessToken: userDefaults.string(forKey: UDKeys.accessToken),
            accessTokenExpiresAt: loadDate(forKey: UDKeys.accessTokenExpiresAt),
            refreshToken: userDefaults.string(forKey: UDKeys.refreshToken),
            refreshTokenExpiresAt: loadDate(forKey: UDKeys.refreshTokenExpiresAt),
            serverHost: userDefaults.string(forKey: UDKeys.serverHost),
            serverPort: userDefaults.integer(forKey: UDKeys.serverPort) == 0 ? nil : userDefaults.integer(forKey: UDKeys.serverPort),
            deviceId: userDefaults.string(forKey: UDKeys.deviceId),
            isLoaded: true
        )
    }

    private func loadDate(forKey key: String) -> Date? {
        guard let str = userDefaults.string(forKey: key) else { return nil }
        return dateFormatter.date(from: str)
    }

    private func saveDate(_ date: Date, forKey key: String) {
        userDefaults.set(dateFormatter.string(from: date), forKey: key)
    }

    // MARK: - Access Token

    public var currentAccessToken: String? {
        ensureCacheLoaded()
        return cache.accessToken
    }

    public var accessTokenExpirationDate: Date? {
        ensureCacheLoaded()
        return cache.accessTokenExpiresAt
    }

    // MARK: - Refresh Token

    public var currentRefreshToken: String? {
        ensureCacheLoaded()
        return cache.refreshToken
    }

    public var refreshTokenExpirationDate: Date? {
        ensureCacheLoaded()
        return cache.refreshTokenExpiresAt
    }

    public var hasRefreshToken: Bool {
        ensureCacheLoaded()
        guard let token = cache.refreshToken, !token.isEmpty else { return false }
        return true
    }

    // MARK: - Save

    public func saveAccessToken(value: String, expiresAt: Date) {
        ensureCacheLoaded()
        cache.accessToken = value
        cache.accessTokenExpiresAt = expiresAt
        userDefaults.set(value, forKey: UDKeys.accessToken)
        saveDate(expiresAt, forKey: UDKeys.accessTokenExpiresAt)
    }

    public func saveRefreshToken(value: String, expiresAt: Date) {
        ensureCacheLoaded()
        cache.refreshToken = value
        cache.refreshTokenExpiresAt = expiresAt
        userDefaults.set(value, forKey: UDKeys.refreshToken)
        saveDate(expiresAt, forKey: UDKeys.refreshTokenExpiresAt)
    }

    public func saveTokens(
        accessToken: String, accessExpiresAt: Date,
        refreshToken: String, refreshExpiresAt: Date
    ) {
        // Атомарное сохранение обоих токенов
        ensureCacheLoaded()
        cache.accessToken = accessToken
        cache.accessTokenExpiresAt = accessExpiresAt
        cache.refreshToken = refreshToken
        cache.refreshTokenExpiresAt = refreshExpiresAt

        // Используем batch update через dictionary для атомарности
        let dict: [String: Any] = [
            UDKeys.accessToken: accessToken,
            UDKeys.accessTokenExpiresAt: dateFormatter.string(from: accessExpiresAt),
            UDKeys.refreshToken: refreshToken,
            UDKeys.refreshTokenExpiresAt: dateFormatter.string(from: refreshExpiresAt)
        ]
        userDefaults.setValuesForKeys(dict)
    }

    // MARK: - Clear

    public func clearAll() {
        cache.accessToken = nil
        cache.accessTokenExpiresAt = nil
        cache.refreshToken = nil
        cache.refreshTokenExpiresAt = nil

        userDefaults.removeObject(forKey: UDKeys.accessToken)
        userDefaults.removeObject(forKey: UDKeys.accessTokenExpiresAt)
        userDefaults.removeObject(forKey: UDKeys.refreshToken)
        userDefaults.removeObject(forKey: UDKeys.refreshTokenExpiresAt)
        // НЕ очищаем server host/port и device_id
    }

    /// Полная очистка: токены + server host/port + device_id. Сбрасывает in-memory cache целиком.
    public func purgeAll() {
        cache = TokenCache(isLoaded: true)

        userDefaults.removeObject(forKey: UDKeys.accessToken)
        userDefaults.removeObject(forKey: UDKeys.accessTokenExpiresAt)
        userDefaults.removeObject(forKey: UDKeys.refreshToken)
        userDefaults.removeObject(forKey: UDKeys.refreshTokenExpiresAt)
        userDefaults.removeObject(forKey: UDKeys.serverHost)
        userDefaults.removeObject(forKey: UDKeys.serverPort)
        userDefaults.removeObject(forKey: UDKeys.deviceId)
    }

    /// Очистка для обычного logout: токены + device_id, host/port остаются.
    public func purgeForLogout() {
        ensureCacheLoaded()
        let preservedHost = cache.serverHost
        let preservedPort = cache.serverPort

        cache = TokenCache(isLoaded: true)
        cache.serverHost = preservedHost
        cache.serverPort = preservedPort

        userDefaults.removeObject(forKey: UDKeys.accessToken)
        userDefaults.removeObject(forKey: UDKeys.accessTokenExpiresAt)
        userDefaults.removeObject(forKey: UDKeys.refreshToken)
        userDefaults.removeObject(forKey: UDKeys.refreshTokenExpiresAt)
        userDefaults.removeObject(forKey: UDKeys.deviceId)
        // serverHost / serverPort оставляем нетронутыми.
    }

    // MARK: - Server Endpoint

    public var savedServerHost: String? {
        ensureCacheLoaded()
        return cache.serverHost
    }

    public var savedServerPort: Int? {
        ensureCacheLoaded()
        return cache.serverPort
    }

    public func saveServerEndpoint(host: String, port: Int) {
        ensureCacheLoaded()
        cache.serverHost = host
        cache.serverPort = port
        userDefaults.set(host, forKey: UDKeys.serverHost)
        userDefaults.set(port, forKey: UDKeys.serverPort)
    }

    // MARK: - Device ID

    public var deviceID: String {
        ensureCacheLoaded()

        if let existing = cache.deviceId {
            return existing
        }

        // Генерируем новый ID
        let newID = UUID().uuidString
        cache.deviceId = newID
        userDefaults.set(newID, forKey: UDKeys.deviceId)
        return newID
    }

    // MARK: - Migration Support

    /// Экспорт всех данных для миграции
    public func exportAllData() async -> TokenExportData {
        ensureCacheLoaded()
        return TokenExportData(
            accessToken: cache.accessToken,
            accessTokenExpiresAt: cache.accessTokenExpiresAt,
            refreshToken: cache.refreshToken,
            refreshTokenExpirationDate: cache.refreshTokenExpiresAt,
            serverHost: cache.serverHost,
            serverPort: cache.serverPort,
            deviceId: cache.deviceId
        )
    }

    /// Импорт всех данных при миграции
    public func importAllData(_ data: TokenExportData) async throws {
        cache = TokenCache(
            accessToken: data.accessToken,
            accessTokenExpiresAt: data.accessTokenExpiresAt,
            refreshToken: data.refreshToken,
            refreshTokenExpiresAt: data.refreshTokenExpirationDate,
            serverHost: data.serverHost,
            serverPort: data.serverPort,
            deviceId: data.deviceId,
            isLoaded: true
        )

        // Сохраняем в UserDefaults
        var dict: [String: Any] = [:]

        if let accessToken = data.accessToken {
            dict[UDKeys.accessToken] = accessToken
        }
        if let expiresAt = data.accessTokenExpiresAt {
            dict[UDKeys.accessTokenExpiresAt] = dateFormatter.string(from: expiresAt)
        }
        if let refreshToken = data.refreshToken {
            dict[UDKeys.refreshToken] = refreshToken
        }
        if let refreshExpiresAt = data.refreshTokenExpirationDate {
            dict[UDKeys.refreshTokenExpiresAt] = dateFormatter.string(from: refreshExpiresAt)
        }
        if let host = data.serverHost {
            dict[UDKeys.serverHost] = host
        }
        if let port = data.serverPort {
            dict[UDKeys.serverPort] = port
        }
        if let deviceId = data.deviceId {
            dict[UDKeys.deviceId] = deviceId
        }

        userDefaults.setValuesForKeys(dict)
    }
}
