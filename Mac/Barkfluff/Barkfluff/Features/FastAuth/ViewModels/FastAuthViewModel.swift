//
//  FastAuthViewModel.swift
//  Barkfluff
//
//  ViewModel для быстрой авторизации
//

import SwiftUI
import Observation
import BFCore

@Observable
final class FastAuthViewModel {

    // MARK: - State

    var currentToken: FastAuthToken?
    var currentStatus: FastAuthStatus = .pending
    var pendingRequests: [FastAuthRequest] = []
    var connectedDevices: [ConnectedDevice] = []
    var isLoading: Bool = false
    var errorMessage: String?

    // MARK: - Dependencies

    private let fastAuthService: FastAuthServiceProtocol

    // MARK: - Init

    init(fastAuthService: FastAuthServiceProtocol) {
        self.fastAuthService = fastAuthService
    }

    // MARK: - Actions (QR Code Auth)

    /// Сгенерировать токен для QR-авторизации
    func generateQRToken() async {
        isLoading = true
        defer { isLoading = false }

        do {
            currentToken = try await fastAuthService.generateToken(type: .qr)
            currentStatus = .pending

            // Подписываемся на результат
            await subscribeToResult()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Подписаться на результат авторизации
    private func subscribeToResult() async {
        guard let tokenID = currentToken?.id else { return }

        do {
            let stream = try await fastAuthService.subscribeToResult(fastAuthID: tokenID)

            for try await result in stream {
                currentStatus = result.status

                if result.status == .accepted, result.accessToken != nil {
                    // Авторизация успешна — сохранить токены
                    // TODO: Сохранить токены через AuthService
                }
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Actions (Device Approval)

    /// Принять запрос на авторизацию
    func acceptRequest(_ requestID: String) async {
        do {
            try await fastAuthService.accept(fastAuthID: requestID)
            pendingRequests.removeAll { $0.id == requestID }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Подписаться на запросы авторизации
    func subscribeToRequests() async {
        do {
            let stream = try await fastAuthService.subscribeToRequests()

            for try await request in stream {
                pendingRequests.append(request)
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Actions (Connected Devices)

    /// Загрузить список подключённых устройств
    func loadConnectedDevices() async {
        isLoading = true
        defer { isLoading = false }

        do {
            connectedDevices = try await fastAuthService.listConnectedDevices()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Подключить новое устройство
    func connectDevice(token: String) async {
        do {
            try await fastAuthService.connectDevice(deviceToken: token)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
