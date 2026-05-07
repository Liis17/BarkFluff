//
//  TokenProvider.swift
//  BFNetworking
//
//  Протокол для хранения и получения токенов авторизации
//

import Foundation

/// Протокол для хранения и получения токенов авторизации.
/// Все методы потокобезопасны (actor-изоляция в реализации).
public protocol TokenProvider: Sendable {

    /// Текущий access token (nil если нет или истёк)
    var currentAccessToken: String? { get async }

    /// Текущий refresh token (nil если нет)
    var currentRefreshToken: String? { get async }

    /// Дата истечения access token
    var accessTokenExpirationDate: Date? { get async }

    /// Дата истечения refresh token
    var refreshTokenExpirationDate: Date? { get async }

    /// Сохранить access token и его дату истечения
    func saveAccessToken(value: String, expiresAt: Date) async

    /// Сохранить refresh token и его дату истечения
    func saveRefreshToken(value: String, expiresAt: Date) async

    /// Сохранить оба токена сразу
    func saveTokens(
        accessToken: String, accessExpiresAt: Date,
        refreshToken: String, refreshExpiresAt: Date
    ) async

    /// Очистить все токены (logout). НЕ очищает server host/port и device_id.
    func clearAll() async

    /// Полная очистка хранилища: токены + server host/port + device_id + in-memory cache.
    /// Используется при «жёстком» logout, когда не должно остаться ни единого следа от старого инстанса.
    func purgeAll() async

    /// Есть ли сохранённый refresh-токен
    var hasRefreshToken: Bool { get async }

    /// Хост последнего Beacon-сервера
    var savedServerHost: String? { get async }

    /// Порт последнего Beacon-сервера
    var savedServerPort: Int? { get async }

    /// Сохранить адрес сервера для auto-reconnect
    func saveServerEndpoint(host: String, port: Int) async

    /// Уникальный ID устройства (генерируется при первом запуске)
    var deviceID: String { get async }

    // MARK: - Migration Support

    /// Экспортировать все данные для миграции в другое хранилище
    func exportAllData() async -> TokenExportData

    /// Импортировать данные при миграции из другого хранилища
    func importAllData(_ data: TokenExportData) async throws
}
