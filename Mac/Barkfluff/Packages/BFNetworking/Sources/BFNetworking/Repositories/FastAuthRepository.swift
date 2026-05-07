//
//  FastAuthRepository.swift
//  BFNetworking
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

public actor FastAuthRepository: FastAuthRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    // MARK: - GenerateFastAuthToken (анонимный, без AuthInterceptor)

    public func generateFastAuthToken(type: FastAuthType) async throws -> FastAuthTokenInfo {
        var request = Barkfluff_Fast_Auth_GenerateFastAuthTokenRequest()
        request.format = Self.toProtoFormat(type)
        let req = request

        do {
            return try await connectionManager.withPublicClient(for: .fastauth) { client in
                let fastAuthClient = Barkfluff_Fast_Auth_FastAuthApi.Client(wrapping: client)
                let response = try await fastAuthClient.generateFastAuthToken(req)
                return FastAuthTokenInfo(
                    id: response.fastAuthID,
                    token: response.token.value,
                    expiresAt: Self.timestampToDate(response.expiresAt, hasValue: response.hasExpiresAt)
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - SubscribeFastAuthResult (анонимный, server-streaming)

    public func subscribeFastAuthResult(fastAuthID: String) async throws -> AsyncThrowingStream<FastAuthResult, Error> {
        var request = Barkfluff_Fast_Auth_SubscribeFastAuthResultRequest()
        request.fastAuthID = fastAuthID
        let req = request

        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withPublicClient(for: .fastauth) { client in
                        let fastAuthClient = Barkfluff_Fast_Auth_FastAuthApi.Client(wrapping: client)
                        try await fastAuthClient.subscribeFastAuthResult(req) { response in
                            for try await event in response.messages {
                                let mapped = FastAuthResult(
                                    fastAuthID: fastAuthID,
                                    status: Self.toDomainStatus(event.status),
                                    accessToken: event.accessToken.isEmpty ? nil : event.accessToken,
                                    accessTokenExpiresAt: event.hasAccessTokenExpiresAt
                                        ? Self.timestampToDate(event.accessTokenExpiresAt, hasValue: true)
                                        : nil,
                                    refreshToken: event.refreshToken.isEmpty ? nil : event.refreshToken,
                                    refreshTokenExpiresAt: event.hasRefreshTokenExpiresAt
                                        ? Self.timestampToDate(event.refreshTokenExpiresAt, hasValue: true)
                                        : nil
                                )
                                continuation.yield(mapped)
                            }
                            continuation.finish()
                        }
                    }
                } catch let error as RPCError {
                    continuation.finish(throwing: GRPCErrorMapper.map(error))
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    // MARK: - Не используется на macOS (мобильный сценарий, заглушки)

    public func checkFastAuth(fastAuthID: String) async throws -> FastAuthStatus { throw BFNetworkingError.unknown("Not implemented") }
    public func acceptFastAuth(fastAuthID: String) async throws {}
    public func subscribeFastAuthRequests() async throws -> AsyncThrowingStream<FastAuthRequest, Error> { return AsyncThrowingStream { _ in } }
    public func connectDevice(deviceToken: String) async throws {}
    public func acceptConnectDevice(deviceID: String) async throws {}
    public func subscribeConnectDeviceStatus() async throws -> AsyncThrowingStream<ConnectDeviceStatus, Error> { return AsyncThrowingStream { _ in } }
    public func listConnectedDevices() async throws -> [ConnectedDeviceInfo] { return [] }
    public func generateConnectDeviceToken() async throws -> String { throw BFNetworkingError.unknown("Not implemented") }

    // MARK: - Helpers

    private nonisolated static func toProtoFormat(_ type: FastAuthType) -> Barkfluff_Fast_Auth_TokenFormat {
        switch type {
        case .qr: return .qr
        case .code: return .text
        }
    }

    private nonisolated static func toDomainStatus(_ status: Barkfluff_Fast_Auth_FastAuthStatus) -> FastAuthStatus {
        switch status {
        case .accepted: return .accepted
        case .rejected: return .rejected
        case .expired: return .expired
        // PENDING, SCANNED, UNKNOWN — все промежуточные. На macOS-логине нет отдельного состояния
        // «просканирован, ждём подтверждения», поэтому мапим как pending — UI показывает «Ожидание сканирования».
        default: return .pending
        }
    }

    private nonisolated static func timestampToDate(
        _ ts: SwiftProtobuf.Google_Protobuf_Timestamp,
        hasValue: Bool
    ) -> Date {
        guard hasValue else {
            // Fallback: TTL FastAuth-сессии — 5 минут.
            return Date().addingTimeInterval(300)
        }
        return Date(timeIntervalSince1970: TimeInterval(ts.seconds) + TimeInterval(ts.nanos) / 1_000_000_000)
    }
}
