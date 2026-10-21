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

    @ObservationIgnored public var onAuthenticated: (@MainActor (SignInSessionAdmissionReceipt) async -> Void)?
    @ObservationIgnored private var refreshID: UUID?
    @ObservationIgnored private let signInService: SignInServiceProtocol
    @ObservationIgnored private let admissionService: SignInSessionAdmissionService
    @ObservationIgnored private var generation: UInt64 = 0
    @ObservationIgnored private var identifier = ""
    @ObservationIgnored private var password = ""
    @ObservationIgnored private var verificationCode = ""
    @ObservationIgnored private var selectedFactor: SignInFactor?
    @ObservationIgnored private var attempt: SignInSessionAdmissionAttempt?
    @ObservationIgnored private var pollingTask: Task<Void, Never>?
    @ObservationIgnored private var pollingTaskID: UUID?
    @ObservationIgnored private var pendingTelegramCompletionRetry = false
    @ObservationIgnored private var foregroundIsActive = false
    @ObservationIgnored private let now: @Sendable () -> Date
    @ObservationIgnored private let sleepBetweenPolls: @Sendable () async throws -> Void

    var hasForegroundPollingTask: Bool { pollingTask != nil }
    var foregroundPollingTaskForTesting: Task<Void, Never>? { pollingTask }

    public init(
        signInService: SignInServiceProtocol,
        admissionService: SignInSessionAdmissionService,
        now: @escaping @Sendable () -> Date = Date.init,
        sleepBetweenPolls: @escaping @Sendable () async throws -> Void = { try await Task.sleep(for: .seconds(2)) }
    ) {
        self.signInService = signInService
        self.admissionService = admissionService
        self.now = now
        self.sleepBetweenPolls = sleepBetweenPolls
    }

    public var canRetryChallenge: Bool {
        step == .challenge && challenge != nil && challenge?.needsCode == false && error != nil
    }

    public func retryChallenge() async {
        guard canRetryChallenge, !isBusy else { return }
        await refreshChallenge()
    }

    public var availableFactors: [SignInFactor] {
        challenge?.availableFactors ?? []
    }

    public var canResend: Bool {
        guard !isBusy, showsResendControl else { return false }
        return resendAvailableAt.map { $0 <= now() } ?? true
    }

    /// Whether the current challenge should offer the resend action and its cooldown.
    /// Code delivery and Telegram approval both use this control.
    public var showsResendControl: Bool {
        guard let challenge, challenge.state == .waiting, !isUsingRecoveryCode,
              !isExpired(challenge) else { return false }
        return challenge.factor == .email || challenge.factor == .telegram
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
            let newAttempt = await admissionService.beginSignInAttempt()
            guard generation == requestGeneration else {
                newAttempt.cancel()
                await admissionService.invalidateSignInAttempt(newAttempt)
                return
            }
            attempt = newAttempt
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

    public func useRecoveryCode(password enteredPassword: String? = nil) async {
        guard !isBusy, canUseRecoveryCode, let loginMode else { return }
        if loginMode == .passwordSecondFactor, let enteredPassword {
            password = enteredPassword
        }
        guard loginMode == .telegramLogin || !password.isEmpty else { return }
        generation &+= 1
        let requestGeneration = generation
        stopPolling()
        pendingTelegramCompletionRetry = false
        isBusy = true
        step = .beginning
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
              factor != .unknown(0), factor != selectedFactor else { return }
        let reference = challenge?.reference
        generation &+= 1
        let requestGeneration = generation
        stopPolling()
        pendingTelegramCompletionRetry = false
        isBusy = true
        step = .beginning
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
        refreshID = nil
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
                guard isCurrent(requestGeneration) else { return }
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
        await refreshChallenge(fromForegroundPolling: false)
    }

    private func refreshChallenge(fromForegroundPolling: Bool) async {
        guard !Task.isCancelled, !isBusy, refreshID == nil, let reference = challenge?.reference else { return }
        guard !isExpired(challenge) else {
            finishTerminal(.expired)
            return
        }
        let requestGeneration = generation
        let requestID = UUID()
        refreshID = requestID
        defer { if refreshID == requestID { refreshID = nil } }
        do {
            let response = try await signInService.getAuthChallenge(reference)
            guard !Task.isCancelled, refreshID == requestID else { return }
            guard isCurrent(requestGeneration) else { await cancelIfStale(response); return }
            guard !isBusy, step == .challenge, challenge?.reference == reference else { return }
            guard response.errorCode == nil else {
                finishTerminal(.unsupportedServer)
                return
            }
            do {
                try validateChallenge(response)
            } catch {
                await cancelIfStale(response)
                guard isCurrent(requestGeneration), refreshID == requestID, !isBusy, step == .challenge else { return }
                finishTerminal(.malformedResponse)
                return
            }
            guard response.reference == reference else {
                await cancelIfStale(response)
                guard isCurrent(requestGeneration), refreshID == requestID, !isBusy, step == .challenge else { return }
                finishTerminal(.malformedResponse)
                return
            }
            switch response.state {
            case .waiting:
                challenge = response
                error = nil
                step = .challenge
            case .approved, .completed:
                challenge = response
                isBusy = false
                await completeCurrentChallenge(
                    code: "",
                    useRecoveryCode: isUsingRecoveryCode,
                    expectedGeneration: requestGeneration,
                    fromForegroundPolling: fromForegroundPolling
                )
            case .rejected:
                finishTerminal(.rejected)
            case .expired:
                finishTerminal(.expired)
            case .cancelled:
                finishTerminal(.cancelled)
            case .unknown:
                finishTerminal(.unsupportedServer)
            }
        } catch {
            guard !Task.isCancelled, refreshID == requestID, isCurrent(requestGeneration),
                  !isBusy, step == .challenge, challenge?.reference == reference else { return }
            if error is CancellationError { return }
            switch error as? SignInRepositoryError {
            case .transport(let status, _) where [5, 7, 16].contains(status):
                finishTerminal(.cancelled)
            case .transport(let status, _) where ![-1, 2, 4, 10, 13, 14].contains(status):
                finishTerminal(.unsupportedServer)
            case .rateLimited, .transport, .none:
                fail(map(error), step: .challenge)
            default:
                finishTerminal(map(error))
            }
        }
    }

    /// Rejoin the same in-memory challenge after returning to the foreground.
    public func resumeForeground() {
        stopPolling()
        foregroundIsActive = true
        startForegroundPollingIfNeeded()
    }

    public func pauseForeground() {
        foregroundIsActive = false
        stopPolling()
    }

    private func stopPolling() {
        pollingTask?.cancel()
        pollingTask = nil
        pollingTaskID = nil
        refreshID = nil
    }

    public func returnToIdentifier() {
        invalidateCurrentFlow(clearIdentifier: false)
        step = .identifier
    }

    public func cancel() {
        foregroundIsActive = false
        invalidateCurrentFlow(clearIdentifier: true)
        step = .identifier
    }

    public func isCurrent(_ receipt: SignInSessionAdmissionReceipt) async -> Bool {
        guard admissionReceipt == receipt else { return false }
        return await admissionService.isCurrentSignInSession(receipt)
    }

    private var shouldPollTelegram: Bool {
        loginMode == .telegramLogin && challenge?.needsCode == false
            && challenge?.state == .waiting && challenge?.factor == .telegram && !isUsingRecoveryCode
            && (step == .challenge)
    }

    private var shouldRefreshTelegramChallenge: Bool {
        guard loginMode == .telegramLogin, challenge?.needsCode == false,
              challenge?.factor == .telegram, !isUsingRecoveryCode else { return false }
        let completionPending = challenge?.state == .approved || challenge?.state == .completed
        if step == .challenge { return shouldPollTelegram || completionPending }
        // Keep a foreground task available when the app resumes during Complete. Its refresh
        // exits while the completion is busy; task cleanup below retries if that completion
        // was cancelled after a newer foreground task took its place.
        return step == .completing && isBusy && completionPending
    }

    private var hasPendingTelegramCompletionRetry: Bool {
        guard pendingTelegramCompletionRetry, foregroundIsActive, !isBusy, error == nil,
              loginMode == .telegramLogin, challenge?.needsCode == false,
              challenge?.factor == .telegram, !isUsingRecoveryCode,
              step == .challenge else { return false }
        return challenge?.state == .approved || challenge?.state == .completed
    }

    private func pollingTaskDidFinish(_ taskID: UUID) {
        guard pollingTaskID == taskID else { return }
        pollingTask = nil
        pollingTaskID = nil
        if hasPendingTelegramCompletionRetry {
            startForegroundPollingIfNeeded()
        }
    }

    private func startForegroundPollingIfNeeded() {
        guard foregroundIsActive, shouldRefreshTelegramChallenge, pollingTask == nil else { return }
        pendingTelegramCompletionRetry = false
        let taskID = UUID()
        pollingTaskID = taskID
        pollingTask = Task { [weak self] in
            guard let self else { return }
            defer { self.pollingTaskDidFinish(taskID) }
            await self.refreshChallenge(fromForegroundPolling: true)
            while !Task.isCancelled, self.shouldPollTelegram {
                do { try await self.sleepBetweenPolls() } catch { return }
                guard !Task.isCancelled, self.shouldPollTelegram else { return }
                await self.refreshChallenge(fromForegroundPolling: true)
            }
        }
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
        defer {
            if isCurrent(requestGeneration), challenge == nil, step != .authenticated {
                isUsingRecoveryCode = false
            }
        }
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
                await cancelIfStale(response)
                guard isCurrent(requestGeneration) else { return }
                challenge = nil
                password = ""
                verificationCode = ""
                isUsingRecoveryCode = false
                fail(.accountNotFound, step: .identifier)
                return
            }
            if response.errorCode == "login_mode_disabled" {
                await cancelIfStale(response)
                guard isCurrent(requestGeneration) else { return }
                await refreshModeAfterMismatch(generation: requestGeneration)
                return
            }
            guard response.errorCode == nil else {
                await cancelIfStale(response)
                guard isCurrent(requestGeneration) else { return }
                fail(.unsupportedServer, step: retryStep(for: loginMode))
                return
            }
            do {
                try validateActiveChallenge(response, recovery: recovery)
            } catch {
                await cancelIfStale(response)
                guard isCurrent(requestGeneration) else { return }
                fail(.malformedResponse, step: retryStep(for: loginMode))
                return
            }
            self.challenge = response
            selectedFactor = response.factor
            isUsingRecoveryCode = recovery
            verificationCode = ""
            resendAvailableAt = response.state == .waiting && !recovery
                && (response.factor == .email || response.factor == .telegram)
                ? now().addingTimeInterval(60) : nil
            switch response.state {
            case .waiting:
                step = .challenge
                isBusy = false
                if response.needsCode {
                    error = nil
                }
                if shouldPollTelegram { startForegroundPollingIfNeeded() }
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
        expectedGeneration: UInt64? = nil,
        fromForegroundPolling: Bool = false
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
        refreshID = nil
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
                let receipt = await admissionService.commitSignInSession(session, for: attempt)
                guard isCurrent(requestGeneration) else {
                    await admissionService.invalidateSignInAttempt(receipt?.attempt ?? attempt)
                    return
                }
                guard let receipt else {
                    challenge = nil
                    isBusy = false
                    step = .identifier
                    error = .cancelled
                    return
                }
                admissionReceipt = receipt
                challenge = nil
                password = ""
                verificationCode = ""
                pendingTelegramCompletionRetry = false
                isBusy = false
                error = nil
                step = .authenticated
                stopPolling()
                await onAuthenticated?(receipt)
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
            if fromForegroundPolling && Task.isCancelled {
                step = .challenge
                isBusy = false
                self.error = nil
                pendingTelegramCompletionRetry = true
                startForegroundPollingIfNeeded()
                return
            }
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

    private func validateActiveChallenge(_ value: SignInChallenge, recovery: Bool? = nil) throws {
        try validateChallenge(value, recovery: recovery)
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

    private func validateChallenge(_ value: SignInChallenge, recovery: Bool? = nil) throws {
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
            guard value.factor == .email || value.factor == .telegram || value.factor == .authenticator
                || ((recovery ?? isUsingRecoveryCode) && value.factor == .unknown(0)) else {
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
        pendingTelegramCompletionRetry = false
        isBusy = false
        error = terminal
        step = retryStep(for: loginMode)
        stopPolling()
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
        stopPolling()
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
        pendingTelegramCompletionRetry = false
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
