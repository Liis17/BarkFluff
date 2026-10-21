import Foundation
import Observation
import BFNetworking

public enum SignInFlowStep: Sendable, Equatable {
    case identifier
    case resolving
    case passwordRequired
    case telegramReady
    case beginning
    case challenge
    case completing
    case authenticated
}

public enum SignInFlowError: Sendable, Equatable {
    case accountNotFound
    case invalidCredentials
    case lockedOut
    case rateLimited
    case modeChanged
    case invalidCode
    case rejected
    case expired
    case cancelled
    case serverUpdateRequired
    case transport
    case unsupportedServer
    case malformedResponse
}

/// Testable coordinator for the identifier-first Identity challenge flow.
@MainActor
@Observable
public final class SignInFlowController {
    public private(set) var step: SignInFlowStep = .identifier
    public private(set) var loginMode: SignInLoginMode?
    public private(set) var challenge: SignInChallenge?
    public private(set) var error: SignInFlowError?
    public private(set) var isBusy = false
    public private(set) var isUsingRecoveryCode = false
    public private(set) var resendAvailableAt: Date?
    public private(set) var admissionReceipt: SignInSessionAdmissionReceipt?

    @ObservationIgnored private let signInService: SignInServiceProtocol
    @ObservationIgnored private let admissionService: SignInSessionAdmissionService
    @ObservationIgnored private var generation: UInt64 = 0
    @ObservationIgnored private var identifier = ""
    @ObservationIgnored private var password = ""
    @ObservationIgnored private var verificationCode = ""
    @ObservationIgnored private var selectedFactor: SignInFactor?
    @ObservationIgnored private var attempt: SignInSessionAdmissionAttempt?
    @ObservationIgnored private var pollingTask: Task<Void, Never>?
    @ObservationIgnored private let now: @Sendable () -> Date

    public init(
        signInService: SignInServiceProtocol,
        admissionService: SignInSessionAdmissionService,
        now: @escaping @Sendable () -> Date = Date.init
    ) {
        self.signInService = signInService
        self.admissionService = admissionService
        self.now = now
    }

    public var availableFactors: [SignInFactor] {
        challenge?.availableFactors ?? []
    }

    public var canResend: Bool {
        guard let challenge, challenge.state == .waiting, !isUsingRecoveryCode,
              challenge.factor == .email || challenge.factor == .telegram else { return false }
        return resendAvailableAt.map { $0 <= now() } ?? true
    }

    public var resendSecondsRemaining: Int {
        guard let resendAvailableAt else { return 0 }
        return max(0, Int(ceil(resendAvailableAt.timeIntervalSince(now()))))
    }

    public var canUseRecoveryCode: Bool {
        guard let loginMode else { return false }
        return loginMode == .telegramLogin || loginMode == .passwordSecondFactor
    }

    public func identifierChanged(to value: String) {
        guard identifier != value else { return }
        identifier = value
        invalidateCurrentFlow(clearIdentifier: false)
        step = .identifier
    }

    public func resolveIdentifier(_ value: String) async {
        guard !isBusy else { return }
        if identifier != value { identifierChanged(to: value) }
        let login = identifier.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !login.isEmpty else { return }

        generation &+= 1
        let requestGeneration = generation
        error = nil
        isBusy = true
        step = .resolving
        if attempt == nil {
            attempt = await admissionService.beginSignInAttempt()
        }
        guard isCurrent(requestGeneration) else { return }

        do {
            let resolution = try await signInService.resolveSignIn(login: login)
            guard isCurrent(requestGeneration) else { return }
            if resolution.errorCode == "account_not_found" {
                fail(.accountNotFound, step: .identifier)
                return
            }
            guard resolution.errorCode == nil else {
                fail(.unsupportedServer, step: .identifier)
                return
            }
            guard case .unknown = resolution.mode else {
                loginMode = resolution.mode
                switch resolution.mode {
                case .password, .passwordSecondFactor:
                    step = .passwordRequired
                    isBusy = false
                case .telegramLogin:
                    password = ""
                    await beginChallenge(factor: nil, recovery: false, generation: requestGeneration)
                case .unknown:
                    break
                }
                return
            }
            fail(.unsupportedServer, step: .identifier)
        } catch {
            guard isCurrent(requestGeneration) else { return }
            fail(map(error), step: .identifier)
        }
    }

