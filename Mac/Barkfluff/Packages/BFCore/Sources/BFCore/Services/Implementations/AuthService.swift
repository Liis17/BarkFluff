//
//  AuthService.swift
//  BFCore
//
//  Реализация сервиса аутентификации
//

import Foundation
import BFNetworking

/// Реализация сервиса аутентификации
public actor AuthService: AuthServiceProtocol, SignInServiceProtocol, SignInSessionAdmissionService {

    private let identityRepository: IdentityRepositoryProtocol
    private let tokenProvider: TokenProvider
    private let connectionManager: ConnectionManager
    private let signInRepository: SignInRepositoryProtocol
    private var activeSignInAttempts: Set<SignInSessionAdmissionAttempt> = []
    private var winningSignInAttempt: SignInSessionAdmissionAttempt?

    // Held across token-provider awaits: actor isolation alone does not serialize storage.
    private var storageLocked = false
    private var storageWaiters: [CheckedContinuation<Void, Never>] = []

    private func acquireStorage() async {
        if !storageLocked { storageLocked = true; return }
        await withCheckedContinuation { storageWaiters.append($0) }
    }

    private func releaseStorage() {
        if storageWaiters.isEmpty { storageLocked = false }
        else { storageWaiters.removeFirst().resume() }
    }

    public init(
        identityRepository: IdentityRepositoryProtocol,
        tokenProvider: TokenProvider,
        connectionManager: ConnectionManager,
        signInRepository: SignInRepositoryProtocol? = nil
    ) {
        self.identityRepository = identityRepository
        self.tokenProvider = tokenProvider
        self.connectionManager = connectionManager
        self.signInRepository = signInRepository ?? SignInRepository(connectionManager: connectionManager)
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
        if let winner = winningSignInAttempt { return winner.isAccepted }
        if !activeSignInAttempts.isEmpty { return false }

        // 1. Проверяем наличие refresh token
        guard await tokenProvider.hasRefreshToken else { return false }

        // 2. Если access token ещё валиден — ОК
        if let expiresAt = await tokenProvider.accessTokenExpirationDate,
           expiresAt.timeIntervalSinceNow > 60 {
            return true
        }

        // 3. Пробуем обновить access token
        guard let refreshToken = await tokenProvider.currentRefreshToken else { return false }
        let startingWinner = winningSignInAttempt
        let startingAttempts = activeSignInAttempts

        do {
            let tokenInfo = try await identityRepository.createToken(refreshToken: refreshToken)
            await acquireStorage()
            defer { releaseStorage() }
            let currentRefreshToken = await tokenProvider.currentRefreshToken
            guard currentRefreshToken == refreshToken else { return currentRefreshToken != nil }
            guard winningSignInAttempt == startingWinner,
                  activeSignInAttempts == startingAttempts,
                  winningSignInAttempt?.isAccepted != true else {
                return winningSignInAttempt?.isAccepted == true
            }
            await tokenProvider.saveAccessToken(
                value: tokenInfo.value,
                expiresAt: tokenInfo.expirationDate
            )
            return true
        } catch {
            await acquireStorage()
            defer { releaseStorage() }
            let currentRefreshToken = await tokenProvider.currentRefreshToken
            guard currentRefreshToken == refreshToken else { return currentRefreshToken != nil }
            guard winningSignInAttempt == startingWinner,
                  activeSignInAttempts == startingAttempts,
                  winningSignInAttempt?.isAccepted != true else {
                return winningSignInAttempt?.isAccepted == true
            }

            // Refresh не удался — удаляем только ту сессию, которая его запустила.
            resetSignInAdmission()
            await tokenProvider.clearAll()
            return false
        }
    }

    public func logout() async throws {
        // 1. Серверный шаг — ОБЯЗАТЕЛЕН: удаляет refresh-токены и устройство из БД,
        //    публикует SessionRevokedEvent → инвалидирует access-токены на остальных сервисах.
        do {
            try await identityRepository.logout()
        } catch let error as BFNetworkingError {
            throw Self.mapError(error)
        }

        // 2. Только при успехе серверного шага — стираем токены и device_id локально.
        //    Очистка остальных кешей/БД делается на уровне DependencyContainer.reset().
        await acquireStorage()
        defer { releaseStorage() }
        resetSignInAdmission()
        await tokenProvider.purgeAll()
    }

    public func forceLocalLogout() async {
        await acquireStorage()
        defer { releaseStorage() }
        resetSignInAdmission()
        await tokenProvider.purgeAll()
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

    // MARK: - Challenge-based sign-in

    public func resolveSignIn(login: String) async throws -> SignInResolution {
        try await signInRepository.resolveSignIn(login: login)
    }

    public func beginSignIn(
        login: String,
        password: String,
        mode: SignInLoginMode,
        factor: SignInFactor?,
        useRecoveryCode: Bool,
        reportLoginModeMismatch: Bool
    ) async throws -> SignInChallenge {
        try await signInRepository.beginSignIn(
            login: login,
            password: password,
            mode: mode,
            factor: factor,
            useRecoveryCode: useRecoveryCode,
            reportLoginModeMismatch: reportLoginModeMismatch
        )
    }

    public func getAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge {
        try await signInRepository.getAuthChallenge(reference)
    }

    public func completeAuthChallenge(
        _ reference: SignInChallengeReference,
        code: String,
        useRecoveryCode: Bool
    ) async throws -> SignInCompletion {
        try await signInRepository.completeAuthChallenge(reference, code: code, useRecoveryCode: useRecoveryCode)
    }

    public func cancelAuthChallenge(_ reference: SignInChallengeReference) async {
        await signInRepository.cancelAuthChallenge(reference)
    }

    public func resendAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge {
        try await signInRepository.resendAuthChallenge(reference)
    }

    public func beginSignInAttempt() async -> SignInSessionAdmissionAttempt {
        let attempt = SignInSessionAdmissionAttempt()
        if winningSignInAttempt?.isAccepted == true {
            attempt.cancel()
        } else {
            activeSignInAttempts.insert(attempt)
        }
        return attempt
    }

    public func invalidateSignInAttempt(_ attempt: SignInSessionAdmissionAttempt) async {
        attempt.cancel()
        activeSignInAttempts.remove(attempt)
        await acquireStorage()
        defer { releaseStorage() }
        if winningSignInAttempt == attempt, !attempt.isAccepted {
            await tokenProvider.clearAll()
            winningSignInAttempt = nil
        }
    }

    public func commitSignInSession(
        _ session: SignInSessionTokens,
        for attempt: SignInSessionAdmissionAttempt
    ) async -> SignInSessionAdmissionReceipt? {
        await acquireStorage()
        defer { releaseStorage() }
        if let winner = winningSignInAttempt, winner.isCancelled {
            await tokenProvider.clearAll()
            winningSignInAttempt = nil
        }
        if winningSignInAttempt?.isAccepted == true {
            attempt.cancel()
            activeSignInAttempts.remove(attempt)
            return nil
        }
        guard winningSignInAttempt == nil, activeSignInAttempts.contains(attempt), !attempt.isCancelled,
              !session.accessToken.isEmpty, !session.refreshToken.isEmpty,
              session.accessExpiresAt.timeIntervalSince1970.isFinite,
              session.refreshExpiresAt.timeIntervalSince1970.isFinite,
              session.accessExpiresAt > Date(), session.refreshExpiresAt > Date() else { return nil }

        // This is the admission linearization point. Other QR/challenge completions can no longer
        // persist after this attempt wins, even while the token provider writes asynchronously.
        winningSignInAttempt = attempt
        activeSignInAttempts.remove(attempt)
        await tokenProvider.saveTokens(
            accessToken: session.accessToken,
            accessExpiresAt: session.accessExpiresAt,
            refreshToken: session.refreshToken,
            refreshExpiresAt: session.refreshExpiresAt
        )
        guard !attempt.isCancelled else {
            await tokenProvider.clearAll()
            winningSignInAttempt = nil
            return nil
        }
        return SignInSessionAdmissionReceipt(attempt: attempt)
    }

    public func isCurrentSignInSession(_ receipt: SignInSessionAdmissionReceipt) -> Bool {
        winningSignInAttempt == receipt.attempt && !receipt.attempt.isCancelled
    }

    private func resetSignInAdmission() {
        activeSignInAttempts.forEach { $0.cancel() }
        activeSignInAttempts.removeAll()
        winningSignInAttempt?.cancel()
        winningSignInAttempt = nil
    }

    // MARK: - FastAuth (QR-логин)

    public func applyFastAuthTokens(
        accessToken: String, accessExpiresAt: Date,
        refreshToken: String, refreshExpiresAt: Date
    ) async {
        await tokenProvider.saveTokens(
            accessToken: accessToken,
            accessExpiresAt: accessExpiresAt,
            refreshToken: refreshToken,
            refreshExpiresAt: refreshExpiresAt
        )
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
