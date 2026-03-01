//
//  LoginViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана входа (iOS версия)
//

import SwiftUI
import Observation
import BFCore

@Observable
final class LoginViewModel {
    var usernameOrEmail = ""
    var password = ""
    var otpCode = ""
    var isLoading = false
    var errorMessage: String?
    var needsOTP = false

    private let authService: AuthServiceProtocol
    private let coordinator: AppCoordinator

    init(authService: AuthServiceProtocol, coordinator: AppCoordinator) {
        self.authService = authService
        self.coordinator = coordinator
    }

    func login() async {
        isLoading = true
        errorMessage = nil

        do {
            let otp = needsOTP && !otpCode.isEmpty ? otpCode : nil
            try await authService.login(
                login: usernameOrEmail,
                password: password,
                otpCode: otp
            )
            coordinator.currentState = .main
        } catch let error as BFError where error == .otpRequired {
            needsOTP = true
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    func goToRegister() {
        coordinator.authScreen = .register
    }
}
