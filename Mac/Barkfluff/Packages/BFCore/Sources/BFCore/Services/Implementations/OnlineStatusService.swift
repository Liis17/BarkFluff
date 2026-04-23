//
//  OnlineStatusService.swift
//  BFCore
//
//  Реализация сервиса онлайн-статусов
//

import Foundation
import BFNetworking

/// Сервис управления онлайн-статусами.
/// Оркестрирует OnlinerStreamManager, OnlinerRepository и OnlineStatusCache.
public actor OnlineStatusService: OnlineStatusServiceProtocol {

    // MARK: - Dependencies

    private let onlinerRepository: OnlinerRepositoryProtocol
    private let streamManager: OnlinerStreamManager
    private let cache: OnlineStatusCache

    // MARK: - State

    private var isActiveValue = false
    private var forwardEventsTask: Task<Void, Never>?
    private var forwardConnectionTask: Task<Void, Never>?

    // MARK: - Streams

    private var eventsContinuation: AsyncStream<OnlineStatusEvent>.Continuation?
    private var eventsStream: AsyncStream<OnlineStatusEvent>?

    private var connectionContinuation: AsyncStream<OnlineStatusConnectionEvent>.Continuation?
    private var connectionStream: AsyncStream<OnlineStatusConnectionEvent>?

    // MARK: - Init

    public init(
        onlinerRepository: OnlinerRepositoryProtocol,
        streamManager: OnlinerStreamManager,
        cache: OnlineStatusCache
    ) {
        self.onlinerRepository = onlinerRepository
        self.streamManager = streamManager
        self.cache = cache
    }

    // MARK: - Lifecycle

    public func start(initialUserIDs: [Int64]) async {
        if isActiveValue {
            // Уже запущен — просто добавляем новых пользователей
            await addToTracking(initialUserIDs)
            return
        }
        isActiveValue = true

        // 1. Запрашиваем начальные статусы через GetOnlineStatus (batch)
        if !initialUserIDs.isEmpty {
            do {
                let statuses = try await onlinerRepository.getOnlineStatus(userIDs: initialUserIDs)
                let events = statuses.map { mapToEvent($0) }
                await cache.updateStatuses(events)

                // Прокидываем начальные статусы в stream для UI
                for event in events {
                    eventsContinuation?.yield(event)
                }
            } catch {
                // Не критично — статусы придут по стриму
            }
        }

        // 2. Запускаем стрим-менеджер (heartbeat + подписка)
        await streamManager.start(userIDs: initialUserIDs)

        // 3. Форвардим события из стрим-менеджера в кеш и наш stream
        startForwardingEvents()
        startForwardingConnectionEvents()
    }

    public func stop() async {
        guard isActiveValue else { return }
        isActiveValue = false

        forwardEventsTask?.cancel()
        forwardEventsTask = nil

        forwardConnectionTask?.cancel()
        forwardConnectionTask = nil

        await streamManager.stop()
    }

    // MARK: - Status Access

    public func getStatus(for userID: Int64) async -> OnlineStatus {
        await cache.getStatus(for: userID)
    }

    public func fetchStatuses(for userIDs: [Int64]) async {
        // Определяем каких нет в кеше
        var missingIDs: [Int64] = []
        for id in userIDs {
            let status = await cache.getStatus(for: id)
            if case .unknown = status {
                missingIDs.append(id)
            }
        }

        guard !missingIDs.isEmpty else { return }

        do {
            let statuses = try await onlinerRepository.getOnlineStatus(userIDs: missingIDs)
            let events = statuses.map { mapToEvent($0) }
            await cache.updateStatuses(events)

            for event in events {
                eventsContinuation?.yield(event)
            }
        } catch {
            // Не критично — повторим при следующей возможности
        }
    }

    // MARK: - Subscription Management

    /// Добавить пользователей к отслеживаемым (инкрементально)
    public func addToTracking(_ userIDs: [Int64]) async {
        guard !userIDs.isEmpty else { return }

        // Добавляем в стрим-менеджер
        await streamManager.addToTracking(userIDs)

        // Запрашиваем текущие статусы для новых пользователей
        await fetchStatuses(for: userIDs)
    }

    /// Удалить пользователей из отслеживаемых (инкрементально)
    public func removeFromTracking(_ userIDs: [Int64]) async {
        await streamManager.removeFromTracking(userIDs)
    }

    /// Полная замена списка отслеживаемых пользователей
    public func replaceTrackedUsers(_ userIDs: [Int64]) async {
        // Обновляем подписку на стриме
        await streamManager.replaceTrackedUsers(userIDs)

        // Запрашиваем текущие статусы для новых пользователей
        await fetchStatuses(for: userIDs)
    }

    /// Получить текущий список отслеживаемых пользователей
    public func getTrackedUserIDs() async -> Set<Int64> {
        await streamManager.getTrackedUserIDs()
    }

    // MARK: - Streams

    public func getStatusEventsStream() async -> AsyncStream<OnlineStatusEvent> {
        if let existing = eventsStream {
            return existing
        }

        let stream = AsyncStream<OnlineStatusEvent> { continuation in
            self.eventsContinuation = continuation
        }
        eventsStream = stream
        return stream
    }

    public func getConnectionEventsStream() async -> AsyncStream<OnlineStatusConnectionEvent> {
        if let existing = connectionStream {
            return existing
        }

        let stream = AsyncStream<OnlineStatusConnectionEvent> { continuation in
            self.connectionContinuation = continuation
        }
        connectionStream = stream
        return stream
    }

    public func isActive() async -> Bool {
        isActiveValue
    }

    // MARK: - Private

    private func startForwardingEvents() {
        forwardEventsTask = Task { [weak self] in
            guard let self else { return }

            let managerStream = await self.streamManager.onlineStatusEvents

            for await info in managerStream {
                if Task.isCancelled { break }

                let event = self.mapToEvent(info)

                // Обновляем кеш
                await self.cache.updateStatus(userID: event.userID, status: event.status)

                // Прокидываем в наш stream для UI
                let cont = await self.eventsContinuation
                cont?.yield(event)
            }
        }
    }

    private func startForwardingConnectionEvents() {
        forwardConnectionTask = Task { [weak self] in
            guard let self else { return }

            let managerStream = await self.streamManager.connectionEvents

            for await event in managerStream {
                if Task.isCancelled { break }

                let domainEvent: OnlineStatusConnectionEvent
                switch event {
                case .connectionLost: domainEvent = .connectionLost
                case .reconnected: domainEvent = .reconnected
                }

                let cont = await self.connectionContinuation
                cont?.yield(domainEvent)
            }
        }
    }

    private nonisolated func mapToEvent(_ info: UserOnlineStatusInfo) -> OnlineStatusEvent {
        let status: OnlineStatus
        switch info.status {
        case .online:
            status = .online
        case .offline:
            status = .offline(lastSeen: info.lastSeen ?? Date())
        case .unknown:
            status = .unknown
        }

        return OnlineStatusEvent(userID: info.userID, status: status)
    }
}
