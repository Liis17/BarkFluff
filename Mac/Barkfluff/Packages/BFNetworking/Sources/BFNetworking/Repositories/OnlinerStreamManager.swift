//
//  OnlinerStreamManager.swift
//  BFNetworking
//
//  Управляет streaming-подпиской на онлайн-статусы и heartbeat
//

import Foundation

// MARK: - OnlinerStreamManager

/// Управляет streaming-подпиской на онлайн-статусы и heartbeat.
/// По аналогии с UpdatesStreamManager: exponential backoff, reconnect, AsyncStream.
public actor OnlinerStreamManager {

    // MARK: - Types

    public enum ConnectionEvent: Sendable {
        case connectionLost
        case reconnected
    }

    // MARK: - Dependencies

    private let onlinerRepository: OnlinerRepositoryProtocol
    private let tokenRefreshCoordinator: TokenRefreshCoordinator?

    // MARK: - Tasks

    private var subscriptionTask: Task<Void, Never>?
    private var heartbeatTask: Task<Void, Never>?

    // MARK: - Streams

    private var statusContinuation: AsyncStream<UserOnlineStatusInfo>.Continuation?
    private var connectionContinuation: AsyncStream<ConnectionEvent>.Continuation?

    public private(set) var onlineStatusEvents: AsyncStream<UserOnlineStatusInfo>
    public private(set) var connectionEvents: AsyncStream<ConnectionEvent>

    // MARK: - State

    private var trackedUserIDs: Set<Int64> = []
    private var isRunning = false

    // MARK: - Constants

    /// Интервал heartbeat в секундах.
    /// Бэкенд OfflineDetectionService проверяет каждые 3 секунды,
    /// порог offline — 5 секунд. Heartbeat каждые 3 секунды гарантирует
    /// что LastSeen всегда < 5 секунд.
    private static let heartbeatInterval: TimeInterval = 3

    /// Максимальный backoff при переподключении стрима
    private static let maxBackoff: TimeInterval = 30

    // MARK: - Init

    public init(onlinerRepository: OnlinerRepositoryProtocol, tokenRefreshCoordinator: TokenRefreshCoordinator? = nil) {
        self.onlinerRepository = onlinerRepository
        self.tokenRefreshCoordinator = tokenRefreshCoordinator

        var statusCont: AsyncStream<UserOnlineStatusInfo>.Continuation!
        self.onlineStatusEvents = AsyncStream { continuation in
            statusCont = continuation
        }
        self.statusContinuation = statusCont

        var connCont: AsyncStream<ConnectionEvent>.Continuation!
        self.connectionEvents = AsyncStream { continuation in
            connCont = continuation
        }
        self.connectionContinuation = connCont
    }

    // MARK: - Public API

    /// Запустить подписку на статусы и heartbeat
    /// - Parameter userIDs: начальный список отслеживаемых пользователей
    public func start(userIDs: [Int64]) async {
        guard !isRunning else {
            // Уже запущен — просто добавляем новых пользователей
            await addToTracking(userIDs)
            return
        }
        print("🔄 [OnlinerStreamManager] Starting with \(userIDs.count) users...")
        isRunning = true
        trackedUserIDs = Set(userIDs)

        startHeartbeat()
        startSubscription()
    }

    /// Остановить все подписки и heartbeat
    public func stop() {
        print("🛑 [OnlinerStreamManager] Stopping...")
        isRunning = false
        trackedUserIDs.removeAll()

        heartbeatTask?.cancel()
        heartbeatTask = nil

        subscriptionTask?.cancel()
        subscriptionTask = nil

        statusContinuation?.finish()
        connectionContinuation?.finish()
        print("✅ [OnlinerStreamManager] Stopped")
    }

    /// Получить текущий список отслеживаемых пользователей
    public func getTrackedUserIDs() -> Set<Int64> {
        trackedUserIDs
    }

    /// Добавить пользователей к отслеживаемым (инкрементально)
    /// Вызывает ChangeUsersInSubscription на сервере без переподключения стрима
    public func addToTracking(_ userIDs: [Int64]) async {
        let newUsers = Set(userIDs).subtracting(trackedUserIDs)
        guard !newUsers.isEmpty else { return }

        trackedUserIDs.formUnion(newUsers)

        // Обновляем подписку на сервере
        await updateSubscriptionOnServer()
    }

    /// Удалить пользователей из отслеживаемых (инкрементально)
    /// Вызывает ChangeUsersInSubscription на сервере без переподключения стрима
    public func removeFromTracking(_ userIDs: [Int64]) async {
        let usersToRemove = Set(userIDs).intersection(trackedUserIDs)
        guard !usersToRemove.isEmpty else { return }

        trackedUserIDs.subtract(usersToRemove)

        // Обновляем подписку на сервере
        await updateSubscriptionOnServer()
    }

    /// Полная замена списка отслеживаемых пользователей
    /// Вызывает ChangeUsersInSubscription на сервере без переподключения стрима
    public func replaceTrackedUsers(_ userIDs: [Int64]) async {
        let newSet = Set(userIDs)
        guard newSet != trackedUserIDs else { return }
        trackedUserIDs = newSet

        // Обновляем подписку на сервере
        await updateSubscriptionOnServer()
    }

    // MARK: - Private

    /// Обновить подписку на сервере через ChangeUsersInSubscription
    private func updateSubscriptionOnServer() async {
        guard isRunning else { return }

        do {
            if trackedUserIDs.isEmpty {
                // Если список пуст — не шлём запрос, сервер сам закроет стрим при отсутствии активности
                return
            }
            try await onlinerRepository.changeUsersInSubscription(userIDs: Array(trackedUserIDs))
        } catch {
            // Если подписки нет (FailedPrecondition) — переподключение произойдёт
            // автоматически, и при следующем connect используется актуальный trackedUserIDs
        }
    }

    // MARK: - Heartbeat

    private func startHeartbeat() {
        print("💓 [OnlinerStreamManager] Starting heartbeat (interval: \(OnlinerStreamManager.heartbeatInterval)s)...")
        heartbeatTask = Task { [weak self] in
            guard let self else { return }
            var consecutiveErrors = 0

            while !Task.isCancelled {
                do {
                    try await self.onlinerRepository.setOnlineStatus()
                    if consecutiveErrors > 0 {
                        print("✅ [OnlinerStreamManager] Heartbeat recovered after \(consecutiveErrors) errors")
                        consecutiveErrors = 0
                    }
                } catch {
                    consecutiveErrors += 1
                    // Ошибка heartbeat не критична — будет retry через интервал.
                    // Не ломаем цикл.
                    if consecutiveErrors <= 3 || consecutiveErrors % 10 == 0 {
                        print("⚠️ [OnlinerStreamManager] Heartbeat error #\(consecutiveErrors): \(error.localizedDescription)")
                    }
                    if Task.isCancelled { break }
                }

                try? await Task.sleep(for: .seconds(OnlinerStreamManager.heartbeatInterval))
            }
            print("🛑 [OnlinerStreamManager] Heartbeat task cancelled")
        }
    }

    // MARK: - Subscription with Reconnect

    private func startSubscription() {
        print("📡 [OnlinerStreamManager] Starting subscription...")
        subscriptionTask = Task { [weak self] in
            guard let self else { return }
            var backoff: TimeInterval = 1
            var hasConnectedBefore = false
            var consecutiveErrors = 0

            while !Task.isCancelled {
                do {
                    // При каждом (пере)подключении используем актуальный trackedUserIDs
                    let currentIDs = await self.trackedUserIDs
                    guard !currentIDs.isEmpty else {
                        // Нет пользователей для отслеживания — подождём и проверим снова
                        try? await Task.sleep(for: .seconds(2))
                        continue
                    }

                    print("📡 [OnlinerStreamManager] Connecting to onlineStatus stream (attempt \(consecutiveErrors + 1), \(currentIDs.count) users)...")
                    let stream = try await self.onlinerRepository
                        .subscribeToOnlineStatus(userIDs: Array(currentIDs))

                    backoff = 1 // Reset on successful connect
                    consecutiveErrors = 0
                    print("✅ [OnlinerStreamManager] onlineStatus stream connected")

                    if hasConnectedBefore {
                        await self.emitConnectionEvent(.reconnected)
                        print("🔄 [OnlinerStreamManager] Reconnected event sent")
                    }
                    hasConnectedBefore = true

                    for try await event in stream {
                        let cont = await self.statusContinuation
                        cont?.yield(event)
                    }

                    // Stream ended normally — treat as connection lost
                    if !Task.isCancelled, hasConnectedBefore {
                        print("⚠️ [OnlinerStreamManager] Stream ended normally, treating as connection lost")
                        await self.emitConnectionEvent(.connectionLost)
                    }

                } catch {
                    if Task.isCancelled { break }
                    consecutiveErrors += 1

                    let isAuthError = self.isAuthenticationError(error)
                    print("❌ [OnlinerStreamManager] Subscription error (#\(consecutiveErrors)): \(error.localizedDescription)")
                    print("   isAuthError: \(isAuthError), backoff: \(backoff)s")

                    // Если ошибка авторизации — пробуем обновить токен
                    if isAuthError {
                        print("🔐 [OnlinerStreamManager] Attempting token refresh due to auth error...")
                        if let coordinator = await self.tokenRefreshCoordinator {
                            do {
                                _ = try await coordinator.refreshAccessToken()
                                print("✅ [OnlinerStreamManager] Token refreshed successfully")
                            } catch {
                                print("❌ [OnlinerStreamManager] Token refresh failed: \(error.localizedDescription)")
                            }
                        } else {
                            print("⚠️ [OnlinerStreamManager] No tokenRefreshCoordinator available")
                        }
                    }

                    if hasConnectedBefore {
                        await self.emitConnectionEvent(.connectionLost)
                    }
                    hasConnectedBefore = true
                    try? await Task.sleep(for: .seconds(backoff))
                    backoff = min(backoff * 2, OnlinerStreamManager.maxBackoff)
                }
            }
            print("🛑 [OnlinerStreamManager] Subscription task cancelled")
        }
    }

    // MARK: - Error Detection

    private nonisolated func isAuthenticationError(_ error: Error) -> Bool {
        let errorString = String(describing: error).lowercased()
        return errorString.contains("unauthenticated") ||
               errorString.contains("unauthorized") ||
               errorString.contains("401") ||
               errorString.contains("token") && errorString.contains("invalid") ||
               errorString.contains("token") && errorString.contains("expired")
    }

    // MARK: - Private

    private func emitConnectionEvent(_ event: ConnectionEvent) {
        connectionContinuation?.yield(event)
    }
}
