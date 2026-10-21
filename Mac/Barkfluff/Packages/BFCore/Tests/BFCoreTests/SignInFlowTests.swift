import XCTest
import BFCore
import BFNetworking

final class SignInFlowTests: XCTestCase {
    @MainActor
    func testPasswordOnlyApprovedChallengeCompletesAndPersistsThroughAuthService() async throws {
        let repository = ScriptedSignInRepository(mode: .password)
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: repository, tokens: tokens)
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueBegin(.success(challenge(state: .approved, factor: .unknown(0), needsCode: false)))
        await repository.enqueueCompletion(.success(completion("password-session")))

        await controller.resolveIdentifier("  person@example.test \n")
        XCTAssertEqual(controller.step, .passwordRequired)
        await controller.submitPassword("correct horse")

        XCTAssertEqual(controller.step, .authenticated)
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "password-session-access")
        XCTAssertEqual(saved.refreshToken, "password-session-refresh")
        let calls = await repository.beginCalls()
        XCTAssertEqual(calls.first?.login, "person@example.test")
        XCTAssertEqual(calls.first?.password, "correct horse")
        XCTAssertTrue(calls.first?.reportLoginModeMismatch == true)
        let completionCalls = await repository.completionCalls()
        XCTAssertEqual(completionCalls.count, 1)
    }

    @MainActor
    func testPasswordSecondFactorRecoveryRemainsAvailableAfterDeliveryFailureAndWrongCodeCanRetry() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: repository, tokens: tokens)
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueBegin(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await repository.enqueueBegin(.success(challenge(
            factor: .authenticator,
            needsCode: true,
            availableFactors: [.email, .telegram, .authenticator]
        )))
        await repository.enqueueCompletion(.success(SignInCompletion(
            state: .waiting,
            session: nil,
            errorCode: "invalid_code"
        )))
        await repository.enqueueCompletion(.success(completion("recovery-session")))

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("secret")
        XCTAssertEqual(controller.error, .transport)
        XCTAssertEqual(controller.step, .passwordRequired)
        XCTAssertTrue(controller.canUseRecoveryCode)

        await controller.useRecoveryCode()
        XCTAssertEqual(controller.step, .challenge)
        XCTAssertTrue(controller.isUsingRecoveryCode)
        XCTAssertEqual(controller.availableFactors, [.email, .telegram, .authenticator])

        await controller.submitCode("wrong-code")
        XCTAssertEqual(controller.error, .invalidCode)
        XCTAssertEqual(controller.step, .challenge)
        XCTAssertNotNil(controller.challenge)

        await controller.submitCode("recovery-code")
        XCTAssertEqual(controller.step, .authenticated)
        let calls = await repository.beginCalls()
        XCTAssertEqual(calls.count, 2)
        XCTAssertTrue(calls[1].useRecoveryCode)
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "recovery-session-access")
    }

    @MainActor
    func testTelegramModeBeginsWithoutPasswordAndOffersRecoveryCode() async throws {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: repository, tokens: tokens)
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: true)))
        await repository.enqueueCompletion(.success(completion("telegram-recovery")))

        await controller.resolveIdentifier("mira")
        XCTAssertEqual(controller.step, .challenge)
        XCTAssertTrue(controller.canUseRecoveryCode)
        await controller.useRecoveryCode()
        XCTAssertTrue(controller.isUsingRecoveryCode)
        await controller.submitCode("recovery-code")

        XCTAssertEqual(controller.step, .authenticated)
        let calls = await repository.beginCalls()
        XCTAssertEqual(calls.count, 2)
        XCTAssertEqual(calls[0].mode, .telegramLogin)
        XCTAssertEqual(calls[0].password, "")
        XCTAssertNil(calls[0].factor)
        XCTAssertFalse(calls[0].useRecoveryCode)
        XCTAssertTrue(calls[1].useRecoveryCode)
        XCTAssertTrue(calls.allSatisfy(\.reportLoginModeMismatch))
    }

    @MainActor
    func testResendCooldownPreservesChallengeOnRateLimitAndFactorSwitchUsesOnlyAdvertisedFactors() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let authService = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        let firstChallenge = challenge(id: "first", factor: .email, needsCode: true, availableFactors: [.email, .telegram])
        await repository.enqueueBegin(.success(firstChallenge))
        await repository.enqueueResend(.failure(.rateLimited))
        await repository.enqueueResend(.success(firstChallenge))
        await repository.enqueueBegin(.success(challenge(id: "second", factor: .telegram, needsCode: true, availableFactors: [.email, .telegram])))

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("secret")
        XCTAssertTrue(controller.canResend)

        await controller.resendCode()
        XCTAssertEqual(controller.error, .rateLimited)
        XCTAssertEqual(controller.challenge?.reference?.id, "first")

        await controller.resendCode()
        XCTAssertFalse(controller.canResend)
        XCTAssertEqual(controller.resendSecondsRemaining, 60)
        XCTAssertEqual(controller.challenge?.reference?.id, "first")

        await controller.selectFactor(.authenticator)
        XCTAssertEqual(controller.challenge?.reference?.id, "first")
        let beginCallsBeforeUnsupportedFactor = await repository.beginCalls()
        XCTAssertEqual(beginCallsBeforeUnsupportedFactor.count, 1)

        await controller.selectFactor(.telegram)
        XCTAssertEqual(controller.challenge?.reference?.id, "second")
        let beginCalls = await repository.beginCalls()
        XCTAssertEqual(beginCalls.count, 2)
        XCTAssertEqual(beginCalls[1].factor, .telegram)
        let cancelledReferences = await repository.cancelledReferences()
        XCTAssertEqual(cancelledReferences.map(\.id), ["first"])
    }

    @MainActor
    func testModeMismatchRefreshesModeAndWaitsWithoutResending() async throws {
        let repository = ScriptedSignInRepository(mode: .password)
        let authService = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueResolution(.success(SignInResolution(mode: .password)))
        await repository.enqueueResolution(.success(SignInResolution(mode: .telegramLogin)))
        await repository.enqueueBegin(.success(challenge(errorCode: "login_mode_disabled")))

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("old-password")

        XCTAssertEqual(controller.loginMode, .telegramLogin)
        XCTAssertEqual(controller.step, .telegramReady)
        XCTAssertEqual(controller.error, .modeChanged)
        let beginCalls = await repository.beginCalls()
        XCTAssertEqual(beginCalls.count, 1)
    }

    @MainActor
    func testAccountMissingKeepsIdentifierStepAndDoesNotBeginChallenge() async throws {
        let repository = ScriptedSignInRepository(mode: .password)
        let authService = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueResolution(.success(SignInResolution(mode: .unknown(0), errorCode: "account_not_found")))

        await controller.resolveIdentifier("missing-user")

        XCTAssertEqual(controller.step, .identifier)
        XCTAssertEqual(controller.error, .accountNotFound)
        let beginCalls = await repository.beginCalls()
        XCTAssertTrue(beginCalls.isEmpty)
    }

    @MainActor
    func testServerTerminalChallengeCanBeRetriedAndUnknownStateFailsSafely() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let authService = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueBegin(.success(challenge()))
        await repository.enqueueGet(.success(challenge(state: .expired, expiry: Date().addingTimeInterval(-1))))

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("secret")
        await controller.refreshChallenge()
        XCTAssertEqual(controller.step, .passwordRequired)
        XCTAssertEqual(controller.error, .expired)
        XCTAssertNil(controller.challenge)

        await repository.enqueueBegin(.success(challenge(state: .unknown(77))))
        await controller.submitPassword("secret")
        XCTAssertEqual(controller.step, .passwordRequired)
        XCTAssertEqual(controller.error, .malformedResponse)
        XCTAssertNil(controller.challenge)
        let cancelledReferences = await repository.cancelledReferences()
        XCTAssertTrue(cancelledReferences.contains { $0.id == "challenge-1" })
    }

    @MainActor
    func testLostRecoveryCompletionCanUseCompletedChallengeToRetrieveSession() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: repository, tokens: tokens)
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        let waiting = challenge(factor: .authenticator, needsCode: true)
        await repository.enqueueBegin(.success(waiting))
        await repository.enqueueBegin(.success(waiting))
        await repository.enqueueCompletion(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await repository.enqueueGet(.success(challenge(state: .completed, factor: .authenticator, needsCode: true)))
        await repository.enqueueCompletion(.success(completion("retrieved-session")))

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("secret")
        await controller.useRecoveryCode()
        await controller.submitCode("one-time-recovery-code")
        XCTAssertEqual(controller.error, .transport)
        XCTAssertEqual(controller.step, .challenge)

        await controller.refreshChallenge()
        XCTAssertEqual(controller.step, .authenticated)
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "retrieved-session-access")
        let calls = await repository.completionCalls()
        XCTAssertEqual(calls.count, 2)
        XCTAssertEqual(calls[0].code, "one-time-recovery-code")
        XCTAssertTrue(calls.allSatisfy(\.useRecoveryCode))
        XCTAssertEqual(calls[1].code, "")
    }

    @MainActor
    func testStaleResolveAndCancelledCompletionRepliesCannotCommit() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: repository, tokens: tokens)
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.holdNextResolution()

        let resolveTask = Task { await controller.resolveIdentifier("old-name") }
        await repository.waitForResolutionStart()
        controller.identifierChanged(to: "new-name")
        await repository.releaseResolution(SignInResolution(mode: .password))
        await resolveTask.value
        XCTAssertEqual(controller.step, .identifier)
        XCTAssertNil(controller.loginMode)
        let staleBeginCalls = await repository.beginCalls()
        XCTAssertTrue(staleBeginCalls.isEmpty)

        await repository.enqueueResolution(.success(SignInResolution(mode: .passwordSecondFactor)))
        await repository.enqueueBegin(.success(challenge()))
        await repository.holdNextCompletion()
        await controller.resolveIdentifier("new-name")
        await controller.submitPassword("secret")
        let completionTask = Task { await controller.submitCode("123456") }
        await repository.waitForCompletionStart()
        controller.cancel()
        await repository.releaseCompletion(.success(completion("cancelled-flow")))
        await completionTask.value

        XCTAssertEqual(controller.step, .identifier)
        let savedAfterCancellation = await tokens.snapshot()
        XCTAssertNil(savedAfterCancellation.accessToken)
    }

    @MainActor
    func testIdentifierChangeDuringAttemptCreationInvalidatesTheLateAttempt() async throws {
        let repository = ScriptedSignInRepository(mode: .password)
        let admission = ControlledAdmissionService()
        let controller = SignInFlowController(signInService: repository, admissionService: admission)
        await admission.holdNextBegin()

        let resolveTask = Task { await controller.resolveIdentifier("old-name") }
        await admission.waitForBeginStart()
        controller.identifierChanged(to: "new-name")
        await admission.releaseBegin()
        await resolveTask.value

        let invalidated = await admission.invalidatedAttempts()
        let beginCalls = await repository.beginCalls()
        XCTAssertEqual(controller.step, .identifier)
        XCTAssertEqual(invalidated.count, 1)
        XCTAssertTrue(invalidated[0].isCancelled)
        XCTAssertTrue(beginCalls.isEmpty)
    }

    @MainActor
    func testQRAndPasswordCompletionRaceUsesRealAuthServiceFirstCommit() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: repository, tokens: tokens)
        let controller = SignInFlowController(signInService: authService, admissionService: authService)
        await repository.enqueueBegin(.success(challenge()))
        await repository.holdNextCompletion()

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("secret")
        let challengeTask = Task { await controller.submitCode("123456") }
        await repository.waitForCompletionStart()

        let qrAttempt = await authService.beginSignInAttempt()
        let qrSession = SignInSessionTokens(
            accessToken: "qr-access",
            accessExpiresAt: Date().addingTimeInterval(3_600),
            refreshToken: "qr-refresh",
            refreshExpiresAt: Date().addingTimeInterval(86_400)
        )
        let qrReceipt = await authService.commitSignInSession(qrSession, for: qrAttempt)
        XCTAssertNotNil(qrReceipt)

        await repository.releaseCompletion(.success(completion("password-loser")))
        await challengeTask.value

        XCTAssertEqual(controller.step, .identifier)
        XCTAssertNil(controller.admissionReceipt)
        let saved = await tokens.snapshot()
        let saveCount = await tokens.saveCount()
        XCTAssertEqual(saved.accessToken, "qr-access")
        XCTAssertEqual(saved.refreshToken, "qr-refresh")
        XCTAssertEqual(saveCount, 1)
    }

    @MainActor
    func testSynchronousAttemptCancellationPreventsTokenPersistence() async throws {
        let tokens = TestTokenProvider()
        let authService = makeAuthService(repository: ScriptedSignInRepository(mode: .password), tokens: tokens)
        let attempt = await authService.beginSignInAttempt()
        attempt.cancel()

        let receipt = await authService.commitSignInSession(
            SignInSessionTokens(
                accessToken: "stale-access",
                accessExpiresAt: Date().addingTimeInterval(3_600),
                refreshToken: "stale-refresh",
                refreshExpiresAt: Date().addingTimeInterval(86_400)
            ),
            for: attempt
        )
        let saved = await tokens.snapshot()

        XCTAssertNil(receipt)
        XCTAssertNil(saved.accessToken)
        XCTAssertEqual(saved.saveCount, 0)
    }

    @MainActor
    private func makeAuthService(repository: SignInRepositoryProtocol, tokens: TestTokenProvider) -> AuthService {
        AuthService(
            identityRepository: UnusedIdentityRepository(),
            tokenProvider: tokens,
            connectionManager: ConnectionManager(),
            signInRepository: repository
        )
    }

    private func challenge(
        id: String = "challenge-1",
        state: SignInChallengeState = .waiting,
        factor: SignInFactor? = .email,
        needsCode: Bool = true,
        availableFactors: [SignInFactor] = [.email],
        expiry: Date = Date().addingTimeInterval(3_600),
        errorCode: String? = nil
    ) -> SignInChallenge {
        SignInChallenge(
            reference: SignInChallengeReference(
                id: id,
                secret: "challenge-secret",
                endpoint: ServiceEndpoint(host: "identity.local", port: 5001)
            ),
            state: state,
            factor: factor,
            expiresAt: expiry,
            availableFactors: availableFactors,
            needsCode: needsCode,
            errorCode: errorCode
        )
    }

    private func completion(_ prefix: String) -> SignInCompletion {
        SignInCompletion(
            state: .completed,
            session: SignInSessionTokens(
                accessToken: "\(prefix)-access",
                accessExpiresAt: Date().addingTimeInterval(3_600),
                refreshToken: "\(prefix)-refresh",
                refreshExpiresAt: Date().addingTimeInterval(86_400)
            )
        )
    }
}

