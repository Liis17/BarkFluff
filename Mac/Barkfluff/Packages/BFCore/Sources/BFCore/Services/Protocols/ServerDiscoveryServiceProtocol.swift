//
//  ServerDiscoveryServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса обнаружения серверов
//

import Foundation

/// Протокол сервиса обнаружения серверов
public protocol ServerDiscoveryServiceProtocol: Sendable {
    /// Подключиться к серверу через Beacon
    func connect(host: String, port: Int) async throws -> ServerInfo

    /// Получить информацию о текущем сервере
    func getCurrentServer() async -> ServerInfo?

    /// Попытка переподключения к последнему серверу (auto-reconnect)
    func tryReconnect() async -> Bool

    /// Получить список серверов из Navigator
    func listServers() async throws -> [NavigatorServer]

    /// Зарегистрировать сервер в Navigator
    func registerServer(name: String, host: String, port: Int, description: String?) async throws

    /// Отключиться от сервера
    func disconnect() async

    /// Сохранённый адрес Beacon (host, port) текущего сервера, если есть.
    func currentServerEndpoint() async -> (host: String, port: Int)?

    /// Пинг текущего Beacon: повторно вызывает `GetServerInfo` и измеряет задержку.
    /// Возвращает время ответа в секундах, либо ошибку.
    func pingCurrentServer() async throws -> TimeInterval
}

/// Информация о сервере из Navigator
public struct NavigatorServer: Identifiable, Hashable, Sendable {
    public let id: String
    public let name: String
    public let host: String
    public let port: Int
    public let description: String?
    public let accountsCount: Int64
    public let serverPublicName: String

    /// Отображаемое имя — публичное если задано, иначе внутреннее
    public var displayName: String {
        serverPublicName.isEmpty ? name : serverPublicName
    }

    /// Форматированный адрес Beacon для отображения
    public var beaconAddress: String {
        "\(host):\(port)"
    }

    public init(
        id: String,
        name: String,
        host: String,
        port: Int,
        description: String? = nil,
        accountsCount: Int64 = 0,
        serverPublicName: String = ""
    ) {
        self.id = id
        self.name = name
        self.host = host
        self.port = port
        self.description = description
        self.accountsCount = accountsCount
        self.serverPublicName = serverPublicName
    }
}
