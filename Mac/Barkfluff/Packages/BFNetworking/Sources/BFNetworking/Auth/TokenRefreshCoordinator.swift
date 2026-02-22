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
            return try await existingTask.value
        }

        // Создаём новую задачу обновления
        let tokenProvider = self.tokenProvider
        let identityRepository = self.identityRepository

        let task = Task<String, Error> {
            guard let refreshToken = await tokenProvider.currentRefreshToken else {
                throw BFNetworkingError.sessionExpired
            }

            // Вызываем Identity.CreateToken
            let response = try await identityRepository.createToken(refreshToken: refreshToken)

            // Сохраняем новый access token
            await tokenProvider.saveAccessToken(
                value: response.value,
                expiresAt: response.expirationDate
            )

            return response.value
        }

        refreshTask = task

        do {
            let token = try await task.value
            refreshTask = nil
            return token
        } catch {
            refreshTask = nil
            throw error
        }
    }
}
