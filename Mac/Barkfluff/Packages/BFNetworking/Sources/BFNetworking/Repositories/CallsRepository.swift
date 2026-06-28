//
//  CallsRepository.swift
//  BFNetworking
//
//  gRPC-сигнализация звонков (BarkFluff.Calls). Медиа — через LiveKit (см. BFCalls).
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

// MARK: - DTOs

/// Тип медиа звонка.
public enum CallMediaTypeDTO: Sendable, Hashable {
    case audio
    case video
}

/// Общее (для всех участников) качество голоса звонка.
public enum CallAudioQualityDTO: Sendable, Hashable {
    case auto
    case low
    case medium
    case high
}

/// Причина завершения звонка.
public enum CallEndReasonDTO: Sendable, Hashable {
    case hangup
    case rejected
    case missed
    case busy
    case failed
    case unknown
}

/// Действие участника (вошёл/вышел) для группового UI.
public enum CallParticipantActionDTO: Sendable, Hashable {
    case joined
    case left
    case unknown
}

/// Данные для входа в LiveKit-комнату (ответ Initiate/Accept/Join).
public struct CallConnectionInfo: Sendable {
    public let callID: String
    public let livekitURL: String
    public let accessToken: String
    public let audioQuality: CallAudioQualityDTO

    public init(callID: String, livekitURL: String, accessToken: String, audioQuality: CallAudioQualityDTO) {
        self.callID = callID
        self.livekitURL = livekitURL
        self.accessToken = accessToken
        self.audioQuality = audioQuality
    }
}

/// Событие звонка из device-scope стрима `SubscribeCallEvents`.
public enum CallEventDTO: Sendable {
    case incoming(callID: String, callerUserID: Int64, chatID: String, media: CallMediaTypeDTO, startedAt: Date?)
    case accepted(callID: String, acceptedByUserID: Int64)
    case rejected(callID: String, rejectedByUserID: Int64)
    case ended(callID: String, reason: CallEndReasonDTO, durationSeconds: Int64)
    case participant(callID: String, userID: Int64, action: CallParticipantActionDTO)
    case audioQualityChanged(callID: String, quality: CallAudioQualityDTO, changedByUserID: Int64)
}

// MARK: - Protocol

public protocol CallsRepositoryProtocol: Sendable {
    /// Старт звонка: ровно одно из `calleeUserID` (1-на-1) / `chatID` (групповой).
    func initiateCall(calleeUserID: Int64?, chatID: String?, media: CallMediaTypeDTO) async throws -> CallConnectionInfo
    func joinCall(callID: String) async throws -> CallConnectionInfo
    func acceptCall(callID: String) async throws -> CallConnectionInfo
    func rejectCall(callID: String) async throws
    func endCall(callID: String) async throws
    func setAudioQuality(callID: String, quality: CallAudioQualityDTO) async throws
    func subscribeCallEvents() async throws -> AsyncThrowingStream<CallEventDTO, Error>
}

// MARK: - Repository

