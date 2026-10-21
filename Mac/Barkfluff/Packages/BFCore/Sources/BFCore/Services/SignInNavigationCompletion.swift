import Foundation

/// Completes provisional admission after user loading; UI navigation is synchronous with acceptance.
@MainActor
public final class SignInNavigationCompletion {
    private let admissionService: SignInSessionAdmissionService
    private var finishing: Set<SignInSessionAdmissionReceipt> = []

    public init(admissionService: SignInSessionAdmissionService) {
        self.admissionService = admissionService
    }

    public func finish(
        _ receipt: SignInSessionAdmissionReceipt,
        loadCurrentUser: @MainActor () async -> Void,
        isActive: @MainActor () -> Bool,
        navigate: @MainActor () -> Void
    ) async {
        guard finishing.insert(receipt).inserted else { return }
        defer { finishing.remove(receipt) }
        guard isActive(), await admissionService.isCurrentSignInSession(receipt) else {
            await admissionService.invalidateSignInAttempt(receipt.attempt)
            return
        }
        await loadCurrentUser()
        let current = await admissionService.isCurrentSignInSession(receipt)
        guard current, isActive(), receipt.attempt.accept() else {
            await admissionService.invalidateSignInAttempt(receipt.attempt)
            return
        }
        navigate()
    }
}
