//
//  KeychainTokenProvider.swift
//  BFNetworking
//
//  Хранение токенов в Keychain через KeychainAccess
//  С in-memory кэшем для предотвращения множественных запросов пароля
//

import Foundation
import KeychainAccess

/// Ключи для Keychain
private enum Keys {
    static let accessToken = "barkfluff.access_token"
    static let accessTokenExpiresAt = "barkfluff.access_token_expires_at"
    static let refreshToken = "barkfluff.refresh_token"
    static let refreshTokenExpiresAt = "barkfluff.refresh_token_expires_at"
    static let serverHost = "barkfluff.server_host"
    static let serverPort = "barkfluff.server_port"
    static let deviceId = "barkfluff.device_id"
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

/// Конфигурация Keychain-хранилища
public struct KeychainConfiguration: Sendable {
    /// Использовать iCloud синхронизацию (требует пароль при первом доступе)
    public let useICloudSync: Bool

    /// Название сервиса в Keychain
    public let serviceIdentifier: String

    public init(useICloudSync: Bool = false, serviceIdentifier: String = "com.barkfluff.tokens") {
        self.useICloudSync = useICloudSync
        self.serviceIdentifier = serviceIdentifier
    }

    public static let `default` = KeychainConfiguration()
    public static let withICloud = KeychainConfiguration(useICloudSync: true)
}

/// Провайдер токенов с хранением в Keychain
public actor KeychainTokenProvider: TokenProvider {

    private let keychain: Keychain
    private let configuration: KeychainConfiguration

    /// In-memory кэш - КЛЮЧЕВОЕ изменение для предотвращения множественных запросов пароля
    private var cache: TokenCache = TokenCache()

    private let dateFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    public init(configuration: KeychainConfiguration = .default) {
        self.configuration = configuration

        var keychain = Keychain(service: configuration.serviceIdentifier)

        if configuration.useICloudSync {
            // iCloud синхронизация - будет запрашивать пароль
            keychain = keychain
                .synchronizable(true)
                .accessibility(.afterFirstUnlock)
        } else {
            // Без iCloud - доступно после первого разблокирования
            keychain = keychain
                .accessibility(.afterFirstUnlockThisDeviceOnly)
        }

        self.keychain = keychain
    }

    /// Init для обратной совместимости (default configuration)
    public init() {
        self.configuration = .default
        self.keychain = Keychain(service: "com.barkfluff.tokens")
            .accessibility(.afterFirstUnlockThisDeviceOnly)
    }

    // MARK: - Cache Management

    /// Загружает все данные в кэш за ОДИН проход по Keychain
    /// Это минимизирует запросы пароля до ОДНОГО при первом запуске
    private func ensureCacheLoaded() {
        guard !cache.isLoaded else { return }

        // Загружаем всё за один раз
        cache = TokenCache(
            accessToken: keychain[Keys.accessToken],
            accessTokenExpiresAt: loadDateFromKeychain(Keys.accessTokenExpiresAt),
            refreshToken: keychain[Keys.refreshToken],
            refreshTokenExpiresAt: loadDateFromKeychain(Keys.refreshTokenExpiresAt),
            serverHost: keychain[Keys.serverHost],
            serverPort: loadPortFromKeychain(),
            deviceId: keychain[Keys.deviceId],
            isLoaded: true
        )
    }

    private func loadDateFromKeychain(_ key: String) -> Date? {
        guard let str = keychain[key] else { return nil }
        return dateFormatter.date(from: str)
    }

    private func loadPortFromKeychain() -> Int? {
        guard let str = keychain[Keys.serverPort] else { return nil }
        return Int(str)
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
        keychain[Keys.accessToken] = value
        keychain[Keys.accessTokenExpiresAt] = dateFormatter.string(from: expiresAt)
    }

    public func saveRefreshToken(value: String, expiresAt: Date) {
        ensureCacheLoaded()
        cache.refreshToken = value
        cache.refreshTokenExpiresAt = expiresAt
        keychain[Keys.refreshToken] = value
        keychain[Keys.refreshTokenExpiresAt] = dateFormatter.string(from: expiresAt)
    }

    public func saveTokens(
        accessToken: String, accessExpiresAt: Date,
        refreshToken: String, refreshExpiresAt: Date
    ) {
        ensureCacheLoaded()

        // Сначала обновляем кэш
        cache.accessToken = accessToken
        cache.accessTokenExpiresAt = accessExpiresAt
        cache.refreshToken = refreshToken
        cache.refreshTokenExpiresAt = refreshExpiresAt

        // Потом сохраняем в keychain
        keychain[Keys.accessToken] = accessToken
        keychain[Keys.accessTokenExpiresAt] = dateFormatter.string(from: accessExpiresAt)
        keychain[Keys.refreshToken] = refreshToken
        keychain[Keys.refreshTokenExpiresAt] = dateFormatter.string(from: refreshExpiresAt)
    }

    // MARK: - Clear

    /// Очищает токены и даты. НЕ очищает server host/port и device_id.
    public func clearAll() {
        cache.accessToken = nil
        cache.accessTokenExpiresAt = nil
        cache.refreshToken = nil
        cache.refreshTokenExpiresAt = nil

        keychain[Keys.accessToken] = nil
        keychain[Keys.accessTokenExpiresAt] = nil
        keychain[Keys.refreshToken] = nil
        keychain[Keys.refreshTokenExpiresAt] = nil
    }

    /// Полная очистка: токены + server host/port + device_id. Сбрасывает in-memory cache целиком.
    public func purgeAll() {
        cache = TokenCache(isLoaded: true)

        keychain[Keys.accessToken] = nil
        keychain[Keys.accessTokenExpiresAt] = nil
        keychain[Keys.refreshToken] = nil
        keychain[Keys.refreshTokenExpiresAt] = nil
        keychain[Keys.serverHost] = nil
        keychain[Keys.serverPort] = nil
        keychain[Keys.deviceId] = nil
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
        keychain[Keys.serverHost] = host
        keychain[Keys.serverPort] = String(port)
    }

    // MARK: - Device ID

    public var deviceID: String {
        ensureCacheLoaded()

        if let existing = cache.deviceId {
            return existing
        }

        let newID = UUID().uuidString
        cache.deviceId = newID
        keychain[Keys.deviceId] = newID
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
        // Обновляем кэш
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

        // Сохраняем в keychain
        if let accessToken = data.accessToken {
            keychain[Keys.accessToken] = accessToken
        }
        if let expiresAt = data.accessTokenExpiresAt {
            keychain[Keys.accessTokenExpiresAt] = dateFormatter.string(from: expiresAt)
        }
        if let refreshToken = data.refreshToken {
            keychain[Keys.refreshToken] = refreshToken
        }
        if let refreshExpiresAt = data.refreshTokenExpirationDate {
            keychain[Keys.refreshTokenExpiresAt] = dateFormatter.string(from: refreshExpiresAt)
        }
        if let host = data.serverHost {
            keychain[Keys.serverHost] = host
        }
        if let port = data.serverPort {
            keychain[Keys.serverPort] = String(port)
        }
        if let deviceId = data.deviceId {
            keychain[Keys.deviceId] = deviceId
        }
    }
}
