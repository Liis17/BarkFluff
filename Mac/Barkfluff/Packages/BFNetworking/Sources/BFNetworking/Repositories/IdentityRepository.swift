//
//  IdentityRepository.swift
//  BFNetworking
//

import Foundation
import GRPCCore
import GRPCNIOTransportHTTP2Posix
import BFProto
import SwiftProtobuf

public actor IdentityRepository: IdentityRepositoryProtocol {
    private let connectionManager: ConnectionManager

    public init(connectionManager: ConnectionManager) {
        self.connectionManager = connectionManager
    }

    // MARK: - Auth (публичный, без AuthInterceptor)

    public func auth(login: String, isEmail: Bool, password: String, otpCode: String?) async throws -> AuthTokens {
        var request = Barkfluff_Identity_AuthRequest()
        if isEmail {
            request.login = .email(login)
        } else {
            request.login = .username(login)
        }
        request.password = password
        request.otpCode = otpCode ?? ""
        let req = request

        do {
            return try await connectionManager.withPublicClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.auth(req)
                return AuthTokens(
                    accessToken: self.mapToken(response.accessToken),
                    refreshToken: self.mapToken(response.refreshToken)
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - CreateToken (публичный, без AuthInterceptor)

    public func createToken(refreshToken: String) async throws -> TokenInfo {
        var request = Barkfluff_Identity_CreateTokenRequest()
        request.refreshToken = refreshToken
        let req = request

        do {
            return try await connectionManager.withPublicClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.createToken(req)
                return self.mapToken(response.accessToken)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - CreateAccount (публичный, без AuthInterceptor)

    public func createAccount(firstName: String, lastName: String, username: String, email: String) async throws -> String {
        var request = Barkfluff_Identity_CreateAccountRequest()
        request.firstName = firstName
        request.lastName = lastName
        request.username = username
        request.email = email
        let req = request

        do {
            return try await connectionManager.withPublicClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.createAccount(req)
                return response.codeID
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - ConfirmAccount (публичный, без AuthInterceptor)

    public func confirmAccount(codeID: String, code: String) async throws -> TokenInfo {
        var request = Barkfluff_Identity_ConfirmAccountRequest()
        request.codeID = codeID
        request.codeValue = code
        let req = request

        do {
            return try await connectionManager.withPublicClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.confirmAccount(req)
                return self.mapToken(response.refreshToken)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Token Mapping

    private nonisolated func mapToken(_ protoToken: Barkfluff_Identity_Token) -> TokenInfo {
        let date: Date
        if protoToken.hasExpirationDate {
            let ts = protoToken.expirationDate
            date = Date(timeIntervalSince1970: TimeInterval(ts.seconds) + TimeInterval(ts.nanos) / 1_000_000_000)
        } else {
            // Fallback: 1 час от текущего времени
            date = Date().addingTimeInterval(3600)
        }
        return TokenInfo(value: protoToken.value, expirationDate: date)
    }

    // MARK: - SetPassword (авторизованный)

    public func setPassword(password: String) async throws {
        var request = Barkfluff_Identity_SetPasswordRequest()
        request.password = password
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                _ = try await identityClient.setPassword(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - EnableOTP (авторизованный)

    public func enableOTP() async throws -> OTPSetupInfo {
        var request = Barkfluff_Identity_EnableOtpVerificationRequest()
        request.otpType = .authenticator
        let req = request

        do {
            return try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.enableOtpVerification(req)
                return OTPSetupInfo(
                    qrCodeBase64: response.otpQr,
                    secretCode: response.otpCode
                )
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - ConfirmOTP (авторизованный)

    public func confirmOTP(code: String) async throws {
        var request = Barkfluff_Identity_ConfirmOtpVerificationRequest()
        request.otpCode = code
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                _ = try await identityClient.confirmOtpVerification(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    // MARK: - Sessions (авторизованный)

    public func getActiveSessions() async throws -> [SessionInfo] {
        let request = Barkfluff_Identity_GetActiveSessionsRequest()

        do {
            return try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                let response = try await identityClient.getActiveSessions(request)
                return response.sessions.map { session in
                    let createdAt = Date(
                        timeIntervalSince1970: TimeInterval(session.createdAt.seconds) + TimeInterval(session.createdAt.nanos) / 1_000_000_000
                    )
                    let expirationAt = Date(
                        timeIntervalSince1970: TimeInterval(session.expirationAt.seconds) + TimeInterval(session.expirationAt.nanos) / 1_000_000_000
                    )
                    return SessionInfo(
                        id: session.id,
                        createdAt: createdAt,
                        expirationAt: expirationAt,
                        deviceId: session.deviceID,
                        originalName: session.originalName,
                        customName: session.customName,
                        appName: session.appName,
                        operationSystem: session.operationSystem,
                        location: session.location
                    )
                }
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }

    public func removeActiveSession(deviceID: String) async throws {
        var request = Barkfluff_Identity_RemoveActiveSessionRequest()
        request.deviceID = deviceID
        let req = request

        do {
            try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                _ = try await identityClient.removeActiveSession(req)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }
    public func disableOTP(code: String) async throws {}
    public func listOTP() async throws -> OTPStatus { throw BFNetworkingError.unknown("Not implemented") }
    public func resetPassword(email: String) async throws -> String { throw BFNetworkingError.unknown("Not implemented") }
    public func confirmResetPassword(codeID: String, code: String, newPassword: String) async throws {}

    // MARK: - Logout (авторизованный)

    public func logout() async throws {
        let request = Barkfluff_Identity_LogoutRequest()

        do {
            try await connectionManager.withAuthorizedClient(for: .identity) { client in
                let identityClient = Barkfluff_Identity_IdentityApi.Client(wrapping: client)
                _ = try await identityClient.logout(request)
            }
        } catch let error as RPCError {
            throw GRPCErrorMapper.map(error)
        }
    }
}
