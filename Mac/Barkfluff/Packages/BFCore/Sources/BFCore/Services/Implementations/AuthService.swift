//
//  AuthService.swift
//  BFCore
//
//  Реализация сервиса аутентификации
//

import Foundation
import BFNetworking

/// Реализация сервиса аутентификации
public actor AuthService: AuthServiceProtocol {

    private let identityRepository: IdentityRepositoryProtocol
    private let tokenProvider: TokenProvider
    private let connectionManager: ConnectionManager

    public init(
        identityRepository: IdentityRepositoryProtocol,
        tokenProvider: TokenProvider,
        connectionManager: ConnectionManager
    ) {
        self.identityRepository = identityRepository
        self.tokenProvider = tokenProvider
        self.connectionManager = connectionManager
    }

    // MARK: - AuthServiceProtocol

    public func login(login: String, password: String, otpCode: String?) async throws {
        let isEmail = login.contains("@")

        do {
            let tokens = try await identityRepository.auth(
                login: login,
                isEmail: isEmail,
                password: password,
                otpCode: otpCode
            )

            await tokenProvider.saveTokens(
                accessToken: tokens.accessToken.value,
                accessExpiresAt: tokens.accessToken.expirationDate,
                refreshToken: tokens.refreshToken.value,
                refreshExpiresAt: tokens.refreshToken.expirationDate
            )
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func register(firstName: String, lastName: String, username: String, email: String) async throws -> String {
        do {
            return try await identityRepository.createAccount(
                firstName: firstName,
                lastName: lastName,
                username: username,
                email: email
            )
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func confirmAccount(codeID: String, code: String) async throws {
        do {
            // 1. Подтвердить аккаунт → получаем refresh token
            let refreshTokenInfo = try await identityRepository.confirmAccount(codeID: codeID, code: code)
            await tokenProvider.saveRefreshToken(
                value: refreshTokenInfo.value,
                expiresAt: refreshTokenInfo.expirationDate
            )

            // 2. Создать access token используя refresh token
            let accessTokenInfo = try await identityRepository.createToken(refreshToken: refreshTokenInfo.value)
            await tokenProvider.saveAccessToken(
                value: accessTokenInfo.value,
                expiresAt: accessTokenInfo.expirationDate
            )
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func tryRestoreSession() async -> Bool {
        // 1. Проверяем наличие refresh token
        guard await tokenProvider.hasRefreshToken else { return false }

        // 2. Если access token ещё валиден — ОК
        if let expiresAt = await tokenProvider.accessTokenExpirationDate,
           expiresAt.timeIntervalSinceNow > 60 {
            return true
        }

        // 3. Пробуем обновить access token
        guard let refreshToken = await tokenProvider.currentRefreshToken else { return false }

        do {
            let tokenInfo = try await identityRepository.createToken(refreshToken: refreshToken)
            await tokenProvider.saveAccessToken(
                value: tokenInfo.value,
                expiresAt: tokenInfo.expirationDate
            )
            return true
        } catch {
            // Refresh не удался — сессия мертва
            await tokenProvider.clearAll()
            return false
        }
    }

    public func logout() async {
        try? await identityRepository.logout()
        await tokenProvider.clearAll()
        await connectionManager.shutdown()
    }

    public func setPassword(password: String) async throws {
        do {
            try await identityRepository.setPassword(password: password)
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func enableOTP() async throws -> OTPSetupInfo {
        do {
            return try await identityRepository.enableOTP()
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    public func confirmOTP(code: String) async throws {
        do {
            try await identityRepository.confirmOTP(code: code)
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }
    }

    // MARK: - Error Mapping

    private static func mapError(_ error: BFNetworkingError) -> BFError {
        switch error {
        case .otpRequired:
            return .otpRequired
        case .invalidCredentials:
            return .invalidCredentials
        case .sessionExpired:
            return .sessionExpired
        case .unauthorized:
            return .unauthorized
        case .usernameAlreadyExists:
            return .usernameAlreadyExists("")
        case .emailAlreadyExists:
            return .emailAlreadyExists("")
        case .invalidConfirmationCode:
            return .invalidConfirmationCode
        case .serverUnavailable:
            return .serverUnavailable
        case .timeout:
            return .timeout
        case .networkError(let msg):
            return .networkError(msg)
        case .invalidURL(let msg):
            return .networkError(msg)
        case .storageLimitExceeded(let msg):
            return .networkError(msg)
        case .fileUploadFailed(let msg):
            return .networkError(msg)
        case .invalidFileHash:
            return .networkError("Invalid file hash")
        case .invalidArgument(let msg):
            return .networkError(msg)
        case .alreadyExists(let msg):
            return .networkError(msg)
        case .notFound(let msg):
            return .networkError(msg)
        case .invalidOtpCode:
            return .invalidOTP
        case .unknown(let msg):
            return .unknown(message: msg)
        }
    }
}
