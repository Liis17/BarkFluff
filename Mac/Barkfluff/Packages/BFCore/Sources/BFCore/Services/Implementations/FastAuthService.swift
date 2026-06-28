//
//  FastAuthService.swift
//  BFCore
//
//  Реализация сервиса быстрой авторизации
//

import Foundation
import BFNetworking

/// Реализация сервиса быстрой авторизации
public actor FastAuthService: FastAuthServiceProtocol {

    private let fastAuthRepository: FastAuthRepositoryProtocol

    public init(fastAuthRepository: FastAuthRepositoryProtocol) {
        self.fastAuthRepository = fastAuthRepository
    }

    // MARK: - FastAuthServiceProtocol

    public func generateToken(type: FastAuthType) async throws -> FastAuthToken {
        let info = try await fastAuthRepository.generateFastAuthToken(type: toNetworkType(type))
        return FastAuthToken(id: info.id, token: info.token, expiresAt: info.expiresAt)
    }

    public func checkStatus(fastAuthID: String) async throws -> FastAuthStatus {
        let status = try await fastAuthRepository.checkFastAuth(fastAuthID: fastAuthID)
        return toDomainStatus(status)
    }

    public func scan(fastAuthID: String) async throws -> ScanFastAuthInfo {
        let info = try await fastAuthRepository.scanFastAuth(fastAuthID: fastAuthID)
        return ScanFastAuthInfo(
            fastAuthID: info.fastAuthID,
            deviceName: info.deviceName,
            operationSystem: info.operationSystem,
            appName: info.appName,
            appVersion: info.appVersion,
            ipAddress: info.ipAddress,
            confirmationCode: info.confirmationCode,
            expiresAt: info.expiresAt
        )
    }

    public func accept(fastAuthID: String, confirmationCode: String) async throws {
        try await fastAuthRepository.acceptFastAuth(fastAuthID: fastAuthID, confirmationCode: confirmationCode)
    }

    public func reject(fastAuthID: String, confirmationCode: String) async throws {
        try await fastAuthRepository.rejectFastAuth(fastAuthID: fastAuthID, confirmationCode: confirmationCode)
    }

    public func subscribeToResult(fastAuthID: String) async throws -> AsyncThrowingStream<FastAuthResult, Error> {
        let stream = try await fastAuthRepository.subscribeFastAuthResult(fastAuthID: fastAuthID)
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await event in stream {
                    let domainResult = FastAuthResult(
                        fastAuthID: event.fastAuthID,
                        status: toDomainStatus(event.status),
                        accessToken: event.accessToken,
                        accessTokenExpiresAt: event.accessTokenExpiresAt,
                        refreshToken: event.refreshToken,
                        refreshTokenExpiresAt: event.refreshTokenExpiresAt
                    )
                    continuation.yield(domainResult)
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func subscribeToRequests() async throws -> AsyncThrowingStream<FastAuthRequest, Error> {
        let stream = try await fastAuthRepository.subscribeFastAuthRequests()
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await request in stream {
                    let domainRequest = FastAuthRequest(
                        id: request.id,
                        deviceName: request.deviceName,
                        deviceType: request.deviceType,
                        requestedAt: request.requestedAt
                    )
                    continuation.yield(domainRequest)
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func connectDevice(deviceToken: String) async throws {
        try await fastAuthRepository.connectDevice(deviceToken: deviceToken)
    }

    public func acceptDevice(deviceID: String) async throws {
        try await fastAuthRepository.acceptConnectDevice(deviceID: deviceID)
    }

    public func subscribeToConnectStatus() async throws -> AsyncThrowingStream<ConnectDeviceStatus, Error> {
        let stream = try await fastAuthRepository.subscribeConnectDeviceStatus()
        return AsyncThrowingStream { continuation in
            let task = Task {
                for try await status in stream {
                    let domainStatus = ConnectDeviceStatus(
                        deviceID: status.deviceID,
                        status: status.status
                    )
                    continuation.yield(domainStatus)
                }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func listConnectedDevices() async throws -> [ConnectedDevice] {
        let devices = try await fastAuthRepository.listConnectedDevices()
        return devices.map { ConnectedDevice(
            id: $0.id,
            deviceName: $0.deviceName,
            deviceType: $0.deviceType,
            connectedAt: $0.connectedAt
        ) }
    }

    public func generateConnectDeviceToken() async throws -> String {
        try await fastAuthRepository.generateConnectDeviceToken()
    }

    // MARK: - Private helpers

    private func toNetworkType(_ type: FastAuthType) -> BFNetworking.FastAuthType {
        switch type {
        case .qr: return .qr
        case .code: return .code
        }
    }

    private func toDomainStatus(_ status: BFNetworking.FastAuthStatus) -> FastAuthStatus {
        switch status {
        case .pending: return .pending
        case .accepted: return .accepted
        case .rejected: return .rejected
        case .expired: return .expired
        }
    }
}
