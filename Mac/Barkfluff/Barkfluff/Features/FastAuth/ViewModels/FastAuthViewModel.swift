//
//  FastAuthViewModel.swift
//  Barkfluff
//
//  ViewModel для быстрой авторизации (QR-вход на macOS-клиенте)
//

import SwiftUI
import AppKit
import Observation
import BFCore

@Observable
@MainActor
final class FastAuthViewModel {

    // MARK: - State

    var currentToken: FastAuthToken?
    var currentTokenImage: NSImage?
    var currentStatus: FastAuthStatus = .pending
    var pendingRequests: [FastAuthRequest] = []
    var connectedDevices: [ConnectedDevice] = []
    var isLoading: Bool = false
    var errorMessage: String?
    var timeRemaining: Int = 0

    // MARK: - Dependencies

    private let fastAuthService: FastAuthServiceProtocol
    private let admissionService: SignInSessionAdmissionService

    // MARK: - Callbacks

    /// Вызывается после успешного применения токенов FastAuth — повторяет роль
    /// `App.OpenMessengerPage()` из WPF-клиента.
    var onAuthenticated: ((SignInSessionAdmissionReceipt) -> Void)?

    // MARK: - Internal

    private var sessionTask: Task<Void, Never>?
    private var timerTask: Task<Void, Never>?
    private var hasFinished: Bool = false
    private var admissionAttempt: SignInSessionAdmissionAttempt?

    var canRetrySession: Bool { hasFinished && errorMessage != nil }

    // MARK: - Init

    init(
        fastAuthService: FastAuthServiceProtocol,
        admissionService: SignInSessionAdmissionService
    ) {
        self.fastAuthService = fastAuthService
        self.admissionService = admissionService
    }

    // MARK: - Lifecycle

    /// Запуск/перезапуск сессии QR-логина: запросить новый токен и подписаться на стрим результата.
    func startSession() {
        cancel()
        hasFinished = false

        sessionTask = Task { [weak self] in
            await self?.runSession()
        }
    }

    /// Полная остановка стрима/таймера. Вызывается при уходе со страницы.
    func cancel() {
        sessionTask?.cancel()
        sessionTask = nil
        timerTask?.cancel()
        timerTask = nil
        if let admissionAttempt {
            admissionAttempt.cancel()
            Task { await admissionService.invalidateSignInAttempt(admissionAttempt) }
        }
        admissionAttempt = nil
    }

    // MARK: - Session

    private func runSession() async {
        guard !hasFinished else { return }
        if admissionAttempt == nil {
            let attempt = await admissionService.beginSignInAttempt()
            guard !Task.isCancelled else {
                attempt.cancel()
                await admissionService.invalidateSignInAttempt(attempt)
                return
            }
            admissionAttempt = attempt
            guard !attempt.isCancelled else { return }
        }

        // 1. Сгенерировать новый токен.
        await generateQRToken()
        guard currentToken != nil, !Task.isCancelled else { return }

        // 2. Подписаться на результат.
        await subscribeToResult()

        // Если стрим завершился без финального статуса (например, потеряна сеть или
        // сервер закрыл соединение), и пользователь ещё на экране — пробуем заново,
        // как делает WPF-клиент.
        if !Task.isCancelled, !hasFinished {
            try? await Task.sleep(for: .seconds(1))
            if !Task.isCancelled, !hasFinished {
                await runSession()
            }
        }
    }

    private func generateQRToken() async {
        let attempt = admissionAttempt
        isLoading = true
        errorMessage = nil
        currentTokenImage = nil
        defer { if admissionAttempt == attempt { isLoading = false } }

        do {
            let token = try await fastAuthService.generateToken(type: .qr)
            guard !Task.isCancelled, admissionAttempt == attempt else { return }
            currentToken = token
            currentStatus = .pending
            currentTokenImage = decodeQRImage(from: token.token)
            startTimer(until: token.expiresAt)
        } catch {
            guard !Task.isCancelled, admissionAttempt == attempt else { return }
            errorMessage = String(localized: "fast_auth.qr.error.token \(error.localizedDescription)")
        }
    }