    public func submitPassword(_ value: String) async {
        guard !isBusy, step == .passwordRequired,
              loginMode == .password || loginMode == .passwordSecondFactor,
              !value.isEmpty else { return }
        password = value
        await beginChallenge(factor: nil, recovery: false)
    }

    /// Explicit continuation for a Telegram login whose previous BeginSignIn reported a changed mode.
    public func continueTelegramLogin() async {
        guard !isBusy, step == .telegramReady, loginMode == .telegramLogin else { return }
        password = ""
        await beginChallenge(factor: nil, recovery: false)
    }

    public func useRecoveryCode() async {
        guard !isBusy, canUseRecoveryCode, let loginMode,
              loginMode == .telegramLogin || !password.isEmpty else { return }
        generation &+= 1
        let requestGeneration = generation
        verificationCode = ""
        error = nil
        isUsingRecoveryCode = true
        let factor = selectedFactor
        if let reference = challenge?.reference {
            challenge = nil
            await signInService.cancelAuthChallenge(reference)
        }
        guard isCurrent(requestGeneration) else { return }
        await beginChallenge(
            factor: loginMode == .passwordSecondFactor ? factor : nil,
            recovery: true,
            generation: requestGeneration
        )
    }

    public func submitCode(_ value: String) async {
        guard !isBusy, step == .challenge, challenge?.needsCode == true else { return }
        verificationCode = value
        await completeCurrentChallenge(code: value)
    }

    public func selectFactor(_ factor: SignInFactor) async {
        guard !isBusy, loginMode == .passwordSecondFactor,
              challenge?.availableFactors.contains(factor) == true,
              factor != .unknown(0) else { return }
        let reference = challenge?.reference
        generation &+= 1
        let requestGeneration = generation
        challenge = nil
        verificationCode = ""
        isUsingRecoveryCode = false
        selectedFactor = factor
        if let reference { await signInService.cancelAuthChallenge(reference) }
        guard isCurrent(requestGeneration) else { return }
        await beginChallenge(factor: factor, recovery: false, generation: requestGeneration)
    }

    public func resendCode() async {
        guard !isBusy, canResend, let reference = challenge?.reference else { return }
        let requestGeneration = generation
        isBusy = true
        error = nil
        do {
            let response = try await signInService.resendAuthChallenge(reference)
            guard isCurrent(requestGeneration) else { await cancelIfStale(response); return }
            guard response.errorCode == nil else {
                fail(.unsupportedServer, step: .challenge)
                return
            }
            do {
                try validateActiveChallenge(response)
            } catch {
                await cancelIfStale(response)
                fail(.malformedResponse, step: .challenge)
                return
            }
            guard response.state == .waiting,
                  response.reference?.id == reference.id,
                  response.factor == .email || response.factor == .telegram else {
                fail(.malformedResponse, step: .identifier)
                return
            }
            challenge = response
            verificationCode = ""
            resendAvailableAt = now().addingTimeInterval(60)
            step = .challenge
            isBusy = false
        } catch {
            guard isCurrent(requestGeneration) else { return }
            fail(map(error), step: .challenge)
        }
    }

    public func refreshChallenge() async {
        guard !isBusy, let reference = challenge?.reference else { return }
        guard !isExpired(challenge) else {
            finishTerminal(.expired)
            return
        }
        let requestGeneration = generation
        isBusy = true
        do {
            let response = try await signInService.getAuthChallenge(reference)
            guard isCurrent(requestGeneration) else { await cancelIfStale(response); return }
            guard response.errorCode == nil else {
                fail(.unsupportedServer, step: .challenge)
                return
            }
            do {
                try validateChallenge(response)
            } catch {
                await cancelIfStale(response)
                fail(.malformedResponse, step: .identifier)
                return
            }
            guard response.reference?.id == reference.id else {
                fail(.malformedResponse, step: .identifier)
                return
            }
            switch response.state {
            case .waiting:
                challenge = response
                step = .challenge
                isBusy = false
            case .approved, .completed:
                challenge = response
                isBusy = false
                await completeCurrentChallenge(
                    code: "",
                    useRecoveryCode: isUsingRecoveryCode,
                    expectedGeneration: requestGeneration
                )
            case .rejected:
                finishTerminal(.rejected)
            case .expired:
                finishTerminal(.expired)
            case .cancelled:
                finishTerminal(.cancelled)
            case .unknown:
                fail(.unsupportedServer, step: .identifier)
            }
        } catch {
            guard isCurrent(requestGeneration) else { return }
            fail(map(error), step: .challenge)
        }
    }

