import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

public actor SignInRepository: SignInRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    public func resolveSignIn(login: String) async throws -> SignInResolution {
        do {
            let (_, response) = try await withCurrentIdentityEndpoint { client in
                var request = Barkfluff_Identity_ResolveSignInRequest()
                request.login = login
                let client = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                return try await client.resolveSignIn(request)
            }
            return SignInResolution(
                mode: SignInProtoMapper.loginMode(response.loginMode),
                errorCode: response.errorCode.isEmpty ? nil : response.errorCode
            )
        } catch {
            throw Self.mapError(error)
        }
    }

    public func beginSignIn(
        login: String,
        password: String,
        mode: SignInLoginMode,
        factor: SignInFactor?,
        useRecoveryCode: Bool,
        reportLoginModeMismatch: Bool
    ) async throws -> SignInChallenge {
        do {
            let (endpoint, response) = try await withCurrentIdentityEndpoint { client in
                var request = Barkfluff_Identity_BeginSignInRequest()
                request.login = login
                request.password = password
                request.loginMode = SignInProtoMapper.loginMode(mode)
                request.factor = factor.map(SignInProtoMapper.factor) ?? .unknown
                request.useRecoveryCode = useRecoveryCode
                request.reportLoginModeMismatch = reportLoginModeMismatch
                let client = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                return try await client.beginSignIn(request)
            }
            return try SignInProtoMapper.challenge(response, endpoint: endpoint)
        } catch {
            throw Self.mapError(error)
        }
    }

    public func getAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge {
        do {
            let response = try await withReference(reference) { client in
                let client = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                return try await client.getAuthChallenge(Self.protoReference(reference))
            }
            return try SignInProtoMapper.challenge(response, endpoint: reference.endpoint)
        } catch {
            throw Self.mapError(error)
        }
    }

    public func completeAuthChallenge(
        _ reference: SignInChallengeReference,
        code: String,
        useRecoveryCode: Bool
    ) async throws -> SignInCompletion {
        do {
            let response = try await withReference(reference) { client in
                var request = Barkfluff_Identity_CompleteAuthChallengeRequest()
                request.challenge = Self.protoReference(reference)
                request.code = code
                request.useRecoveryCode = useRecoveryCode
                let client = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                return try await client.completeAuthChallenge(request)
            }
            return try SignInProtoMapper.completion(response)
        } catch {
            throw Self.mapError(error)
        }
    }

    public func cancelAuthChallenge(_ reference: SignInChallengeReference) async {
        // Cancellation is best effort. The RPC deadline bounds it, and the expected endpoint
        // prevents a challenge reference from being sent to a newly selected server.
        do {
            try await withReference(reference) { client in
                let client = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                var options = CallOptions.defaults
                options.timeout = .seconds(2)
                _ = try await client.cancelAuthChallenge(
                    Self.protoReference(reference),
                    options: options
                )
            }
        } catch {
            // The local generation is already invalidated; remote cleanup is opportunistic.
        }
    }

    public func resendAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge {
        do {
            let response = try await withReference(reference) { client in
                let client = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                return try await client.resendAuthChallenge(Self.protoReference(reference))
            }
            return try SignInProtoMapper.challenge(response, endpoint: reference.endpoint)
        } catch {
            throw Self.mapError(error)
        }
    }

    private func withCurrentIdentityEndpoint<T: Sendable>(
        operation: @Sendable (GRPCClient<HTTP2ClientTransport.Posix>) async throws -> T
    ) async throws -> (ServiceEndpoint, T) {
        guard let endpoint = await connectionManager.endpoints()[.identity] else {
            throw ConnectionError.serviceNotConfigured(.identity)
        }
        let result = try await connectionManager.withPublicClient(for: .identity, expectedEndpoint: endpoint, operation: operation)
        return (endpoint, result)
    }

    private func withReference<T: Sendable>(
        _ reference: SignInChallengeReference,
        operation: @Sendable (GRPCClient<HTTP2ClientTransport.Posix>) async throws -> T
    ) async throws -> T {
        try await connectionManager.withPublicClient(
            for: .identity,
            expectedEndpoint: reference.endpoint,
            operation: operation
        )
    }

    private nonisolated static func protoReference(
        _ reference: SignInChallengeReference
    ) -> Barkfluff_Identity_AuthChallengeReference {
        var proto = Barkfluff_Identity_AuthChallengeReference()
        proto.id = reference.id
        proto.secret = reference.secret
        return proto
    }

    private nonisolated static func mapError(_ error: Error) -> SignInRepositoryError {
        if error is CancellationError || Task.isCancelled { return .cancelled }
        if let connectionError = error as? ConnectionError,
           case .endpointChanged = connectionError {
            return .cancelled
        }
        guard let rpcError = error as? RPCError else {
            return .transport(statusCode: -1, serverErrorCode: nil)
        }
        return SignInRPCErrorMapper.map(rpcError)
    }
}

public enum SignInRPCErrorMapper {
    public static let invalidCredentialsCode = "21BFB9B5-C377-45D1-9B15-6B7F3432B397"
    public static let lockoutCode = "B95A5B58-6A7F-43A2-A9B9-D9D8F8B4B1E4"
    public static let rateLimitCode = "7D1CBF0E-2C85-4A2A-9B2D-6B2A6CF5A1E2"

