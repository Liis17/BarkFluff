//
//  CallEventsStreamManager.swift
//  BFNetworking
//
//  Фоновый device-scope стрим событий звонков с авто-реконнектом.
//  По образцу UpdatesStreamManager / OnlinerStreamManager, но один стрим.
//

import Foundation

public actor CallEventsStreamManager {

    private let callsRepository: CallsRepositoryProtocol
    private let tokenRefreshCoordinator: TokenRefreshCoordinator?

    private var streamTask: Task<Void, Never>?
    private var eventsContinuation: AsyncStream<CallEventDTO>.Continuation?

    /// Поток доменных событий звонков. Пересоздаётся в `start()`.
    public private(set) var events: AsyncStream<CallEventDTO>

    private var isRunning = false
    private static let maxBackoff: TimeInterval = 30

    public init(callsRepository: CallsRepositoryProtocol, tokenRefreshCoordinator: TokenRefreshCoordinator? = nil) {
        self.callsRepository = callsRepository
        self.tokenRefreshCoordinator = tokenRefreshCoordinator

        var cont: AsyncStream<CallEventDTO>.Continuation!
        self.events = AsyncStream { cont = $0 }
        self.eventsContinuation = cont
    }

    public func start() {
        guard !isRunning else { return }
        isRunning = true

        // Пересоздаём поток: после stop() прежний continuation завершён.
        var cont: AsyncStream<CallEventDTO>.Continuation!
        events = AsyncStream { cont = $0 }
        eventsContinuation = cont

        streamTask = Task { [weak self] in
            guard let self else { return }
            var backoff: TimeInterval = 1
            while !Task.isCancelled {
                do {
                    let stream = try await self.subscribe()
                    backoff = 1
                    for try await event in stream {
                        await self.emit(event)
                    }
                    // Стрим завершился штатно — реконнектимся после паузы.
                } catch {
                    if Task.isCancelled { break }
                    await self.handleError(error)
                }
                if Task.isCancelled { break }
                try? await Task.sleep(for: .seconds(backoff))
                backoff = min(backoff * 2, Self.maxBackoff)
            }
        }
    }

    public func stop() {
        isRunning = false
        streamTask?.cancel()
        streamTask = nil
        eventsContinuation?.finish()
    }

    // MARK: - Private (actor-isolated)

    private func subscribe() async throws -> AsyncThrowingStream<CallEventDTO, Error> {
        try await callsRepository.subscribeCallEvents()
    }

    private func emit(_ event: CallEventDTO) {
        eventsContinuation?.yield(event)
    }

    private func handleError(_ error: Error) async {
        guard isAuthenticationError(error), let tokenRefreshCoordinator else { return }
        _ = try? await tokenRefreshCoordinator.refreshAccessToken()
    }

    private nonisolated func isAuthenticationError(_ error: Error) -> Bool {
        let s = String(describing: error).lowercased()
        return s.contains("unauthenticated") || s.contains("unauthorized") || s.contains("401")
            || (s.contains("token") && s.contains("invalid"))
            || (s.contains("token") && s.contains("expired"))
    }
}