    /// Rejoin the same in-memory challenge after returning to the foreground.
    public func resumeForeground() {
        pollingTask?.cancel()
        guard shouldPollTelegram else { return }
        pollingTask = Task { [weak self] in
            guard let self else { return }
            await self.refreshChallenge()
            while !Task.isCancelled {
                do { try await Task.sleep(for: .seconds(2)) } catch { return }
                guard !Task.isCancelled, self.shouldPollTelegram else { return }
                await self.refreshChallenge()
            }
        }
    }

    public func pauseForeground() {
        pollingTask?.cancel()
        pollingTask = nil
    }

    public func returnToIdentifier() {
        invalidateCurrentFlow(clearIdentifier: false)
        step = .identifier
    }

    public func cancel() {
        invalidateCurrentFlow(clearIdentifier: true)
        step = .identifier
    }

    public func isCurrent(_ receipt: SignInSessionAdmissionReceipt) async -> Bool {
        guard admissionReceipt == receipt else { return false }
        return await admissionService.isCurrentSignInSession(receipt)
    }

    private var shouldPollTelegram: Bool {
        challenge?.state == .waiting && challenge?.factor == .telegram && !isUsingRecoveryCode
            && (step == .challenge)
    }

    private func beginChallenge(
        factor: SignInFactor?,
        recovery: Bool,
        generation explicitGeneration: UInt64? = nil
    ) async {
        guard let loginMode else { return }
        let requestGeneration: UInt64
        if let explicitGeneration {
            requestGeneration = explicitGeneration
        } else {
            generation &+= 1
            requestGeneration = generation
        }
        error = nil
        isBusy = true
        step = .beginning
        do {
            let response = try await signInService.beginSignIn(
                login: identifier.trimmingCharacters(in: .whitespacesAndNewlines),
                password: password,
                mode: loginMode,
                factor: loginMode == .telegramLogin ? nil : factor,
                useRecoveryCode: recovery,
                reportLoginModeMismatch: true
            )
            guard isCurrent(requestGeneration) else { await cancelIfStale(response); return }
            if response.errorCode == "account_not_found" {
                challenge = nil
                password = ""
                verificationCode = ""
                isUsingRecoveryCode = false
                fail(.accountNotFound, step: .identifier)
                return
            }
            if response.errorCode == "login_mode_disabled" {
                await refreshModeAfterMismatch(generation: requestGeneration)
                return
            }
            guard response.errorCode == nil else {
                await cancelIfStale(response)
                fail(.unsupportedServer, step: retryStep(for: loginMode))
                return
            }
            do {
                try validateActiveChallenge(response)
            } catch {
                await cancelIfStale(response)
                fail(.malformedResponse, step: retryStep(for: loginMode))
                return
            }
            self.challenge = response
            selectedFactor = response.factor
            isUsingRecoveryCode = recovery
            verificationCode = ""
            resendAvailableAt = nil
            switch response.state {
            case .waiting:
                step = .challenge
                isBusy = false
                if response.needsCode {
                    error = nil
                }
                if response.factor == .telegram && !recovery { resumeForeground() }
            case .approved, .completed:
                isBusy = false
                await completeCurrentChallenge(
                    code: "",
                    useRecoveryCode: recovery,
                    expectedGeneration: requestGeneration
                )
            case .rejected:
                finishTerminal(.rejected)
            case .expired:
                finishTerminal(.expired)
            case .cancelled:
                finishTerminal(.cancelled)
            case .unknown:
                fail(.unsupportedServer, step: retryStep(for: loginMode))
            }
        } catch {
            guard isCurrent(requestGeneration) else { return }
            if case .serverUpdateRequired? = error as? SignInRepositoryError {
                fail(.serverUpdateRequired, step: retryStep(for: loginMode))
            } else {
                fail(map(error), step: retryStep(for: loginMode))
            }
        }
    }