private struct BeginCall: Sendable, Equatable {
    let login: String
    let password: String
    let mode: SignInLoginMode
    let factor: SignInFactor?
    let useRecoveryCode: Bool
    let reportLoginModeMismatch: Bool
}

private struct CompletionCall: Sendable {
    let code: String
    let useRecoveryCode: Bool
}

private actor ScriptedSignInRepository: SignInServiceProtocol {
    private var resolutions: [Result<SignInResolution, SignInRepositoryError>] = []
    private var begins: [Result<SignInChallenge, SignInRepositoryError>] = []
    private var challengeReads: [Result<SignInChallenge, SignInRepositoryError>] = []
    private var completions: [Result<SignInCompletion, SignInRepositoryError>] = []
    private var resends: [Result<SignInChallenge, SignInRepositoryError>] = []
    private var beginsCalled: [BeginCall] = []
    private var completionsCalled: [CompletionCall] = []
    private var cancellations: [SignInChallengeReference] = []
    private var mode: SignInLoginMode

    private var holdResolution = false
    private var blockedResolution: CheckedContinuation<SignInResolution, any Error>?
    private var resolutionStarted = false
    private var resolutionWaiters: [CheckedContinuation<Void, Never>] = []
    private var holdCompletion = false
    private var blockedCompletion: CheckedContinuation<SignInCompletion, any Error>?
    private var completionStarted = false
    private var completionWaiters: [CheckedContinuation<Void, Never>] = []

    init(mode: SignInLoginMode) {
        self.mode = mode
    }

    func enqueueResolution(_ result: Result<SignInResolution, SignInRepositoryError>) { resolutions.append(result) }
    func enqueueBegin(_ result: Result<SignInChallenge, SignInRepositoryError>) { begins.append(result) }
    func enqueueGet(_ result: Result<SignInChallenge, SignInRepositoryError>) { challengeReads.append(result) }
    func enqueueCompletion(_ result: Result<SignInCompletion, SignInRepositoryError>) { completions.append(result) }
    func enqueueResend(_ result: Result<SignInChallenge, SignInRepositoryError>) { resends.append(result) }
    func beginCalls() -> [BeginCall] { beginsCalled }
    func completionCalls() -> [CompletionCall] { completionsCalled }
    func cancelledReferences() -> [SignInChallengeReference] { cancellations }

    func holdNextResolution() { holdResolution = true; resolutionStarted = false }
    func holdNextCompletion() { holdCompletion = true; completionStarted = false }

    func waitForResolutionStart() async {
        if resolutionStarted { return }
        await withCheckedContinuation { resolutionWaiters.append($0) }
    }

    func waitForCompletionStart() async {
        if completionStarted { return }
        await withCheckedContinuation { completionWaiters.append($0) }
    }

    func releaseResolution(_ value: SignInResolution) {
        holdResolution = false
        blockedResolution?.resume(returning: value)
        blockedResolution = nil
    }

    func releaseCompletion(_ result: Result<SignInCompletion, SignInRepositoryError>) {
        holdCompletion = false
        guard let continuation = blockedCompletion else { return }
        blockedCompletion = nil
        switch result {
        case .success(let response): continuation.resume(returning: response)
        case .failure(let error): continuation.resume(throwing: error)
        }
    }

    func resolveSignIn(login: String) async throws -> SignInResolution {
        if holdResolution {
            return try await withCheckedThrowingContinuation { continuation in
                blockedResolution = continuation
                resolutionStarted = true
                resolutionWaiters.forEach { $0.resume() }
                resolutionWaiters.removeAll()
            }
        }
        if !resolutions.isEmpty { return try resolutions.removeFirst().get() }
        return SignInResolution(mode: mode)
    }

    func beginSignIn(
        login: String,
        password: String,
        mode: SignInLoginMode,
        factor: SignInFactor?,
        useRecoveryCode: Bool,
        reportLoginModeMismatch: Bool
    ) async throws -> SignInChallenge {
        beginsCalled.append(BeginCall(
            login: login,
            password: password,
            mode: mode,
            factor: factor,
            useRecoveryCode: useRecoveryCode,
            reportLoginModeMismatch: reportLoginModeMismatch
        ))
        if !begins.isEmpty { return try begins.removeFirst().get() }
        return SignInChallenge(
            reference: SignInChallengeReference(id: "default", secret: "secret", endpoint: ServiceEndpoint(host: "identity.local", port: 5001)),
            state: .waiting,
            factor: .email,
            expiresAt: Date().addingTimeInterval(3_600),
            availableFactors: [.email],
            needsCode: true
        )
    }

    func getAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge {
        if !challengeReads.isEmpty { return try challengeReads.removeFirst().get() }
        return SignInChallenge(
            reference: reference,
            state: .waiting,
            factor: .email,
            expiresAt: Date().addingTimeInterval(3_600),
            availableFactors: [.email],
            needsCode: true
        )
    }

    func completeAuthChallenge(
        _ reference: SignInChallengeReference,
        code: String,
        useRecoveryCode: Bool
    ) async throws -> SignInCompletion {
        completionsCalled.append(CompletionCall(code: code, useRecoveryCode: useRecoveryCode))
        if holdCompletion {
            return try await withCheckedThrowingContinuation { continuation in
                blockedCompletion = continuation
                completionStarted = true
                completionWaiters.forEach { $0.resume() }
                completionWaiters.removeAll()
            }
        }
        if !completions.isEmpty { return try completions.removeFirst().get() }
        return SignInCompletion(state: .waiting, session: nil, errorCode: "invalid_code")
    }

    func cancelAuthChallenge(_ reference: SignInChallengeReference) async { cancellations.append(reference) }

    func resendAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge {
        if !resends.isEmpty { return try resends.removeFirst().get() }
        return SignInChallenge(
            reference: reference,
            state: .waiting,
            factor: .email,
            expiresAt: Date().addingTimeInterval(3_600),
            availableFactors: [.email],
            needsCode: true
        )
    }
}

