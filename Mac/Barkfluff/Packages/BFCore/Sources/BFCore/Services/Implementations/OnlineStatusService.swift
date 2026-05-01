//
//  OnlineStatusService.swift
//  BFCore
//
//  Реализация сервиса онлайн-статусов
//

import Foundation
import BFNetworking

/// Сервис управления онлайн-статусами.
///
/// Архитектура: per-user multicast + ref-counted tracking.
///
/// Каждый консумер (View / ViewModel) регистрирует свою заинтересованность через
/// `track(userID)` (обязательный парный `untrack`) и подписывается на индивидуальный
/// поток через `statusStream(for:)`. Когда статус меняется, broadcast идёт ТОЛЬКО
/// подписчикам конкретного userID — это исключает массовые re-render'ы UI.
///
/// Все обновления статуса проходят через `applyStatus(userID:newStatus:)` с dedup'ом:
/// если значение в кеше уже совпадает, событие не публикуется.
public actor OnlineStatusService: OnlineStatusServiceProtocol {

    // MARK: - Dependencies

    private let onlinerRepository: OnlinerRepositoryProtocol
    private let streamManager: OnlinerStreamManager
    private let cache: OnlineStatusCache

    // MARK: - State

    private var isActiveValue = false
    private var forwardEventsTask: Task<Void, Never>?
    private var forwardConnectionTask: Task<Void, Never>?

    // MARK: - Subscriptions

    /// Per-user multicast: каждому userID соответствует словарь активных подписчиков.
    /// При изменении статуса userID broadcast идёт ТОЛЬКО его подписчикам.
    private var perUserSubscribers: [Int64: [UUID: AsyncStream<OnlineStatus>.Continuation]] = [:]

    /// Ref-counted tracking: сколько UI-консумеров заинтересовано в конкретном userID.
    /// Когда счётчик достигает 0 — пользователь удаляется из gRPC-подписки.
    private var trackingRefcount: [Int64: Int] = [:]

    // MARK: - Connection Stream

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
            // Сервис уже работает — bootstrap-fetch для свежих юзеров,
            // прогревает кеш до того как row'ы вызовут track.
            await warmupCache(for: initialUserIDs)
            return
        }
        isActiveValue = true

        // Запускаем стрим-менеджер (heartbeat + gRPC subscription).
        await streamManager.start(userIDs: initialUserIDs)

        // Форвардим события из стрима через единую точку applyStatus (с dedup'ом).
        startForwardingEvents()
        startForwardingConnectionEvents()

        // Warmup кеша: подписчики ещё не зарегистрированы, broadcast уйдёт в пустоту,
        // зато currentStatus(for:) вернёт сразу актуальное значение.
        await warmupCache(for: initialUserIDs)
    }

    public func stop() async {
        guard isActiveValue else { return }
        isActiveValue = false

        forwardEventsTask?.cancel()
        forwardEventsTask = nil

        forwardConnectionTask?.cancel()
        forwardConnectionTask = nil

        // Финализируем все per-user continuations — освобождаем Task'и подписчиков.
        for bucket in perUserSubscribers.values {
            for continuation in bucket.values {
                continuation.finish()
            }
        }
        perUserSubscribers.removeAll()
        trackingRefcount.removeAll()

        connectionContinuation?.finish()
        connectionContinuation = nil
        connectionStream = nil

        // Сбрасываем кеш чтобы следующий start() получил свежие статусы.
        await cache.removeAll()

        await streamManager.stop()
    }

    // MARK: - Status Access

    public func currentStatus(for userID: Int64) async -> OnlineStatus {
        await cache.getStatus(for: userID)
    }

    // MARK: - Per-User Streams

    public func statusStream(for userID: Int64) async -> AsyncStream<OnlineStatus> {
        AsyncStream<OnlineStatus>(bufferingPolicy: .bufferingNewest(1)) { continuation in
            let id = UUID()
            // Регистрация — асинхронная (заходим в actor), но AsyncStream
            // буферизует yield'и до момента когда консумер начнёт их читать.
            Task { await self.registerSubscriber(userID: userID, id: id, continuation: continuation) }
            continuation.onTermination = { [weak self] _ in
                guard let self else { return }
                Task { await self.unregisterSubscriber(userID: userID, id: id) }
            }
        }
    }

    private func registerSubscriber(
        userID: Int64,
        id: UUID,
        continuation: AsyncStream<OnlineStatus>.Continuation
    ) {
        var bucket = perUserSubscribers[userID, default: [:]]
        bucket[id] = continuation
        perUserSubscribers[userID] = bucket
    }

    private func unregisterSubscriber(userID: Int64, id: UUID) {
        guard var bucket = perUserSubscribers[userID] else { return }
        bucket.removeValue(forKey: id)
        if bucket.isEmpty {
            perUserSubscribers.removeValue(forKey: userID)
        } else {
            perUserSubscribers[userID] = bucket
        }
    }

    // MARK: - Tracking (Ref-Counted)

    public func track(_ userID: Int64) async {
        let prev = trackingRefcount[userID] ?? 0
        trackingRefcount[userID] = prev + 1

        // Только при первом track для этого userID — добавляем в gRPC-подписку
        // и делаем authoritative fetch.
        if prev == 0 {
            await streamManager.addToTracking([userID])
            await fetchStatusAuthoritative(userIDs: [userID])
        }
    }

    public func untrack(_ userID: Int64) async {
        guard let cur = trackingRefcount[userID], cur > 0 else { return }
        if cur == 1 {
            trackingRefcount.removeValue(forKey: userID)
            await streamManager.removeFromTracking([userID])
        } else {
            trackingRefcount[userID] = cur - 1
        }
    }

    public func track(_ userIDs: [Int64]) async {
        guard !userIDs.isEmpty else { return }
        var newToTrack: [Int64] = []
        for id in userIDs {
            let prev = trackingRefcount[id] ?? 0
            trackingRefcount[id] = prev + 1
            if prev == 0 { newToTrack.append(id) }
        }
        if !newToTrack.isEmpty {
            await streamManager.addToTracking(newToTrack)
            await fetchStatusAuthoritative(userIDs: newToTrack)
        }
    }

    public func untrack(_ userIDs: [Int64]) async {
        guard !userIDs.isEmpty else { return }
        var toRemove: [Int64] = []
        for id in userIDs {
            guard let cur = trackingRefcount[id], cur > 0 else { continue }
            if cur == 1 {
                trackingRefcount.removeValue(forKey: id)
                toRemove.append(id)
            } else {
                trackingRefcount[id] = cur - 1
            }
        }
        if !toRemove.isEmpty {
            await streamManager.removeFromTracking(toRemove)
        }
    }

    // MARK: - Connection Stream

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

    // MARK: - Private: Status Application

    /// Единая точка применения статуса. Защищает от лишних re-render'ов через dedup:
    /// если значение в кеше совпадает — событие не публикуется.
    private func applyStatus(userID: Int64, newStatus: OnlineStatus) async {
        let old = await cache.getStatus(for: userID)
        guard old != newStatus else { return }
        await cache.updateStatus(userID: userID, status: newStatus)

        if let bucket = perUserSubscribers[userID] {
            for continuation in bucket.values {
                continuation.yield(newStatus)
            }
        }
    }

    /// Authoritative fetch: всегда перезаписывает кеш свежим значением с сервера.
    /// В отличие от старого `fetchStatuses`, не пропускает уже закешированные значения —
    /// stale `.online` будет корректно заменён свежим `.offline`.
    private func fetchStatusAuthoritative(userIDs: [Int64]) async {
        guard !userIDs.isEmpty else { return }
        do {
            let statuses = try await onlinerRepository.getOnlineStatus(userIDs: userIDs)
            for info in statuses {
                let event = mapToEvent(info)
                await applyStatus(userID: event.userID, newStatus: event.status)
            }
        } catch {
            // Не критично — повторим при следующей возможности (track / reconnect).
        }
    }

    /// Прогрев кеша при старте сервиса: получаем статусы и кладём в кеш
    /// (через applyStatus с dedup'ом — если кеш пуст, broadcast уйдёт в пустые
    /// per-user buckets, что корректно).
    private func warmupCache(for userIDs: [Int64]) async {
        await fetchStatusAuthoritative(userIDs: userIDs)
    }

    // MARK: - Private: Forwarding

    private func startForwardingEvents() {
        forwardEventsTask = Task { [weak self] in
            guard let self else { return }

            let managerStream = await self.streamManager.onlineStatusEvents

            for await info in managerStream {
                if Task.isCancelled { break }
                let event = self.mapToEvent(info)
                await self.applyStatus(userID: event.userID, newStatus: event.status)
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
                case .connectionLost:
                    domainEvent = .connectionLost
                case .reconnected:
                    domainEvent = .reconnected
                    // Стрим — delta-only, после реконнекта статусы изменившиеся
                    // во время разрыва теряются. Делаем authoritative refresh.
                    // applyStatus с dedup'ом гарантирует, что не-изменившиеся
                    // юзеры не вызовут лишних UI-обновлений.
                    await self.refreshAllTrackedStatuses()
                }

                let cont = await self.connectionContinuation
                cont?.yield(domainEvent)
            }
        }
    }

    /// Authoritative refresh всех отслеживаемых юзеров. Вызывается после reconnect.
    private func refreshAllTrackedStatuses() async {
        let trackedIDs = Array(trackingRefcount.keys)
        guard !trackedIDs.isEmpty else { return }
        await fetchStatusAuthoritative(userIDs: trackedIDs)
    }

    // MARK: - Private: Mapping

    private nonisolated func mapToEvent(_ info: UserOnlineStatusInfo) -> OnlineStatusEvent {
        let status: OnlineStatus
        switch info.status {
        case .online:
            status = .online
        case .offline:
            status = .offline(lastSeen: info.lastSeen)
        case .unknown:
            status = .unknown
        }

        return OnlineStatusEvent(userID: info.userID, status: status)
    }
}