    private func completeCurrentChallenge(
        code: String,
        useRecoveryCode: Bool? = nil,
        expectedGeneration: UInt64? = nil
    ) async {
        guard let reference = challenge?.reference else {
            fail(.malformedResponse, step: retryStep(for: loginMode))
            return
        }
        if isExpired(challenge) {
            finishTerminal(.expired)
            return
        }
        let requestGeneration = expectedGeneration ?? generation
        guard isCurrent(requestGeneration) else { return }
        step = .completing
        isBusy = true
        error = nil
        do {
            let response = try await signInService.completeAuthChallenge(
                reference,
                code: code,
                useRecoveryCode: useRecoveryCode ?? isUsingRecoveryCode
            )
            guard isCurrent(requestGeneration) else { return }
            if response.errorCode == "invalid_code" {
                verificationCode = ""
                if response.state == .rejected {
                    finishTerminal(.rejected)
                } else if response.state == .waiting {
                    setChallengeState(.waiting)
                    step = .challenge
                    error = .invalidCode
                    isBusy = false
                } else {
                    fail(.unsupportedServer, step: .challenge)
                }
                return
            }
            guard response.errorCode == nil else {
                fail(.unsupportedServer, step: .challenge)
                return
            }
            switch response.state {
            case .completed:
                guard let session = response.session else {
                    fail(.malformedResponse, step: retryStep(for: loginMode))
                    return
                }
                guard let attempt else {
                    fail(.cancelled, step: .identifier)
                    return
                }
                guard let receipt = await admissionService.commitSignInSession(session, for: attempt) else {
                    challenge = nil
                    isBusy = false
                    step = .identifier
                    error = .cancelled
                    return
                }
                guard isCurrent(requestGeneration) else { return }
                admissionReceipt = receipt
                challenge = nil
                password = ""
                verificationCode = ""
                isBusy = false
                error = nil
                step = .authenticated
                pauseForeground()
            case .waiting:
                setChallengeState(.waiting)
                step = .challenge
                isBusy = false
                failWithoutChangingStep(.invalidCode)
            case .approved:
                setChallengeState(.approved)
                fail(.unsupportedServer, step: .challenge)
            case .rejected:
                finishTerminal(.rejected)
            case .expired:
                finishTerminal(.expired)
            case .cancelled:
                finishTerminal(.cancelled)
            case .unknown:
                fail(.unsupportedServer, step: .challenge)
            }
        } catch {
            guard isCurrent(requestGeneration) else { return }
            // Keep the challenge and submitted recovery code in memory so a lost completion
            // response can be retried after the server has already consumed the code.
            fail(map(error), step: .challenge)
        }
    }

    private func refreshModeAfterMismatch(generation requestGeneration: UInt64) async {
        password = ""
        verificationCode = ""
        isUsingRecoveryCode = false
        challenge = nil
        loginMode = nil
        isBusy = true
        do {
            let resolution = try await signInService.resolveSignIn(
                login: identifier.trimmingCharacters(in: .whitespacesAndNewlines)
            )
            guard isCurrent(requestGeneration) else { return }
            guard resolution.errorCode == nil else {
                fail(resolution.errorCode == "account_not_found" ? .accountNotFound : .unsupportedServer, step: .identifier)
                return
            }
            guard case .unknown = resolution.mode else {
                loginMode = resolution.mode
                error = .modeChanged
                step = resolution.mode == .telegramLogin ? .telegramReady : .passwordRequired
                isBusy = false
                return
            }
            fail(.unsupportedServer, step: .identifier)
        } catch {
            guard isCurrent(requestGeneration) else { return }
            fail(map(error), step: .identifier)
        }
    }

    private func validateActiveChallenge(_ value: SignInChallenge) throws {
        try validateChallenge(value)
        guard let reference = value.reference, !reference.id.isEmpty, !reference.secret.isEmpty,
              let expiresAt = value.expiresAt, expiresAt > now() else {
            throw SignInRepositoryError.malformedResponse
        }
        _ = reference
    }

