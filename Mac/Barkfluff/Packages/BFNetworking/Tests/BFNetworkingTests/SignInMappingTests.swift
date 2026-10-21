import XCTest
@testable import BFNetworking
import BFProto
import GRPCCore
import SwiftProtobuf

final class SignInMappingTests: XCTestCase {
    func testRPCErrorMapperPreservesIdentityTrailerAndClassifiesCredentialsAndAbuse() {
        XCTAssertEqual(
            SignInRPCErrorMapper.map(rpcError(.failedPrecondition, code: SignInRPCErrorMapper.invalidCredentialsCode)),
            .invalidCredentials
        )
        XCTAssertEqual(
            SignInRPCErrorMapper.map(rpcError(.resourceExhausted, code: SignInRPCErrorMapper.lockoutCode)),
            .lockedOut
        )
        XCTAssertEqual(
            SignInRPCErrorMapper.map(rpcError(.resourceExhausted, code: SignInRPCErrorMapper.rateLimitCode)),
            .rateLimited
        )
        XCTAssertEqual(
            SignInRPCErrorMapper.map(rpcError(.unimplemented)),
            .serverUpdateRequired
        )
        XCTAssertEqual(
            SignInRPCErrorMapper.map(rpcError(.permissionDenied, code: "identity-policy-code")),
            .transport(statusCode: 7, serverErrorCode: "identity-policy-code")
        )
    }

    func testEndpointChangesAndTaskCancellationMapToCancellation() async {
        XCTAssertEqual(SignInRepository.mapError(ConnectionError.endpointChanged), .cancelled)
        XCTAssertEqual(SignInRepository.mapError(CancellationError()), .cancelled)
        XCTAssertEqual(SignInRepository.mapError(SignInRepositoryError.malformedResponse), .malformedResponse)
        let task = Task {
            withUnsafeCurrentTask { $0?.cancel() }
            return SignInRepository.mapError(SignInRepositoryError.malformedResponse)
        }
        let result = await task.value
        XCTAssertEqual(result, .cancelled)
    }

    func testMissingChallengeStatusRemainsAvailableForTerminalPollClassification() {
        XCTAssertEqual(SignInRPCErrorMapper.map(rpcError(.notFound, code: "removed")),
                       .transport(statusCode: 5, serverErrorCode: "removed"))
        XCTAssertEqual(SignInRPCErrorMapper.map(rpcError(.cancelled)), .cancelled)
    }

    func testKnownAndUnknownEnumsMapWithoutGuessing() {
        XCTAssertEqual(
            SignInProtoMapper.loginMode(Barkfluff_Identity_AuthLoginMode.password),
            SignInLoginMode.password
        )
        XCTAssertEqual(
            SignInProtoMapper.loginMode(Barkfluff_Identity_AuthLoginMode.passwordSecondFactor),
            SignInLoginMode.passwordSecondFactor
        )
        XCTAssertEqual(
            SignInProtoMapper.loginMode(Barkfluff_Identity_AuthLoginMode.UNRECOGNIZED(44)),
            SignInLoginMode.unknown(44)
        )
        XCTAssertEqual(SignInProtoMapper.factor(Barkfluff_Identity_OtpTypeId.telegram), SignInFactor.telegram)
        XCTAssertEqual(SignInProtoMapper.factor(Barkfluff_Identity_OtpTypeId.UNRECOGNIZED(23)), SignInFactor.unknown(23))
        XCTAssertEqual(SignInProtoMapper.challengeState(Barkfluff_Identity_AuthChallengeState.completed), .completed)
        XCTAssertEqual(SignInProtoMapper.challengeState(Barkfluff_Identity_AuthChallengeState.UNRECOGNIZED(99)), .unknown(99))
    }

    func testChallengeMappingKeepsServerReferenceFactorsExpiryAndLink() throws {
        let endpoint = ServiceEndpoint(host: "identity.example.test", port: 5001, useTLS: true)
        var response = Barkfluff_Identity_AuthChallengeResponse()
        response.challenge.id = "challenge-id"
        response.challenge.secret = "challenge-secret"
        response.state = .waiting
        response.factor = .email
        response.telegramURL = "https://t.me/barkfluff_bot?start=abc"
        response.expiresAt = timestamp(Date(timeIntervalSince1970: 2_000_000_000))
        response.availableFactors = [.email, .telegram, .authenticator]
        response.needsCode = true

        let result = try SignInProtoMapper.challenge(response, endpoint: endpoint)

        XCTAssertEqual(result.reference, SignInChallengeReference(id: "challenge-id", secret: "challenge-secret", endpoint: endpoint))
        XCTAssertEqual(result.state, .waiting)
        XCTAssertEqual(result.factor, .email)
        XCTAssertEqual(result.availableFactors, [.email, .telegram, .authenticator])
        XCTAssertEqual(result.telegramURL, "https://t.me/barkfluff_bot?start=abc")
        XCTAssertEqual(result.expiresAt, Date(timeIntervalSince1970: 2_000_000_000))
        XCTAssertTrue(result.needsCode)
    }