    private func subscribeToResult() async {
        guard let tokenID = currentToken?.id else { return }

        do {
            let stream = try await fastAuthService.subscribeToResult(fastAuthID: tokenID)
            for try await result in stream {
                if Task.isCancelled { return }
                currentStatus = result.status

                switch result.status {
                case .pending:
                    continue

                case .accepted:
                    if let access = result.accessToken, !access.isEmpty,
                       let accessExpires = result.accessTokenExpiresAt,
                       let refresh = result.refreshToken, !refresh.isEmpty,
                       let refreshExpires = result.refreshTokenExpiresAt,
                       accessExpires.timeIntervalSince1970.isFinite, refreshExpires.timeIntervalSince1970.isFinite,
                       accessExpires > Date(), refreshExpires > Date() {
                        guard let admissionAttempt else { return }
                        let session = SignInSessionTokens(
                            accessToken: access,
                            accessExpiresAt: accessExpires,
                            refreshToken: refresh,
                            refreshExpiresAt: refreshExpires
                        )
                        let receipt = await commitWhenAvailable(session, for: admissionAttempt)
                        guard !Task.isCancelled, self.admissionAttempt == admissionAttempt else {
                            await admissionService.invalidateSignInAttempt(admissionAttempt)
                            return
                        }
                        guard let receipt else {
                            if !admissionAttempt.isCancelled, accessExpires <= Date() || refreshExpires <= Date() {
                                errorMessage = String(localized: "fast_auth.qr.error.invalid_session")
                                await admissionService.invalidateSignInAttempt(admissionAttempt)
                            }
                            hasFinished = true
                            timerTask?.cancel()
                            return
                        }
                        let current = await admissionService.isCurrentSignInSession(receipt)
                        guard current, !Task.isCancelled, self.admissionAttempt == admissionAttempt else {
                            await admissionService.invalidateSignInAttempt(receipt.attempt)
                            return
                        }
                        hasFinished = true
                        timerTask?.cancel()
                        onAuthenticated?(receipt)
                        return
                    } else {
                        hasFinished = true
                        timerTask?.cancel()
                        errorMessage = String(localized: "fast_auth.qr.error.invalid_session")
                        return
                    }

                case .rejected:
                    errorMessage = String(localized: "fast_auth.qr.error.rejected")
                    return

                case .expired:
                    errorMessage = nil
                    return
                }
            }
        } catch {
            if !Task.isCancelled {
                errorMessage = String(localized: "fast_auth.qr.error.subscribe \(error.localizedDescription)")
            }
        }
    }

    private func commitWhenAvailable(
        _ session: SignInSessionTokens,
        for attempt: SignInSessionAdmissionAttempt
    ) async -> SignInSessionAdmissionReceipt? {
        while !Task.isCancelled, !attempt.isCancelled,
              session.accessExpiresAt > Date(), session.refreshExpiresAt > Date() {
            if let receipt = await admissionService.commitSignInSession(session, for: attempt) {
                return receipt
            }
            guard !Task.isCancelled, !attempt.isCancelled,
                  session.accessExpiresAt > Date(), session.refreshExpiresAt > Date() else { return nil }
            do { try await Task.sleep(for: .seconds(1)) } catch { return nil }
        }
        return nil
    }

    // MARK: - QR PNG decoding

    private func decodeQRImage(from base64: String) -> NSImage? {
        guard let data = Data(base64Encoded: base64), let image = NSImage(data: data) else {
            return nil
        }
        return image
    }

    // MARK: - Countdown timer

    private func startTimer(until expiresAt: Date) {
        timerTask?.cancel()
        timeRemaining = max(0, Int(expiresAt.timeIntervalSinceNow))

        timerTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(1))
                guard let self, !Task.isCancelled else { return }
                let remaining = max(0, Int(expiresAt.timeIntervalSinceNow))
                self.timeRemaining = remaining
                if remaining == 0 { return }
            }
        }
    }
}
