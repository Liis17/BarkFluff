import Foundation

public enum SignInLoginMode: Sendable, Equatable {
    case password
    case telegramLogin
    case passwordSecondFactor
    case unknown(Int)
}

public enum SignInFactor: Sendable, Equatable, Hashable {
    case email
    case telegram
    case authenticator
    case unknown(Int)
}

public enum SignInChallengeState: Sendable, Equatable {
    case waiting
    case approved
    case rejected
    case expired
    case completed
    case cancelled
    case unknown(Int)
}

/// The server reference is bound to the Identity endpoint that created it.
public struct SignInChallengeReference: Sendable, Equatable {
    public let id: String
    public let secret: String
    public let endpoint: ServiceEndpoint

    public init(id: String, secret: String, endpoint: ServiceEndpoint) {
        self.id = id
        self.secret = secret
        self.endpoint = endpoint
    }
}

public struct SignInResolution: Sendable, Equatable {
    public let mode: SignInLoginMode
    public let errorCode: String?

    public init(mode: SignInLoginMode, errorCode: String? = nil) {
        self.mode = mode
        self.errorCode = errorCode
    }
}

public struct SignInChallenge: Sendable, Equatable {
    public let reference: SignInChallengeReference?
    public let state: SignInChallengeState
    public let factor: SignInFactor?
    public let telegramURL: String?
    public let expiresAt: Date?
    public let availableFactors: [SignInFactor]
    public let needsCode: Bool
    public let errorCode: String?

    public init(
        reference: SignInChallengeReference?,
        state: SignInChallengeState,
        factor: SignInFactor?,
        telegramURL: String? = nil,
        expiresAt: Date?,
        availableFactors: [SignInFactor] = [],
        needsCode: Bool,
        errorCode: String? = nil
    ) {
        self.reference = reference
        self.state = state
        self.factor = factor
        self.telegramURL = telegramURL
        self.expiresAt = expiresAt
        self.availableFactors = availableFactors
        self.needsCode = needsCode
        self.errorCode = errorCode
    }
}

public struct SignInSessionTokens: Sendable, Equatable {
    public let accessToken: String
    public let accessExpiresAt: Date
    public let refreshToken: String
    public let refreshExpiresAt: Date

    public init(accessToken: String, accessExpiresAt: Date, refreshToken: String, refreshExpiresAt: Date) {
        self.accessToken = accessToken
        self.accessExpiresAt = accessExpiresAt
        self.refreshToken = refreshToken
        self.refreshExpiresAt = refreshExpiresAt
    }
}

public struct SignInCompletion: Sendable, Equatable {
    public let state: SignInChallengeState
    public let session: SignInSessionTokens?
    public let errorCode: String?

    public init(state: SignInChallengeState, session: SignInSessionTokens?, errorCode: String? = nil) {
        self.state = state
        self.session = session
        self.errorCode = errorCode
    }
}

public enum SignInRepositoryError: Error, Sendable, Equatable {
    case invalidCredentials
    case lockedOut
    case rateLimited
    case serverUpdateRequired
    case cancelled
    case transport(statusCode: Int, serverErrorCode: String?)
    case malformedResponse
}
