//
//  ServerDiscoveryService.swift
//  BFCore
//
//  Реализация сервиса обнаружения серверов
//

import Foundation
import BFNetworking

/// Реализация сервиса обнаружения серверов
public actor ServerDiscoveryService: ServerDiscoveryServiceProtocol {

    private let beaconRepository: BeaconRepositoryProtocol
    private let navigatorRepository: NavigatorRepositoryProtocol
    private let connectionManager: ConnectionManager
    private let tokenProvider: TokenProvider

    private var currentServerValue: ServerInfo?

    public init(
        beaconRepository: BeaconRepositoryProtocol,
        navigatorRepository: NavigatorRepositoryProtocol,
        connectionManager: ConnectionManager,
        tokenProvider: TokenProvider
    ) {
        self.beaconRepository = beaconRepository
        self.navigatorRepository = navigatorRepository
        self.connectionManager = connectionManager
        self.tokenProvider = tokenProvider
    }

    // MARK: - ServerDiscoveryServiceProtocol

    public func getCurrentServer() async -> ServerInfo? {
        currentServerValue
    }

    public func connect(host: String, port: Int) async throws -> ServerInfo {
        let serverInfo = try await beaconRepository.getServerInfo(host: host, port: port)

        // Сохраняем адрес сервера для auto-reconnect
        await tokenProvider.saveServerEndpoint(host: host, port: port)

        let server = ServerInfo(
            name: serverInfo.name,
            version: serverInfo.version,
            description: serverInfo.description,
            color: serverInfo.color.map { ServerColor(hex: $0) },
            publicName: serverInfo.publicName,
            location: serverInfo.location
        )

        currentServerValue = server
        return server
    }

    public func tryReconnect() async -> Bool {
        guard let host = await tokenProvider.savedServerHost,
              let port = await tokenProvider.savedServerPort else {
            return false
        }

        do {
            _ = try await connect(host: host, port: port)
            return true
        } catch {
            return false
        }
    }

    public func listServers() async throws -> [NavigatorServer] {
        let servers = try await navigatorRepository.listServers()
        return servers.map { NavigatorServer(
            id: $0.id,
            name: $0.name,
            host: $0.host,
            port: $0.port,
            description: $0.description.isEmpty ? nil : $0.description,
            accountsCount: $0.accountsCount,
            serverPublicName: $0.serverPublicName
        ) }
    }

    public func registerServer(name: String, host: String, port: Int, description: String?) async throws {
        try await navigatorRepository.registerServer(
            name: name,
            host: host,
            port: port,
            description: description,
            accountsCount: 0,
            serverPublicName: ""
        )
    }

    public func disconnect() async {
        await connectionManager.shutdown()
        currentServerValue = nil
    }

    public func currentServerEndpoint() async -> (host: String, port: Int)? {
        guard let host = await tokenProvider.savedServerHost,
              let port = await tokenProvider.savedServerPort else {
            return nil
        }
        return (host, port)
    }

    public func pingCurrentServer() async throws -> TimeInterval {
        guard let endpoint = await currentServerEndpoint() else {
            throw ConnectionError.notBootstrapped
        }
        let start = Date()
        _ = try await beaconRepository.getServerInfo(host: endpoint.host, port: endpoint.port)
        return Date().timeIntervalSince(start)
    }
}