    public static func map(_ error: RPCError) -> SignInRepositoryError {
        let errorCode = error.metadata[stringValues: "x-error-code"].first(where: { _ in true })
        switch errorCode {
        case invalidCredentialsCode:
            return .invalidCredentials
        case lockoutCode:
            return .lockedOut
        case rateLimitCode:
            return .rateLimited
        default:
            break
        }

        if error.code == .unimplemented { return .serverUpdateRequired }
        if error.code == .cancelled { return .cancelled }
        if error.code == .resourceExhausted { return .rateLimited }
        return .transport(statusCode: error.code.rawValue, serverErrorCode: errorCode)
    }
}

public enum SignInProtoMapper {
    public static func loginMode(_ value: Barkfluff_Identity_AuthLoginMode) -> SignInLoginMode {
        switch value {
        case .password: .password
        case .telegramLogin: .telegramLogin
        case .passwordSecondFactor: .passwordSecondFactor
        case .loginModeUnspecified: .unknown(0)
        case .UNRECOGNIZED(let rawValue): .unknown(rawValue)
        }
    }

    public static func loginMode(_ value: SignInLoginMode) -> Barkfluff_Identity_AuthLoginMode {
        switch value {
        case .password: .password
        case .telegramLogin: .telegramLogin
        case .passwordSecondFactor: .passwordSecondFactor
        case .unknown(let rawValue): Barkfluff_Identity_AuthLoginMode(rawValue: rawValue) ?? .loginModeUnspecified
        }
    }

    public static func factor(_ value: Barkfluff_Identity_OtpTypeId) -> SignInFactor {
        switch value {
        case .email: .email
        case .telegram: .telegram
        case .authenticator: .authenticator
        case .unknown: .unknown(0)
        case .UNRECOGNIZED(let rawValue): .unknown(rawValue)
        }
    }

    public static func factor(_ value: SignInFactor) -> Barkfluff_Identity_OtpTypeId {
        switch value {
        case .email: .email
        case .telegram: .telegram
        case .authenticator: .authenticator
        case .unknown(let rawValue): Barkfluff_Identity_OtpTypeId(rawValue: rawValue) ?? .unknown
        }
    }

    public static func challengeState(_ value: Barkfluff_Identity_AuthChallengeState) -> SignInChallengeState {
        switch value {
        case .waiting: .waiting
        case .approved: .approved
        case .rejected: .rejected
        case .expired: .expired
        case .completed: .completed
        case .cancelled: .cancelled
        case .challengeUnspecified: .unknown(0)
        case .UNRECOGNIZED(let rawValue): .unknown(rawValue)
        }
    }

    public static func challenge(
        _ response: Barkfluff_Identity_AuthChallengeResponse,
        endpoint: ServiceEndpoint
    ) throws -> SignInChallenge {
        let reference: SignInChallengeReference?
        if response.hasChallenge {
            guard !response.challenge.id.isEmpty, !response.challenge.secret.isEmpty else {
                throw SignInRepositoryError.malformedResponse
            }
            reference = SignInChallengeReference(
                id: response.challenge.id,
                secret: response.challenge.secret,
                endpoint: endpoint
            )
        } else {
            reference = nil
        }

        let expiresAt: Date?
        if response.hasExpiresAt {
            guard let parsed = date(from: response.expiresAt) else {
                throw SignInRepositoryError.malformedResponse
            }
            expiresAt = parsed
        } else {
            expiresAt = nil
        }

        return SignInChallenge(
            reference: reference,
            state: challengeState(response.state),
            factor: factor(response.factor),
            telegramURL: response.telegramURL.isEmpty ? nil : response.telegramURL,
            expiresAt: expiresAt,
            availableFactors: response.availableFactors.map(factor),
            needsCode: response.needsCode,
            errorCode: response.errorCode.isEmpty ? nil : response.errorCode
        )
    }

    public static func completion(
        _ response: Barkfluff_Identity_CompleteAuthChallengeResponse,
        now: Date = Date()
    ) throws -> SignInCompletion {
        let session = response.hasSession ? try session(response.session, now: now) : nil
        return SignInCompletion(
            state: challengeState(response.state),
            session: session,
            errorCode: response.errorCode.isEmpty ? nil : response.errorCode
        )
    }

    public static func session(
        _ response: Barkfluff_Identity_AuthResponse,
        now: Date = Date()
    ) throws -> SignInSessionTokens {
        guard response.hasAccessToken, response.hasRefreshToken,
              !response.accessToken.value.isEmpty, !response.refreshToken.value.isEmpty,
              response.accessToken.hasExpirationDate, response.refreshToken.hasExpirationDate,
              let accessExpiry = date(from: response.accessToken.expirationDate),
              let refreshExpiry = date(from: response.refreshToken.expirationDate),
              accessExpiry > now, refreshExpiry > now else {
            throw SignInRepositoryError.malformedResponse
        }
        return SignInSessionTokens(
            accessToken: response.accessToken.value,
            accessExpiresAt: accessExpiry,
            refreshToken: response.refreshToken.value,
            refreshExpiresAt: refreshExpiry
        )
    }

    private static func date(from timestamp: SwiftProtobuf.Google_Protobuf_Timestamp) -> Date? {
        guard (-62_135_596_800...253_402_300_799).contains(timestamp.seconds),
              (0..<1_000_000_000).contains(timestamp.nanos) else { return nil }
        let value = TimeInterval(timestamp.seconds) + TimeInterval(timestamp.nanos) / 1_000_000_000
        guard value.isFinite else { return nil }
        return Date(timeIntervalSince1970: value)
    }
}
