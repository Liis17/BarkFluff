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
    private let authService: AuthServiceProtocol

    // MARK: - Callbacks

    /// Вызывается после успешного применения токенов FastAuth — повторяет роль
    /// `App.OpenMessengerPage()` из WPF-клиента.
    var onAuthenticated: (() -> Void)?

    // MARK: - Internal

    private var sessionTask: Task<Void, Never>?
    private var timerTask: Task<Void, Never>?
    private var hasFinished: Bool = false

    // MARK: - Init

    init(fastAuthService: FastAuthServiceProtocol, authService: AuthServiceProtocol) {
        self.fastAuthService = fastAuthService
        self.authService = authService
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
    }

    // MARK: - Session

    private func runSession() async {
        guard !hasFinished else { return }

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
        isLoading = true
        errorMessage = nil
        currentTokenImage = nil
        defer { isLoading = false }

        do {
            let token = try await fastAuthService.generateToken(type: .qr)
            currentToken = token
            currentStatus = .pending
            currentTokenImage = decodeQRImage(from: token.token)
            startTimer(until: token.expiresAt)
        } catch {
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
                    if let access = result.accessToken,
                       let accessExpires = result.accessTokenExpiresAt,
                       let refresh = result.refreshToken,
                       let refreshExpires = result.refreshTokenExpiresAt {
                        await authService.applyFastAuthTokens(
                            accessToken: access, accessExpiresAt: accessExpires,
                            refreshToken: refresh, refreshExpiresAt: refreshExpires
                        )
                        hasFinished = true
                        timerTask?.cancel()
                        onAuthenticated?()
                        return
                    } else {
                        errorMessage = String(localized: "fast_auth.qr.error.empty_tokens")
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