public actor CallsRepository: CallsRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func initiateCall(calleeUserID: Int64?, chatID: String?, media: CallMediaTypeDTO) async throws -> CallConnectionInfo {
        var req = Barkfluff_Calls_InitiateCallRequest()
        if let calleeUserID {
            req.calleeUserID = calleeUserID
        } else if let chatID {
            req.chatID = chatID
        }
        req.mediaType = Self.mapMedia(media)
        let request = req
        return try await connectionManager.withAuthorizedClient(for: .calls) { client in
            let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
            let resp = try await callsClient.initiateCall(request)
            return Self.mapConnection(callID: resp.callID, url: resp.livekitURL, token: resp.accessToken, quality: resp.audioQuality)
        }
    }

    public func joinCall(callID: String) async throws -> CallConnectionInfo {
        var req = Barkfluff_Calls_JoinCallRequest()
        req.callID = callID
        let request = req
        return try await connectionManager.withAuthorizedClient(for: .calls) { client in
            let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
            let resp = try await callsClient.joinCall(request)
            return Self.mapConnection(callID: callID, url: resp.livekitURL, token: resp.accessToken, quality: resp.audioQuality)
        }
    }

    public func acceptCall(callID: String) async throws -> CallConnectionInfo {
        var req = Barkfluff_Calls_AcceptCallRequest()
        req.callID = callID
        let request = req
        return try await connectionManager.withAuthorizedClient(for: .calls) { client in
            let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
            let resp = try await callsClient.acceptCall(request)
            return Self.mapConnection(callID: callID, url: resp.livekitURL, token: resp.accessToken, quality: resp.audioQuality)
        }
    }

    public func rejectCall(callID: String) async throws {
        var req = Barkfluff_Calls_RejectCallRequest()
        req.callID = callID
        let request = req
        try await connectionManager.withAuthorizedClient(for: .calls) { client in
            let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
            _ = try await callsClient.rejectCall(request)
        }
    }

    public func endCall(callID: String) async throws {
        var req = Barkfluff_Calls_EndCallRequest()
        req.callID = callID
        let request = req
        try await connectionManager.withAuthorizedClient(for: .calls) { client in
            let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
            _ = try await callsClient.endCall(request)
        }
    }

    public func setAudioQuality(callID: String, quality: CallAudioQualityDTO) async throws {
        var req = Barkfluff_Calls_SetCallAudioQualityRequest()
        req.callID = callID
        req.quality = Self.mapQualityToProto(quality)
        let request = req
        try await connectionManager.withAuthorizedClient(for: .calls) { client in
            let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
            _ = try await callsClient.setCallAudioQuality(request)
        }
    }

    public func subscribeCallEvents() async throws -> AsyncThrowingStream<CallEventDTO, Error> {
        let req = Barkfluff_Calls_SubscribeCallEventsRequest()
        return AsyncThrowingStream { continuation in
            Task {
                do {
                    try await self.connectionManager.withAuthorizedClient(for: .calls) { client in
                        let callsClient = Barkfluff_Calls_CallsApi.Client(wrapping: client)
                        try await callsClient.subscribeCallEvents(req) { response in
                            for try await event in response.messages {
                                if let dto = Self.mapEvent(event) {
                                    continuation.yield(dto)
                                }
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

    // MARK: - Mapping

    private static func mapMedia(_ media: CallMediaTypeDTO) -> Barkfluff_Calls_CallMediaType {
        switch media {
        case .audio: return .callMediaAudio
        case .video: return .callMediaVideo
        }
    }

    private static func mapMediaToDTO(_ media: Barkfluff_Calls_CallMediaType) -> CallMediaTypeDTO {
        switch media {
        case .callMediaVideo: return .video
        default: return .audio
        }
    }

    private static func mapQualityToProto(_ quality: CallAudioQualityDTO) -> Barkfluff_Calls_CallAudioQuality {
        switch quality {
        case .auto: return .auto
        case .low: return .low
        case .medium: return .medium
        case .high: return .high
        }
    }

    private static func mapQualityToDTO(_ quality: Barkfluff_Calls_CallAudioQuality) -> CallAudioQualityDTO {
        switch quality {
        case .low: return .low
        case .medium: return .medium
        case .high: return .high
        default: return .auto
        }
    }

    private static func mapEndReason(_ reason: Barkfluff_Calls_CallEndReason) -> CallEndReasonDTO {
        switch reason {
        case .callEndHangup: return .hangup
        case .callEndRejected: return .rejected
        case .callEndMissed: return .missed
        case .callEndBusy: return .busy
        case .callEndFailed: return .failed
        default: return .unknown
        }
    }

    private static func mapAction(_ action: Barkfluff_Calls_ParticipantAction) -> CallParticipantActionDTO {
        switch action {
        case .participantJoined: return .joined
        case .participantLeft: return .left
        default: return .unknown
        }
    }

    private static func mapConnection(callID: String, url: String, token: String, quality: Barkfluff_Calls_CallAudioQuality) -> CallConnectionInfo {
        CallConnectionInfo(callID: callID, livekitURL: url, accessToken: token, audioQuality: mapQualityToDTO(quality))
    }

    private static func mapEvent(_ event: Barkfluff_Calls_CallEvent) -> CallEventDTO? {
        switch event.event {
        case .incoming(let e):
            let startedAt: Date? = e.hasStartedAt ? e.startedAt.date : nil
            return .incoming(callID: e.callID, callerUserID: e.callerUserID, chatID: e.chatID,
                             media: mapMediaToDTO(e.mediaType), startedAt: startedAt)
        case .accepted(let e):
            return .accepted(callID: e.callID, acceptedByUserID: e.acceptedByUserID)
        case .rejected(let e):
            return .rejected(callID: e.callID, rejectedByUserID: e.rejectedByUserID)
        case .ended(let e):
            return .ended(callID: e.callID, reason: mapEndReason(e.reason), durationSeconds: e.durationSeconds)
        case .member(let e):
            return .participant(callID: e.callID, userID: e.userID, action: mapAction(e.action))
        case .audioQuality(let e):
            return .audioQualityChanged(callID: e.callID, quality: mapQualityToDTO(e.quality), changedByUserID: e.changedByUserID)
        case .none:
            return nil
        }
    }
}