    private func setChallengeState(_ state: SignInChallengeState) {
        guard let challenge else { return }
        self.challenge = SignInChallenge(
            reference: challenge.reference,
            state: state,
            factor: challenge.factor,
            telegramURL: challenge.telegramURL,
            expiresAt: challenge.expiresAt,
            availableFactors: challenge.availableFactors,
            needsCode: challenge.needsCode,
            errorCode: challenge.errorCode
        )
    }

    private func validateChallenge(_ value: SignInChallenge) throws {
        if case .unknown = value.state { throw SignInRepositoryError.malformedResponse }
        if let factor = value.factor, case .unknown(let raw) = factor, raw != 0 {
            throw SignInRepositoryError.malformedResponse
        }
        if value.availableFactors.contains(where: { if case .unknown = $0 { true } else { false } }) {
            throw SignInRepositoryError.malformedResponse
        }
        if value.state == .waiting || value.state == .approved || value.state == .completed {
            guard let expiresAt = value.expiresAt, expiresAt > now() else {
                throw SignInRepositoryError.malformedResponse
            }
        }
        switch loginMode {
        case .some(.password):
            guard value.factor == .unknown(0) else { throw SignInRepositoryError.malformedResponse }
        case .some(.telegramLogin):
            guard value.factor == .telegram else { throw SignInRepositoryError.malformedResponse }
        case .some(.passwordSecondFactor):
            guard value.factor == .email || value.factor == .telegram || value.factor == .authenticator else {
                throw SignInRepositoryError.malformedResponse
            }
        case .some(.unknown), .none:
            break
        }
    }

    private func finishTerminal(_ terminal: SignInFlowError) {
        let reference = challenge?.reference
        challenge = nil
        verificationCode = ""
        isUsingRecoveryCode = false
        resendAvailableAt = nil
        isBusy = false
        error = terminal
        step = retryStep(for: loginMode)
        pauseForeground()
        if let reference { Task { await signInService.cancelAuthChallenge(reference) } }
    }

    private func retryStep(for mode: SignInLoginMode?) -> SignInFlowStep {
        mode == .telegramLogin ? .telegramReady : .passwordRequired
    }

    private func isExpired(_ challenge: SignInChallenge?) -> Bool {
        guard let challenge, challenge.state == .waiting || challenge.state == .approved || challenge.state == .completed else {
            return false
        }
        guard let expiresAt = challenge.expiresAt else { return true }
        return expiresAt <= now()
    }

    private func invalidateCurrentFlow(clearIdentifier: Bool) {
        generation &+= 1
        pauseForeground()
        let reference = challenge?.reference
        let oldAttempt = attempt
        oldAttempt?.cancel()
        challenge = nil
        loginMode = nil
        selectedFactor = nil
        error = nil
        isBusy = false
        isUsingRecoveryCode = false
        resendAvailableAt = nil
        admissionReceipt = nil
        attempt = nil
        password = ""
        verificationCode = ""
        if clearIdentifier { identifier = "" }
        if let reference { Task { await signInService.cancelAuthChallenge(reference) } }
        if let oldAttempt { Task { await admissionService.invalidateSignInAttempt(oldAttempt) } }
    }

    /// Request lifetime is controlled by generation. A cancelled Telegram polling task can still
    /// receive its in-flight Get response; cancellation should stop the next poll, not cancel the
    /// user's server challenge.
    private func isCurrent(_ requestGeneration: UInt64) -> Bool {
        generation == requestGeneration
    }

    private func cancelIfStale(_ challenge: SignInChallenge) async {
        if let reference = challenge.reference { await signInService.cancelAuthChallenge(reference) }
    }

    private func fail(_ value: SignInFlowError, step nextStep: SignInFlowStep) {
        error = value
        step = nextStep
        isBusy = false
    }

    private func failWithoutChangingStep(_ value: SignInFlowError) {
        error = value
    }

    private func map(_ error: Error) -> SignInFlowError {
        guard let signInError = error as? SignInRepositoryError else { return .transport }
        switch signInError {
        case .invalidCredentials: return .invalidCredentials
        case .lockedOut: return .lockedOut
        case .rateLimited: return .rateLimited
        case .serverUpdateRequired: return .serverUpdateRequired
        case .cancelled: return .cancelled
        case .transport: return .transport
        case .malformedResponse: return .malformedResponse
        }
    }
}
