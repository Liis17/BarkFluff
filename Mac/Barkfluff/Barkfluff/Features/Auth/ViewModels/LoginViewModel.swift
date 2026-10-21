import SwiftUI
import Observation
import BFCore
import BFNetworking

@MainActor
@Observable
final class LoginViewModel {
    var usernameOrEmail = ""
    var password = ""
    var otpCode = ""
    var cooldownTick = 0

    let flow: SignInFlowController

    private let navigation: SignInNavigationCompletion
    private let coordinator: AppCoordinator
    private let loadCurrentUser: @MainActor () async -> Void
    @ObservationIgnored private var cooldownTaskID: UUID?
    @ObservationIgnored private var cooldownTask: Task<Void, Never>?

    init(
        authService: AuthService,
        coordinator: AppCoordinator,
        loadCurrentUser: @escaping @MainActor () async -> Void
    ) {
        self.navigation = SignInNavigationCompletion(admissionService: authService)
        self.coordinator = coordinator
        self.loadCurrentUser = loadCurrentUser
        self.flow = SignInFlowController(signInService: authService, admissionService: authService)
        self.flow.onAuthenticated = { [weak self] _ in
            await Task { @MainActor in await self?.finishIfAuthenticated() }.value
        }
    }

    var isLoading: Bool { flow.isBusy }

    var errorMessage: LocalizedStringResource? {
        guard let error = flow.error else { return nil }
        return LocalizedStringResource(stringLiteral: Self.errorKey(error))
    }

    func identifierChanged(_ value: String) {
        usernameOrEmail = value
        password = ""
        otpCode = ""
        flow.identifierChanged(to: value)
        stopCooldownTicker()
    }

    func login() async {
        switch flow.step {
        case .identifier:
            await flow.resolveIdentifier(usernameOrEmail)
        case .passwordRequired:
            await flow.submitPassword(password)
        case .telegramReady:
            await flow.continueTelegramLogin()
        case .challenge where flow.canRetryChallenge:
            await flow.retryChallenge()
        case .challenge where flow.challenge?.needsCode == true:
            await flow.submitCode(otpCode)
            if flow.error == .invalidCode { otpCode = "" }
        default:
            return
        }
        if flow.error == .modeChanged || flow.error == .accountNotFound
            || flow.error == .rejected || flow.error == .expired || flow.error == .cancelled {
            password = ""
            otpCode = ""
        }
        startCooldownTickerIfNeeded()
    }

    func useRecoveryCode() async {
        otpCode = ""
        await flow.useRecoveryCode(password: password)
        startCooldownTickerIfNeeded()
    }

    func chooseFactor(_ factor: SignInFactor) async {
        otpCode = ""
        await flow.selectFactor(factor)
        startCooldownTickerIfNeeded()
    }

    func resendCode() async {
        await flow.resendCode()
        if flow.error == nil { otpCode = "" }
        startCooldownTickerIfNeeded()
    }

    func resumeForeground() {
        flow.resumeForeground()
        startCooldownTickerIfNeeded()
    }

    func pauseForeground() {
        flow.pauseForeground()
        stopCooldownTicker()
    }

    func returnToIdentifier() {
        flow.returnToIdentifier()
        password = ""
        otpCode = ""
        stopCooldownTicker()
    }

    func leave() {
        flow.cancel()
        password = ""
        otpCode = ""
        stopCooldownTicker()
    }

    func goToRegister() {
        leave()
        coordinator.authScreen = .register
    }

    func chooseServer() {
        leave()
        coordinator.currentState = .serverSelection
    }

    private func finishIfAuthenticated() async {
        guard flow.step == .authenticated, let receipt = flow.admissionReceipt else { return }
        await navigation.finish(
            receipt,
            loadCurrentUser: loadCurrentUser,
            isActive: { self.coordinator.currentState == .authentication && self.coordinator.authScreen == .login
                && self.flow.admissionReceipt == receipt },
            navigate: {
                self.coordinator.isConnectionReady = true
                self.coordinator.currentState = .main
            }
        )
    }

    private func startCooldownTickerIfNeeded() {
        guard flow.challenge != nil, flow.resendAvailableAt != nil, cooldownTask == nil else { return }
        let taskID = UUID()
        cooldownTaskID = taskID
        cooldownTask = Task { [weak self] in
            defer {
                if self?.cooldownTaskID == taskID {
                    self?.cooldownTask = nil
                    self?.cooldownTaskID = nil
                }
            }
            while !Task.isCancelled {
                do { try await Task.sleep(for: .seconds(1)) } catch { return }
                guard let self, !Task.isCancelled, self.flow.challenge != nil else { return }
                self.cooldownTick &+= 1
                if self.flow.resendSecondsRemaining == 0 {
                    return
                }
            }
        }
    }

    private func stopCooldownTicker() {
        cooldownTask?.cancel()
        cooldownTask = nil
        cooldownTaskID = nil
    }

    private static func errorKey(_ error: SignInFlowError) -> String {
        switch error {
        case .accountNotFound: "auth.signin.error.account_not_found"
        case .invalidCredentials: "auth.signin.error.invalid_credentials"
        case .lockedOut: "auth.signin.error.locked_out"
        case .rateLimited: "auth.signin.error.rate_limited"
        case .modeChanged: "auth.signin.error.mode_changed"
        case .invalidCode: "auth.signin.error.invalid_code"
        case .rejected: "auth.signin.error.rejected"
        case .expired: "auth.signin.error.expired"
        case .cancelled: "auth.signin.error.cancelled"
        case .serverUpdateRequired: "auth.signin.error.server_update_required"
        case .transport: "auth.signin.error.transport"
        case .unsupportedServer: "auth.signin.error.unsupported_server"
        case .malformedResponse: "auth.signin.error.malformed_response"
        }
    }
}
