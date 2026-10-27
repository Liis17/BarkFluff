import Foundation

public protocol SignInRepositoryProtocol: Sendable {
    func resolveSignIn(login: String) async throws -> SignInResolution
    func beginSignIn(
        login: String,
        password: String,
        mode: SignInLoginMode,
        factor: SignInFactor?,
        useRecoveryCode: Bool,
        reportLoginModeMismatch: Bool
    ) async throws -> SignInChallenge
    func getAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge
    func completeAuthChallenge(
        _ reference: SignInChallengeReference,
        code: String,
        useRecoveryCode: Bool
    ) async throws -> SignInCompletion
    func cancelAuthChallenge(_ reference: SignInChallengeReference) async
    func resendAuthChallenge(_ reference: SignInChallengeReference) async throws -> SignInChallenge
}
