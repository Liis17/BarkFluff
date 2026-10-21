import XCTest
@testable import BFCore
import BFNetworking
import Synchronization
import Observation

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
    func testTelegramApprovalResendHonorsInitialAndRepeatedCooldown() async {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let authService = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let clock = Mutex(Date())
        let controller = SignInFlowController(
            signInService: authService,
            admissionService: authService,
            now: { clock.withLock { $0 } }
        )
        let waiting = challenge(id: "telegram", factor: .telegram, needsCode: false)
        await repository.enqueueBegin(.success(waiting))
        await repository.enqueueResend(.success(waiting))

        await controller.resolveIdentifier("mira")
        XCTAssertFalse(controller.canResend)
        XCTAssertEqual(controller.resendSecondsRemaining, 60)
        XCTAssertEqual(controller.challenge?.reference, waiting.reference)

        clock.withLock { $0 = $0.addingTimeInterval(60) }
        XCTAssertTrue(controller.canResend)
        await controller.resendCode()
        XCTAssertEqual(controller.challenge?.reference, waiting.reference)
        XCTAssertFalse(controller.canResend)
        XCTAssertEqual(controller.resendSecondsRemaining, 60)
        XCTAssertFalse(controller.isBusy)
    }

    @MainActor
    func testResendCooldownPreservesChallengeOnRateLimitAndFactorSwitchUsesOnlyAdvertisedFactors() async throws {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let authService = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let clock = Mutex(Date())
        let controller = SignInFlowController(signInService: authService, admissionService: authService, now: { clock.withLock { $0 } })
        let firstChallenge = challenge(id: "first", factor: .email, needsCode: true, availableFactors: [.email, .telegram])
        await repository.enqueueBegin(.success(firstChallenge))
        await repository.enqueueResend(.failure(.rateLimited))
        await repository.enqueueResend(.success(firstChallenge))
        await repository.enqueueBegin(.success(challenge(id: "second", factor: .telegram, needsCode: true, availableFactors: [.email, .telegram])))

        await controller.resolveIdentifier("mira")
        await controller.submitPassword("secret")
        XCTAssertFalse(controller.canResend)
        XCTAssertEqual(controller.resendSecondsRemaining, 60)
        await controller.selectFactor(.email)
        XCTAssertEqual(controller.challenge?.reference?.id, "first")
        XCTAssertEqual(controller.resendSecondsRemaining, 60)
        let beginCallsBeforeSameFactor = await repository.beginCalls()
        XCTAssertEqual(beginCallsBeforeSameFactor.count, 1)
        clock.withLock { $0 = $0.addingTimeInterval(60) }
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
        XCTAssertFalse(controller.canResend)
        XCTAssertEqual(controller.resendSecondsRemaining, 60)
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
    func testNilCommitFromOldGenerationCannotOverwriteNewPasswordStep() async {
        let repository = ScriptedSignInRepository(mode: .password)
        let tokens = TestTokenProvider()
        let auth = makeAuthService(repository: repository, tokens: tokens)
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.success(challenge(id: "old", state: .approved, factor: .unknown(0), needsCode: false)))
        await repository.enqueueCompletion(.success(completion("old-session")))
        await repository.enqueueBegin(.success(challenge(id: "new", state: .approved, factor: .unknown(0), needsCode: false)))
        await repository.enqueueCompletion(.success(completion("new-session")))

        await flow.resolveIdentifier("old")
        await tokens.holdNextSave()
        let oldLogin = Task { await flow.submitPassword("old-password") }
        await tokens.waitForSaveStart()

        flow.identifierChanged(to: "new")
        await flow.resolveIdentifier("new")
        XCTAssertEqual(flow.step, .passwordRequired)
        XCTAssertNil(flow.error)

        await tokens.releaseSave()
        await oldLogin.value
        XCTAssertEqual(flow.step, .passwordRequired)
        XCTAssertFalse(flow.isBusy)
        XCTAssertNil(flow.error)

        await flow.submitPassword("new-password")
        XCTAssertEqual(flow.step, .authenticated)
        let calls = await repository.beginCalls()
        XCTAssertEqual(calls.map(\.password), ["old-password", "new-password"])
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "new-session-access")
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
    func testRecoveryWithoutEnabledDeliveryAcceptsUnknownZeroAndFailedBeginCanRetry() async {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await repository.enqueueBegin(.failure(.rateLimited))
        await repository.enqueueBegin(.success(challenge(factor: .unknown(0), availableFactors: [])))
        await flow.resolveIdentifier("mira")
        await flow.submitPassword("secret")
        await flow.useRecoveryCode()
        XCTAssertFalse(flow.isUsingRecoveryCode)
        XCTAssertTrue(flow.canUseRecoveryCode)
        await flow.useRecoveryCode()
        XCTAssertEqual(flow.step, .challenge)
        XCTAssertTrue(flow.isUsingRecoveryCode)
        XCTAssertEqual(flow.challenge?.factor, .unknown(0))
        XCTAssertFalse(flow.canResend)
        flow.cancel()
    }

    @MainActor
    func testUnknownFactorIsRejectedOutsideRecoveryAndUnrecognizedFactorAlwaysRejected() async {
        for recovery in [false, true] {
            let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
            let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
            let flow = SignInFlowController(signInService: auth, admissionService: auth)
            if recovery { await repository.enqueueBegin(.failure(.rateLimited)) }
            await repository.enqueueBegin(.success(challenge(factor: recovery ? .unknown(99) : .unknown(0), availableFactors: [])))
            await flow.resolveIdentifier("mira")
            await flow.submitPassword("secret")
            if recovery { await flow.useRecoveryCode() }
            XCTAssertEqual(flow.error, .malformedResponse)
        }
    }

    @MainActor
    func testTelegramDeliveryCodeDoesNotPollOrDisableControls() async {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.success(challenge(factor: .telegram)))
        await flow.resolveIdentifier("mira")
        await flow.submitPassword("secret")
        flow.resumeForeground()
        XCTAssertFalse(flow.hasForegroundPollingTask)
        let reads = await repository.getCount()
        XCTAssertEqual(reads, 0)
        XCTAssertFalse(flow.isBusy)
        flow.cancel()
    }

    @MainActor
    func testTelegramBeginWhileBackgroundedWaitsUntilForegroundToPoll() async {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
        flow.pauseForeground()

        await flow.resolveIdentifier("mira")
        XCTAssertFalse(flow.hasForegroundPollingTask)
        let backgroundReads = await repository.getCount()
        XCTAssertEqual(backgroundReads, 0)

        await repository.holdNextGet()
        flow.resumeForeground()
        XCTAssertTrue(flow.hasForegroundPollingTask)
        await repository.waitForGetStart()
        flow.pauseForeground()
        await repository.releaseGet(.failure(.cancelled))
        flow.cancel()
    }

    @MainActor
    func testPollingApprovalNavigatesExactlyOnceThroughSharedCompletionBoundary() async {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        let navigation = SignInNavigationCompletion(admissionService: auth)
        let navigated = ResponseGate<Bool>()
        var loads = 0
        var transitions = 0
        var active = true
        flow.onAuthenticated = { receipt in
            await navigation.finish(receipt, loadCurrentUser: { loads += 1 }, isActive: { active }, navigate: {
                transitions += 1
                active = false
            })
            await navigated.release(.success(true))
        }
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
        await repository.enqueueCompletion(.success(completion("poll-approved")))
        await repository.holdNextGet()
        flow.resumeForeground()
        await flow.resolveIdentifier("mira")
        await repository.waitForGetStart()
        XCTAssertFalse(flow.isBusy)
        await repository.releaseGet(.success(challenge(state: .approved, factor: .telegram, needsCode: false)))
        _ = try? await navigated.response()
        XCTAssertEqual(transitions, 1)
        XCTAssertEqual(loads, 1)
        let receipt = flow.admissionReceipt!
        await navigation.finish(receipt, loadCurrentUser: { loads += 1 }, isActive: { active }, navigate: { transitions += 1 })
        XCTAssertEqual(transitions, 1)
        XCTAssertEqual(loads, 1)
        // Leaving login after navigation must preserve the accepted session.
        flow.cancel()
        await auth.invalidateSignInAttempt(receipt.attempt)
        let current = await auth.isCurrentSignInSession(receipt)
        XCTAssertTrue(current)
    }

    @MainActor
    func testPasswordLostCompletionHasActionableRetryOnSameReferenceAndNavigates() async {
        let repository = ScriptedSignInRepository(mode: .password)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        let navigation = SignInNavigationCompletion(admissionService: auth)
        var transitions = 0
        flow.onAuthenticated = { receipt in
            await navigation.finish(receipt, loadCurrentUser: {}, isActive: { transitions == 0 }, navigate: { transitions += 1 })
        }
        await repository.enqueueBegin(.success(challenge(state: .approved, factor: .unknown(0), needsCode: false)))
        await repository.enqueueCompletion(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await repository.enqueueGet(.success(challenge(state: .completed, factor: .unknown(0), needsCode: false)))
        await repository.enqueueCompletion(.success(completion("retry")))
        await flow.resolveIdentifier("mira")
        await flow.submitPassword("secret")
        XCTAssertTrue(flow.canRetryChallenge)
        XCTAssertEqual(flow.step, .challenge)
        await flow.retryChallenge()
        XCTAssertEqual(transitions, 1)
        let calls = await repository.completionCalls()
        XCTAssertEqual(calls.count, 2)
        XCTAssertTrue(calls.allSatisfy { $0.reference.id == "challenge-1" })
    }

    @MainActor
    func testCancelledInFlightPollIsSilentAndSuccessfulWaitingClearsTransientError() async {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
        await repository.holdNextGet()
        flow.resumeForeground()
        await flow.resolveIdentifier("mira")
        await repository.waitForGetStart()
        flow.pauseForeground()
        await repository.releaseGet(.failure(.cancelled))
        // Await a second explicit request; it must remain usable after cancelled polling.
        await repository.enqueueGet(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await flow.refreshChallenge()
        XCTAssertEqual(flow.error, .transport)
        await repository.enqueueGet(.success(challenge(factor: .telegram, needsCode: false)))
        await flow.refreshChallenge()
        XCTAssertNil(flow.error)
        XCTAssertEqual(flow.step, .challenge)
        XCTAssertEqual(flow.challenge?.reference?.id, "challenge-1")
        flow.cancel()
    }

    @MainActor
    func testBackgroundCancellationDuringTelegramCompletionResumesSameChallengeSilently() async {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        let reference = challenge(factor: .telegram, needsCode: false).reference!
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
        await repository.holdNextGet()
        await repository.holdNextCompletion()
        flow.resumeForeground()

        let resolving = Task { await flow.resolveIdentifier("mira") }
        await repository.waitForGetStart()
        await resolving.value
        await repository.releaseGet(.success(challenge(state: .approved, factor: .telegram, needsCode: false)))
        await repository.waitForCompletionStart()
        flow.pauseForeground()
        await repository.releaseCompletion(.failure(.cancelled))
        await waitUntil { flow.step == .challenge && !flow.isBusy }

        XCTAssertNil(flow.error)
        XCTAssertEqual(flow.challenge?.reference, reference)
        XCTAssertEqual(flow.challenge?.state, .approved)

        await repository.holdNextGet()
        await repository.holdNextCompletion()
        flow.resumeForeground()
        await repository.waitForGetStart()
        await repository.releaseGet(.success(challenge(state: .approved, factor: .telegram, needsCode: false)))
        await repository.waitForCompletionStart()
        let gets = await repository.challengeReadReferences()
        let completions = await repository.completionCalls()
        XCTAssertEqual(gets.suffix(2).map(\.id), [reference.id, reference.id])
        XCTAssertEqual(completions.suffix(2).map { $0.reference.id }, [reference.id, reference.id])
        await repository.releaseCompletion(.success(completion("resumed-telegram")))
        await waitUntil { flow.step == .authenticated }
        XCTAssertNil(flow.error)
    }

    @MainActor
    func testMissingOrForbiddenPollStopsAndOffersFreshBegin() async {
        for status in [5, 7, 16] {
            let repository = ScriptedSignInRepository(mode: .telegramLogin)
            let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
            let flow = SignInFlowController(signInService: auth, admissionService: auth)
            await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
            await repository.holdNextGet()
            flow.resumeForeground()
            await flow.resolveIdentifier("mira")
            await repository.waitForGetStart()
            flow.pauseForeground()
            await repository.releaseGet(.failure(.cancelled))
            await repository.enqueueGet(.failure(.transport(statusCode: status, serverErrorCode: nil)))
            await flow.refreshChallenge()
            XCTAssertEqual(flow.step, .telegramReady)
            XCTAssertEqual(flow.error, .cancelled)
            XCTAssertNil(flow.challenge)
            flow.resumeForeground()
            let reads = await repository.getCount()
            XCTAssertEqual(reads, 2)
            flow.cancel()
        }
    }

    @MainActor
    func testCancellationDuringRealTokenSaveRollsBackBeforeNewWinnerWrites() async {
        let tokens = TestTokenProvider()
        let auth = makeAuthService(repository: ScriptedSignInRepository(mode: .password), tokens: tokens)
        let old = await auth.beginSignInAttempt()
        await tokens.holdNextSave()
        let oldCommit = Task { await auth.commitSignInSession(self.completion("old").session!, for: old) }
        await tokens.waitForSaveStart()
        old.cancel()
        let cleanup = Task { await auth.invalidateSignInAttempt(old) }
        let next = await auth.beginSignInAttempt()
        let nextCommit = Task { await auth.commitSignInSession(self.completion("new").session!, for: next) }
        await tokens.releaseSave()
        let oldReceipt = await oldCommit.value
        let newReceipt = await nextCommit.value
        await cleanup.value
        XCTAssertNil(oldReceipt)
        XCTAssertNotNil(newReceipt)
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "new-access")
        await auth.invalidateSignInAttempt(old)
        let afterLateCleanup = await tokens.snapshot()
        XCTAssertEqual(afterLateCleanup.accessToken, "new-access")
    }

    @MainActor
    func testContendingAttemptRetriesAfterAbandonedWinnerAndCancelsAfterAcceptedWinner() async {
        let auth = makeAuthService(repository: ScriptedSignInRepository(mode: .password), tokens: TestTokenProvider())
        let firstWinner = await auth.beginSignInAttempt()
        let firstReceipt = await auth.commitSignInSession(completion("first-winner").session!, for: firstWinner)!
        let contender = await auth.beginSignInAttempt()
        XCTAssertFalse(contender.isCancelled)
        let deniedWhileProvisional = await auth.commitSignInSession(completion("contender").session!, for: contender)
        XCTAssertNil(deniedWhileProvisional)
        await auth.invalidateSignInAttempt(firstReceipt.attempt)

        let contenderReceipt = await auth.commitSignInSession(completion("contender").session!, for: contender)
        XCTAssertNotNil(contenderReceipt)
        let loser = await auth.beginSignInAttempt()
        let deniedBeforeAcceptance = await auth.commitSignInSession(completion("loser").session!, for: loser)
        XCTAssertNil(deniedBeforeAcceptance)
        XCTAssertFalse(loser.isCancelled)

        XCTAssertTrue(contenderReceipt!.attempt.accept())
        let deniedAfterAcceptance = await auth.commitSignInSession(completion("loser").session!, for: loser)
        XCTAssertNil(deniedAfterAcceptance)
        XCTAssertTrue(loser.isCancelled)
    }

    @MainActor
    func testRefreshFailureClearsOnlyItsOwnTokensAndLeavesAdmissionUsable() async {
        let tokens = TestTokenProvider()
        await tokens.saveTokens(
            accessToken: "old-access",
            accessExpiresAt: Date().addingTimeInterval(-1),
            refreshToken: "old-refresh",
            refreshExpiresAt: Date().addingTimeInterval(3_600)
        )
        let auth = AuthService(
            identityRepository: UnusedIdentityRepository(createTokenHandler: { _ in throw BFNetworkingError.serverUnavailable }),
            tokenProvider: tokens,
            connectionManager: ConnectionManager()
        )

        let restored = await auth.tryRestoreSession()
        XCTAssertFalse(restored)
        let cleared = await tokens.snapshot()
        XCTAssertNil(cleared.accessToken)
        XCTAssertNil(cleared.refreshToken)
        let retry = await auth.beginSignInAttempt()
        let receipt = await auth.commitSignInSession(completion("retry-after-refresh").session!, for: retry)
        XCTAssertNotNil(receipt)
    }

    @MainActor
    func testRefreshFailureCannotClearNewerCommittedSession() async {
        let tokens = TestTokenProvider()
        await tokens.saveTokens(
            accessToken: "old-access",
            accessExpiresAt: Date().addingTimeInterval(-1),
            refreshToken: "old-refresh",
            refreshExpiresAt: Date().addingTimeInterval(3_600)
        )
        let refreshGate = RefreshTokenGate()
        await refreshGate.holdNextRefresh()
        let auth = AuthService(
            identityRepository: UnusedIdentityRepository(createTokenHandler: { _ in try await refreshGate.createToken() }),
            tokenProvider: tokens,
            connectionManager: ConnectionManager()
        )
        let restoring = Task { await auth.tryRestoreSession() }
        await refreshGate.waitForRefreshStart()

        let newerAttempt = await auth.beginSignInAttempt()
        let newerReceipt = await auth.commitSignInSession(completion("newer-session").session!, for: newerAttempt)!
        await refreshGate.releaseFailure()

        let restored = await restoring.value
        XCTAssertTrue(restored)
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "newer-session-access")
        XCTAssertEqual(saved.refreshToken, "newer-session-refresh")
        let isCurrent = await auth.isCurrentSignInSession(newerReceipt)
        XCTAssertTrue(isCurrent)
    }

    @MainActor
    func testCancellationDuringUserLoadRollsBackAndAdmitsRetry() async {
        let tokens = TestTokenProvider()
        let auth = makeAuthService(repository: ScriptedSignInRepository(mode: .password), tokens: tokens)
        let attempt = await auth.beginSignInAttempt()
        let receipt = await auth.commitSignInSession(completion("abandoned").session!, for: attempt)!
        let navigation = SignInNavigationCompletion(admissionService: auth)
        let load = ResponseGate<Bool>()
        var transitions = 0
        let finishing = Task {
            await navigation.finish(receipt, loadCurrentUser: { _ = try? await load.response() }, isActive: { true }, navigate: { transitions += 1 })
        }
        await load.waitForStart()
        attempt.cancel()
        await auth.invalidateSignInAttempt(attempt)
        let next = await auth.beginSignInAttempt()
        let nextReceipt = await auth.commitSignInSession(completion("retry").session!, for: next)
        await load.release(.success(true))
        await finishing.value
        XCTAssertEqual(transitions, 0)
        XCTAssertNotNil(nextReceipt)
        let saved = await tokens.snapshot()
        XCTAssertEqual(saved.accessToken, "retry-access")
    }

    @MainActor
    func testRecoveryReplacementIsBusyBeforeCancelReturnsAndCannotDoubleBegin() async {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.success(challenge()))
        await repository.enqueueBegin(.success(challenge(id: "recovery", factor: .unknown(0), availableFactors: [])))
        await flow.resolveIdentifier("mira")
        await flow.submitPassword("secret")
        await repository.holdNextCancel()
        let replacing = Task { await flow.useRecoveryCode() }
        await repository.waitForCancelStart()
        XCTAssertTrue(flow.isBusy)
        XCTAssertEqual(flow.step, .beginning)
        await flow.useRecoveryCode()
        let before = await repository.beginCalls()
        XCTAssertEqual(before.count, 1)
        await repository.releaseCancel()
        await replacing.value
        let after = await repository.beginCalls()
        XCTAssertEqual(after.count, 2)
        XCTAssertEqual(flow.challenge?.reference?.id, "recovery")
        flow.cancel()
    }

    @MainActor
    func testForegroundResumePollsSameReferenceAndWaitingClearsErrorWithoutBusy() async {
        let repository = ScriptedSignInRepository(mode: .telegramLogin)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let pollClock = ResponseGate<Bool>()
        let flow = SignInFlowController(signInService: auth, admissionService: auth, sleepBetweenPolls: {
            _ = try await pollClock.response()
            throw CancellationError()
        })
        await repository.enqueueBegin(.success(challenge(factor: .telegram, needsCode: false)))
        await repository.holdNextGet()
        flow.resumeForeground()
        await flow.resolveIdentifier("mira")
        await repository.waitForGetStart()
        flow.pauseForeground()
        await repository.releaseGet(.failure(.cancelled))
        await repository.enqueueGet(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await flow.refreshChallenge()
        XCTAssertEqual(flow.error, .transport)
        await repository.enqueueGet(.success(challenge(factor: .telegram, needsCode: false)))
        flow.resumeForeground()
        await pollClock.waitForStart()
        XCTAssertNil(flow.error)
        XCTAssertFalse(flow.isBusy)
        XCTAssertEqual(flow.challenge?.reference?.id, "challenge-1")
        let reads = await repository.getCount()
        XCTAssertEqual(reads, 3)
        flow.pauseForeground()
        await pollClock.release(.success(true))
        flow.cancel()
    }

    @MainActor
    private func waitUntil(
        file: StaticString = #filePath,
        line: UInt = #line,
        _ condition: () -> Bool
    ) async {
        for _ in 0..<10_000 {
            if condition() { return }
            await Task.yield()
        }
        XCTFail("Timed out waiting for observable sign-in state", file: file, line: line)
    }

    @MainActor
    func testLateRefreshFailureCannotInterruptUserCompletion() async {
        let repository = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let auth = makeAuthService(repository: repository, tokens: TestTokenProvider())
        let flow = SignInFlowController(signInService: auth, admissionService: auth)
        await repository.enqueueBegin(.success(challenge()))
        await flow.resolveIdentifier("mira")
        await flow.submitPassword("secret")
        await repository.holdNextGet()
        let refreshing = Task { await flow.refreshChallenge() }
        await repository.waitForGetStart()
        XCTAssertFalse(flow.isBusy)
        await repository.holdNextCompletion()
        let completing = Task { await flow.submitCode("123456") }
        await repository.waitForCompletionStart()
        await repository.releaseGet(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await refreshing.value
        XCTAssertTrue(flow.isBusy)
        XCTAssertEqual(flow.step, .completing)
        XCTAssertNil(flow.error)
        await repository.releaseCompletion(.success(completion("foreground-completion")))
        await completing.value
        XCTAssertEqual(flow.step, .authenticated)
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
    let reference: SignInChallengeReference
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
    private var readReferences: [SignInChallengeReference] = []
    private var lastChallenge: SignInChallenge?
    private var reads = 0
    private var getGate = ResponseGate<SignInChallenge>()
    private let cancelGate = ResponseGate<Bool>()
    private var holdCancel = false
    private var holdGet = false
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
    func challengeReadReferences() -> [SignInChallengeReference] { readReferences }

    func holdNextGet() { holdGet = true; getGate = ResponseGate<SignInChallenge>() }
    func holdNextCancel() { holdCancel = true }
    func waitForCancelStart() async { await cancelGate.waitForStart() }
    func releaseCancel() async { await cancelGate.release(.success(true)) }
    func waitForGetStart() async { await getGate.waitForStart() }
    func releaseGet(_ result: Result<SignInChallenge, SignInRepositoryError>) async { await getGate.release(result) }
    func getCount() -> Int { reads }

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
        if !begins.isEmpty {
            let response = try begins.removeFirst().get()
            lastChallenge = response
            return response
        }
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
        reads += 1
        readReferences.append(reference)
        if holdGet {
            holdGet = false
            return try await getGate.response()
        }
        if !challengeReads.isEmpty { return try challengeReads.removeFirst().get() }
        if let lastChallenge { return lastChallenge }
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
        completionsCalled.append(CompletionCall(reference: reference, code: code, useRecoveryCode: useRecoveryCode))
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

    func cancelAuthChallenge(_ reference: SignInChallengeReference) async {
        cancellations.append(reference)
        if holdCancel {
            holdCancel = false
            _ = try? await cancelGate.response()
        }
    }

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
    private let saveGate = ResponseGate<Bool>()
    private var blockSave = false
    func holdNextSave() { blockSave = true }
    func waitForSaveStart() async { await saveGate.waitForStart() }
    func releaseSave() async { await saveGate.release(.success(true)) }

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
        if blockSave {
            blockSave = false
            _ = try? await saveGate.response()
        }
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
    var createTokenHandler: (@Sendable (String) async throws -> TokenInfo)? = nil
    func auth(login: String, isEmail: Bool, password: String, otpCode: String?) async throws -> AuthTokens { throw unused() }
    func createToken(refreshToken: String) async throws -> TokenInfo {
        if let createTokenHandler { return try await createTokenHandler(refreshToken) }
        throw unused()
    }
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

private actor RefreshTokenGate {
    private var holdNext = false
    private var started = false
    private var continuation: CheckedContinuation<TokenInfo, any Error>?
    private var startWaiters: [CheckedContinuation<Void, Never>] = []

    func holdNextRefresh() { holdNext = true; started = false }

    func createToken() async throws -> TokenInfo {
        guard holdNext else { throw BFNetworkingError.serverUnavailable }
        holdNext = false
        return try await withCheckedThrowingContinuation { continuation in
            self.continuation = continuation
            started = true
            startWaiters.forEach { $0.resume() }
            startWaiters.removeAll()
        }
    }

    func waitForRefreshStart() async {
        if started { return }
        await withCheckedContinuation { startWaiters.append($0) }
    }

    func releaseFailure() {
        continuation?.resume(throwing: BFNetworkingError.serverUnavailable)
        continuation = nil
    }
}

/// Test-controlled async boundary; responses can be supplied before or after the waiter arrives.
private actor ResponseGate<Value: Sendable> {
    private var result: Result<Value, SignInRepositoryError>?
    private var continuation: CheckedContinuation<Value, any Error>?
    private var started = false
    private var startWaiters: [CheckedContinuation<Void, Never>] = []

    func response() async throws -> Value {
        started = true
        startWaiters.forEach { $0.resume() }
        startWaiters.removeAll()
        if let result { self.result = nil; return try result.get() }
        return try await withCheckedThrowingContinuation { continuation = $0 }
    }

    func waitForStart() async {
        if started { return }
        await withCheckedContinuation { startWaiters.append($0) }
    }

    func release(_ result: Result<Value, SignInRepositoryError>) {
        if let continuation {
            self.continuation = nil
            continuation.resume(with: result.mapError { $0 as any Error })
        } else { self.result = result }
    }
}

// Test-only navigation state. The production LoginViewModel is compiled from its source symlink.
@MainActor @Observable final class AppCoordinator {
    enum State { case authentication, main, serverSelection }
    enum AuthScreen { case login, register }
    var currentState: State = .authentication { didSet { if currentState == .main { onMain?() } } }
    var authScreen: AuthScreen = .login
    var isConnectionReady = false
    var onMain: (() -> Void)?
}

final class MacLoginViewModelTests: XCTestCase {
    @MainActor
    func testRecoveryFromFreshPasswordStepForwardsExactPasswordToBegin() async {
        let repo = ScriptedSignInRepository(mode: .passwordSecondFactor)
        let auth = makeAuth(repo)
        let coordinator = AppCoordinator()
        let vm = LoginViewModel(authService: auth, coordinator: coordinator, loadCurrentUser: {})
        await repo.enqueueBegin(.success(challenge(
            .waiting,
            factor: .unknown(0),
            needsCode: true
        )))
        vm.usernameOrEmail = "mira"
        await vm.login()
        XCTAssertEqual(vm.flow.step, .passwordRequired)

        vm.password = "  exact password  "
        await vm.useRecoveryCode()

        let calls = await repo.beginCalls()
        XCTAssertEqual(calls.count, 1)
        XCTAssertEqual(calls[0].password, "  exact password  ")
        XCTAssertTrue(calls[0].useRecoveryCode)
        XCTAssertEqual(vm.flow.step, .challenge)
        XCTAssertTrue(vm.flow.isUsingRecoveryCode)
    }

    @MainActor
    func testPollingActuallyNavigatesLoginViewModelToMain() async {
        let repo = ScriptedSignInRepository(mode: .telegramLogin)
        let auth = makeAuth(repo)
        let coordinator = AppCoordinator()
        let navigated = ResponseGate<Bool>()
        var transitions = 0
        var loads = 0
        coordinator.onMain = { transitions += 1; Task { await navigated.release(.success(true)) } }
        let vm = LoginViewModel(authService: auth, coordinator: coordinator, loadCurrentUser: { loads += 1 })
        await repo.enqueueBegin(.success(challenge(.waiting, factor: .telegram)))
        await repo.holdNextGet()
        await repo.enqueueCompletion(.success(completion()))
        vm.resumeForeground()
        vm.usernameOrEmail = "mira"
        await vm.login()
        await repo.waitForGetStart()
        await repo.releaseGet(.success(challenge(.approved, factor: .telegram)))
        _ = try? await navigated.response()
        XCTAssertEqual(coordinator.currentState, .main)
        XCTAssertTrue(coordinator.isConnectionReady)
        XCTAssertEqual(transitions, 1)
        XCTAssertEqual(loads, 1)
        let receipt = vm.flow.admissionReceipt!
        vm.leave()
        let valid = await auth.isCurrentSignInSession(receipt)
        XCTAssertTrue(valid)
    }

    @MainActor
    func testPasswordLostCompleteRetriesThroughActualLoginAction() async {
        let repo = ScriptedSignInRepository(mode: .password)
        let auth = makeAuth(repo)
        let coordinator = AppCoordinator()
        let vm = LoginViewModel(authService: auth, coordinator: coordinator, loadCurrentUser: {})
        await repo.enqueueBegin(.success(challenge(.approved, factor: .unknown(0))))
        await repo.enqueueCompletion(.failure(.transport(statusCode: 14, serverErrorCode: nil)))
        await repo.enqueueGet(.success(challenge(.completed, factor: .unknown(0))))
        await repo.enqueueCompletion(.success(completion()))
        vm.usernameOrEmail = "mira"
        await vm.login()
        vm.password = "secret"
        await vm.login()
        XCTAssertTrue(vm.flow.canRetryChallenge)
        XCTAssertEqual(coordinator.currentState, .authentication)
        await vm.login()
        XCTAssertEqual(coordinator.currentState, .main)
        XCTAssertTrue(coordinator.isConnectionReady)
        let calls = await repo.completionCalls()
        XCTAssertEqual(calls.count, 2)
        XCTAssertEqual(calls[0].reference, calls[1].reference)
    }

    @MainActor
    func testLeavingActualLoginDuringLoadRollsBackAndDoesNotNavigate() async {
        let repo = ScriptedSignInRepository(mode: .password)
        let tokens = TestTokenProvider()
        let auth = makeAuth(repo, tokens: tokens)
        let coordinator = AppCoordinator()
        let gate = ResponseGate<Bool>()
        let vm = LoginViewModel(authService: auth, coordinator: coordinator, loadCurrentUser: { _ = try? await gate.response() })
        await repo.enqueueBegin(.success(challenge(.approved, factor: .unknown(0))))
        await repo.enqueueCompletion(.success(completion()))
        vm.usernameOrEmail = "mira"
        await vm.login()
        vm.password = "secret"
        let login = Task { await vm.login() }
        await gate.waitForStart()
        vm.leave()
        await gate.release(.success(true))
        await login.value
        XCTAssertEqual(coordinator.currentState, .authentication)
        let saved = await tokens.snapshot()
        XCTAssertNil(saved.accessToken)
        let retry = await auth.beginSignInAttempt()
        let receipt = await auth.commitSignInSession(completion().session!, for: retry)
        XCTAssertNotNil(receipt)
    }

    @MainActor private func makeAuth(_ repo: ScriptedSignInRepository, tokens: TestTokenProvider = TestTokenProvider()) -> AuthService {
        AuthService(identityRepository: UnusedIdentityRepository(), tokenProvider: tokens, connectionManager: ConnectionManager(), signInRepository: repo)
    }
    private func challenge(
        _ state: SignInChallengeState,
        factor: SignInFactor,
        needsCode: Bool = false
    ) -> SignInChallenge {
        SignInChallenge(
            reference: SignInChallengeReference(id: "challenge", secret: "secret", endpoint: ServiceEndpoint(host: "identity.test", port: 5001)),
            state: state,
            factor: factor,
            expiresAt: Date().addingTimeInterval(3600),
            availableFactors: [],
            needsCode: needsCode
        )
    }
    private func completion() -> SignInCompletion {
        SignInCompletion(state: .completed, session: SignInSessionTokens(accessToken: "access", accessExpiresAt: Date().addingTimeInterval(3600), refreshToken: "refresh", refreshExpiresAt: Date().addingTimeInterval(7200)))
    }
}
