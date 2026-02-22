//
//  BeaconRepository.swift
//  BFNetworking
//

import Foundation

public actor BeaconRepository: BeaconRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func getServerInfo(host: String, port: Int) async throws -> ServerInfoDTO {
        return try await connectionManager.bootstrap(host: host, port: port)
    }
}