    func testChallengeMappingRejectsMalformedReferenceAndTimestamp() {
        let endpoint = ServiceEndpoint(host: "identity.example.test", port: 5001)
        var malformedReference = Barkfluff_Identity_AuthChallengeResponse()
        malformedReference.challenge.id = "id-without-secret"
        malformedReference.state = .waiting
        malformedReference.expiresAt = timestamp(Date(timeIntervalSince1970: 2_000_000_000))
        XCTAssertThrowsError(try SignInProtoMapper.challenge(malformedReference, endpoint: endpoint)) {
            XCTAssertEqual(SignInRepository.mapError($0), .malformedResponse)
        }

        var malformedTimestamp = Barkfluff_Identity_AuthChallengeResponse()
        malformedTimestamp.expiresAt.seconds = 2_000_000_000
        malformedTimestamp.expiresAt.nanos = 1_000_000_000
        malformedTimestamp.state = .expired
        XCTAssertThrowsError(try SignInProtoMapper.challenge(malformedTimestamp, endpoint: endpoint))
    }

    func testSessionMappingRequiresBothTokensAndValidFutureTimestamps() throws {
        let future = Date().addingTimeInterval(3_600)
        let valid = authResponse(access: "access", refresh: "refresh", accessExpiry: future, refreshExpiry: future)
        let mapped = try SignInProtoMapper.session(valid)
        XCTAssertEqual(mapped.accessToken, "access")
        XCTAssertEqual(mapped.refreshToken, "refresh")

        XCTAssertThrowsError(try SignInProtoMapper.session(Barkfluff_Identity_AuthResponse()))
        XCTAssertThrowsError(try SignInProtoMapper.session(authResponse(access: "", refresh: "refresh", accessExpiry: future, refreshExpiry: future)))

        var missingExpiry = Barkfluff_Identity_AuthResponse()
        missingExpiry.accessToken.value = "access"
        missingExpiry.refreshToken.value = "refresh"
        missingExpiry.accessToken.expirationDate = timestamp(future)
        XCTAssertThrowsError(try SignInProtoMapper.session(missingExpiry))

        let expired = authResponse(
            access: "access", refresh: "refresh",
            accessExpiry: Date(timeIntervalSince1970: 1),
            refreshExpiry: future
        )
        XCTAssertThrowsError(try SignInProtoMapper.session(expired))
    }

    func testCompletionWithoutSessionAndUnrecognizedStateDoNotInventSession() throws {
        var response = Barkfluff_Identity_CompleteAuthChallengeResponse()
        response.state = .completed
        let mapped = try SignInProtoMapper.completion(response)
        XCTAssertEqual(mapped.state, .completed)
        XCTAssertNil(mapped.session)

        response.state = .UNRECOGNIZED(73)
        XCTAssertEqual(try SignInProtoMapper.completion(response).state, .unknown(73))
    }

    private func rpcError(_ code: RPCError.Code, code errorCode: String? = nil) -> RPCError {
        var metadata = Metadata()
        if let errorCode { metadata.addString(errorCode, forKey: "x-error-code") }
        return RPCError(code: code, message: "private backend detail", metadata: metadata)
    }

    private func timestamp(_ date: Date) -> Google_Protobuf_Timestamp {
        let seconds = date.timeIntervalSince1970
        var value = Google_Protobuf_Timestamp()
        value.seconds = Int64(seconds.rounded(.down))
        value.nanos = Int32((seconds - Double(value.seconds)) * 1_000_000_000)
        return value
    }

    private func authResponse(
        access: String,
        refresh: String,
        accessExpiry: Date,
        refreshExpiry: Date
    ) -> Barkfluff_Identity_AuthResponse {
        var response = Barkfluff_Identity_AuthResponse()
        response.accessToken.value = access
        response.accessToken.expirationDate = timestamp(accessExpiry)
        response.refreshToken.value = refresh
        response.refreshToken.expirationDate = timestamp(refreshExpiry)
        return response
    }
}