private actor ControlledAdmissionService: SignInSessionAdmissionService {
    private var holdBegin = false
    private var beginStarted = false
    private var blockedBegin: CheckedContinuation<SignInSessionAdmissionAttempt, Never>?
    private var beginWaiters: [CheckedContinuation<Void, Never>] = []
    private var invalidated: [SignInSessionAdmissionAttempt] = []

    func holdNextBegin() { holdBegin = true; beginStarted = false }

    func beginSignInAttempt() async -> SignInSessionAdmissionAttempt {
        guard holdBegin else { return SignInSessionAdmissionAttempt() }
        return await withCheckedContinuation { continuation in
            blockedBegin = continuation
            beginStarted = true
            beginWaiters.forEach { $0.resume() }
            beginWaiters.removeAll()
        }
    }

    func waitForBeginStart() async {
        if beginStarted { return }
        await withCheckedContinuation { beginWaiters.append($0) }
    }

    func releaseBegin() {
        holdBegin = false
        let attempt = SignInSessionAdmissionAttempt()
        blockedBegin?.resume(returning: attempt)
        blockedBegin = nil
    }

    func invalidateSignInAttempt(_ attempt: SignInSessionAdmissionAttempt) {
        attempt.cancel()
        invalidated.append(attempt)
    }

    func commitSignInSession(
        _ session: SignInSessionTokens,
        for attempt: SignInSessionAdmissionAttempt
    ) async -> SignInSessionAdmissionReceipt? { nil }

    func isCurrentSignInSession(_ receipt: SignInSessionAdmissionReceipt) -> Bool { false }

    func invalidatedAttempts() -> [SignInSessionAdmissionAttempt] { invalidated }
}

