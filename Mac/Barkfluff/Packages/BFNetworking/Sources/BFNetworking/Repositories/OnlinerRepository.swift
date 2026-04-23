//
//  OnlinerRepository.swift
//  BFNetworking
//
//  Репозиторий для работы с онлайн-статусами
//

import Foundation
import GRPCCore
import BFProto
import SwiftProtobuf

public actor OnlinerRepository: OnlinerRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    // MARK: - SetOnlineStatus (heartbeat)

    public func setOnlineStatus() async throws {
        let request = Barkfluff_Onliner_SetOnlineStatusRequest()

        try await connectionManager.withAuthorizedClient(for: .onliner) { client in
            let onlinerClient = Barkfluff_Onliner_OnlinerApi.Client(wrapping: client)
            _ = try await onlinerClient.setOnlineStatus(request)
        }
    }

    // MARK: - GetOnlineStatus (batch)

    public func getOnlineStatus(userIDs: [Int64]) async throws -> [UserOnlineStatusInfo] {
        var request = Barkfluff_Onliner_GetOnlineStatusRequest()
        request.userIds = userIDs
        let req = request

        return try await connectionManager.withAuthorizedClient(for: .onliner) { client in
            let onlinerClient = Barkfluff_Onliner_OnlinerApi.Client(wrapping: client)
            let response = try await onlinerClient.getOnlineStatus(req)

            return response.usersStatuses.map { proto in
                Self.mapProtoToStatusInfo(proto)
            }
        }
    }

    // MARK: - SubscribeToOnlineStatus (server streaming)

    public func subscribeToOnlineStatus(userIDs: [Int64]) async throws -> AsyncThrowingStream<UserOnlineStatusInfo, Error> {
        var request = Barkfluff_Onliner_SubscribeToOnlineStatusRequest()
        request.userIds = userIDs
        let req = request

        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withAuthorizedClient(for: .onliner) { client in
                        let onlinerClient = Barkfluff_Onliner_OnlinerApi.Client(wrapping: client)
                        try await onlinerClient.subscribeToOnlineStatus(req) { response in
                            for try await proto in response.messages {
                                let info = Self.mapProtoToStatusInfo(proto)
                                continuation.yield(info)
                            }
                            continuation.finish()
                        }
                    }
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    // MARK: - ChangeUsersInSubscription

    public func changeUsersInSubscription(userIDs: [Int64]) async throws {
        var request = Barkfluff_Onliner_ChangeUsersInSubscriptionRequest()
        request.userIds = userIDs
        let req = request

        try await connectionManager.withAuthorizedClient(for: .onliner) { client in
            let onlinerClient = Barkfluff_Onliner_OnlinerApi.Client(wrapping: client)
            _ = try await onlinerClient.changeUsersInSubscription(req)
        }
    }

    // MARK: - Helpers

    private nonisolated static func mapProtoToStatusInfo(_ proto: Barkfluff_Onliner_UserOnlineStatus) -> UserOnlineStatusInfo {
        let status: UserOnlineStatusType
        switch proto.status {
        case .statusOnline:
            status = .online
        case .statusOffline:
            status = .offline
        default:
            status = .unknown
        }

        let lastSeen: Date?
        if proto.hasLastSeen {
            lastSeen = Date(
                timeIntervalSince1970: TimeInterval(proto.lastSeen.seconds) + TimeInterval(proto.lastSeen.nanos) / 1_000_000_000
            )
        } else {
            lastSeen = nil
        }

        return UserOnlineStatusInfo(userID: proto.userID, status: status, lastSeen: lastSeen)
    }
}
