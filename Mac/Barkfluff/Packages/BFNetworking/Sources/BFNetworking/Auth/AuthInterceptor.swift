//
//  AuthInterceptor.swift
//  BFNetworking
//
//  gRPC ClientInterceptor для авторизованных запросов
//

import Foundation
import GRPCCore

/// gRPC ClientInterceptor. Добавляет JWT access token в авторизованные запросы.
/// Проактивно обновляет токен если он скоро истечёт.
public struct AuthInterceptor: ClientInterceptor {

    private let tokenProvider: TokenProvider
    private let refreshCoordinator: TokenRefreshCoordinator
    private let onSessionExpired: @Sendable () -> Void

    /// Минимальный остаток жизни токена для проактивного обновления (секунды)
    private let proactiveRefreshThreshold: TimeInterval = 60

    public init(
        tokenProvider: TokenProvider,
        refreshCoordinator: TokenRefreshCoordinator,
        onSessionExpired: @escaping @Sendable () -> Void
    ) {
        self.tokenProvider = tokenProvider
        self.refreshCoordinator = refreshCoordinator
        self.onSessionExpired = onSessionExpired
    }

    public func intercept<Input: Sendable, Output: Sendable>(
        request: StreamingClientRequest<Input>,
        context: ClientContext,
        next: (
            _ request: StreamingClientRequest<Input>,
            _ context: ClientContext
        ) async throws -> StreamingClientResponse<Output>
    ) async throws -> StreamingClientResponse<Output> {

        // 1. Проактивное обновление, если токен скоро истечёт
        var accessToken = await tokenProvider.currentAccessToken

        let expiringSoon = await isTokenExpiringSoon()
        if accessToken == nil || expiringSoon {
            do {
                accessToken = try await refreshCoordinator.refreshAccessToken()
            } catch {
                // Если обновление не удалось — пробуем с текущим (или без)
            }
        }

        // 2. Добавляем auth token
        var metadata = request.metadata
        if let token = accessToken {
            metadata.addString(token, forKey: "x-auth-token")
        }

        let modifiedRequest = StreamingClientRequest<Input>(
            metadata: metadata,
            producer: request.producer
        )

        // 3. Отправляем запрос
        return try await next(modifiedRequest, context)
    }

    private func isTokenExpiringSoon() async -> Bool {
        guard let expiresAt = await tokenProvider.accessTokenExpirationDate else {
            return true
        }
        return expiresAt.timeIntervalSinceNow < proactiveRefreshThreshold
    }
}