private struct StoredTokens: Sendable {
    let accessToken: String?
    let refreshToken: String?
    let saveCount: Int
}

private actor TestTokenProvider: TokenProvider {
    private var accessToken: String?
    private var refreshToken: String?
    private var accessExpiry: Date?
    private var refreshExpiry: Date?
    private var host: String?
    private var port: Int?
    private var saves = 0
    private let stableDeviceID = "test-device"

    var currentAccessToken: String? { accessToken }
    var currentRefreshToken: String? { refreshToken }
    var accessTokenExpirationDate: Date? { accessExpiry }
    var refreshTokenExpirationDate: Date? { refreshExpiry }
    var hasRefreshToken: Bool { refreshToken != nil }
    var savedServerHost: String? { host }
    var savedServerPort: Int? { port }
    var deviceID: String { stableDeviceID }

    func saveAccessToken(value: String, expiresAt: Date) async { accessToken = value; accessExpiry = expiresAt; saves += 1 }
    func saveRefreshToken(value: String, expiresAt: Date) async { refreshToken = value; refreshExpiry = expiresAt; saves += 1 }

    func saveTokens(
        accessToken: String,
        accessExpiresAt: Date,
        refreshToken: String,
        refreshExpiresAt: Date
    ) async {
        self.accessToken = accessToken
        self.accessExpiry = accessExpiresAt
        self.refreshToken = refreshToken
        self.refreshExpiry = refreshExpiresAt
        saves += 1
    }

    func clearAll() async { accessToken = nil; refreshToken = nil; accessExpiry = nil; refreshExpiry = nil }
    func purgeAll() async { await clearAll(); host = nil; port = nil }
    func purgeForLogout() async { await clearAll() }
    func saveServerEndpoint(host: String, port: Int) async { self.host = host; self.port = port }
    func exportAllData() async -> TokenExportData {
        TokenExportData(
            accessToken: accessToken,
            accessTokenExpiresAt: accessExpiry,
            refreshToken: refreshToken,
            refreshTokenExpirationDate: refreshExpiry,
            serverHost: host,
            serverPort: port,
            deviceId: stableDeviceID
        )
    }

    func importAllData(_ data: TokenExportData) async throws {
        accessToken = data.accessToken
        accessExpiry = data.accessTokenExpiresAt
        refreshToken = data.refreshToken
        refreshExpiry = data.refreshTokenExpirationDate
        host = data.serverHost
        port = data.serverPort
    }

    func snapshot() -> StoredTokens { StoredTokens(accessToken: accessToken, refreshToken: refreshToken, saveCount: saves) }
    func saveCount() -> Int { saves }
}

private struct UnusedIdentityRepository: IdentityRepositoryProtocol {
    func auth(login: String, isEmail: Bool, password: String, otpCode: String?) async throws -> AuthTokens { throw unused() }
    func createToken(refreshToken: String) async throws -> TokenInfo { throw unused() }
    func createAccount(firstName: String, lastName: String, username: String, email: String) async throws -> String { throw unused() }
    func confirmAccount(codeID: String, code: String) async throws -> TokenInfo { throw unused() }
    func getActiveSessions() async throws -> [SessionInfo] { throw unused() }
    func removeActiveSession(deviceID: String) async throws { throw unused() }
    func enableOTP() async throws -> OTPSetupInfo { throw unused() }
    func confirmOTP(code: String) async throws { throw unused() }
    func disableOTP(code: String) async throws { throw unused() }
    func listOTP() async throws -> OTPStatus { throw unused() }
    func resetPassword(email: String) async throws -> String { throw unused() }
    func confirmResetPassword(codeID: String, code: String, newPassword: String) async throws { throw unused() }
    func setPassword(password: String) async throws { throw unused() }
    func logout() async throws { throw unused() }
    private func unused() -> BFNetworkingError { .unknown("Unused identity call in sign-in tests") }
}
