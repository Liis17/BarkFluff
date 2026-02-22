//
//  FastAuthRepository.swift
//  BFNetworking
//

import Foundation

public actor FastAuthRepository: FastAuthRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func generateFastAuthToken(type: FastAuthType) async throws -> FastAuthTokenInfo { throw BFNetworkingError.unknown("Not implemented") }
    public func checkFastAuth(fastAuthID: String) async throws -> FastAuthStatus { throw BFNetworkingError.unknown("Not implemented") }
    public func acceptFastAuth(fastAuthID: String) async throws {}
    public func subscribeFastAuthResult(fastAuthID: String) async throws -> AsyncThrowingStream<FastAuthResult, Error> { return AsyncThrowingStream { _ in } }
    public func subscribeFastAuthRequests() async throws -> AsyncThrowingStream<FastAuthRequest, Error> { return AsyncThrowingStream { _ in } }
    public func connectDevice(deviceToken: String) async throws {}
    public func acceptConnectDevice(deviceID: String) async throws {}
    public func subscribeConnectDeviceStatus() async throws -> AsyncThrowingStream<ConnectDeviceStatus, Error> { return AsyncThrowingStream { _ in } }
    public func listConnectedDevices() async throws -> [ConnectedDeviceInfo] { return [] }
    public func generateConnectDeviceToken() async throws -> String { throw BFNetworkingError.unknown("Not implemented") }
}
