//
//  TokenRefreshCoordinator.swift
//  BFNetworking
//
//  Координация обновления access token (serialization для параллельных запросов)
//

import Foundation

/// Координирует обновление access token.
/// Гарантирует, что при одновременных запросах обновление происходит ОДИН РАЗ.
public actor TokenRefreshCoordinator {

    private let tokenProvider: TokenProvider
    private let identityRepository: IdentityRepositoryProtocol

    /// Текущая задача обновления (nil если не обновляется)
    private var refreshTask: Task<String, Error>?

    public init(tokenProvider: TokenProvider, identityRepository: IdentityRepositoryProtocol) {
        self.tokenProvider = tokenProvider
        self.identityRepository = identityRepository
    }

    /// Обновить access token. Если уже идёт обновление — ждёт его результата.
    /// Возвращает новый access token.
    /// Бросает ошибку, если refresh token невалиден (сессия истекла).
    public func refreshAccessToken() async throws -> String {
        // Если уже есть активная задача — ждём её
        if let existingTask = refreshTask {
            print("⏳ [TokenRefreshCoordinator] Waiting for existing refresh task...")
            return try await existingTask.value
        }

        print("🔄 [TokenRefreshCoordinator] Starting token refresh...")

        // Создаём новую задачу обновления
        let tokenProvider = self.tokenProvider
        let identityRepository = self.identityRepository

        let task = Task<String, Error> {
            guard let refreshToken = await tokenProvider.currentRefreshToken else {
                print("❌ [TokenRefreshCoordinator] No refresh token available")
                throw BFNetworkingError.sessionExpired
            }

            // Вызываем Identity.CreateToken
            print("📡 [TokenRefreshCoordinator] Calling createToken API...")
            let response = try await identityRepository.createToken(refreshToken: refreshToken)

            // Сохраняем новый access token
            await tokenProvider.saveAccessToken(
                value: response.value,
                expiresAt: response.expirationDate
            )

            print("✅ [TokenRefreshCoordinator] Token refreshed, expires at: \(response.expirationDate)")
            return response.value
        }

        refreshTask = task

        do {
            let token = try await task.value
            refreshTask = nil
            return token
        } catch {
            refreshTask = nil
            print("❌ [TokenRefreshCoordinator] Token refresh failed: \(error.localizedDescription)")
            throw error
        }
    }
}
