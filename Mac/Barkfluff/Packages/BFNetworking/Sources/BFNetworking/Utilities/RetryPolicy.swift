//
//  RetryPolicy.swift
//  BFNetworking
//
//  Политика повторных попыток
//

import Foundation

/// Политика повторных попыток с exponential backoff
public struct RetryPolicy: Sendable {

    /// Максимальное количество попыток
    public let maxAttempts: Int

    /// Начальная задержка в секундах
    public let initialDelay: TimeInterval

    /// Максимальная задержка в секундах
    public let maxDelay: TimeInterval

    /// Множитель для exponential backoff
    public let multiplier: Double

    /// Джиттер (случайное отклонение) для предотвращения thundering herd
    public let jitter: Double

    public init(
        maxAttempts: Int = 3,
        initialDelay: TimeInterval = 1.0,
        maxDelay: TimeInterval = 30.0,
        multiplier: Double = 2.0,
        jitter: Double = 0.1
    ) {
        self.maxAttempts = maxAttempts
        self.initialDelay = initialDelay
        self.maxDelay = maxDelay
        self.multiplier = multiplier
        self.jitter = jitter
    }

    /// Стандартная политика для unary запросов
    public static let standard = RetryPolicy(
        maxAttempts: 3,
        initialDelay: 1.0,
        maxDelay: 10.0
    )

    /// Политика для streaming (более агрессивная)
    public static let streaming = RetryPolicy(
        maxAttempts: 10,
        initialDelay: 1.0,
        maxDelay: 30.0
    )

    /// Политика без повторных попыток
    public static let noRetry = RetryPolicy(maxAttempts: 1)

    /// Рассчитать задержку для конкретной попытки
    public func delay(for attempt: Int) -> TimeInterval {
        guard attempt > 0 else { return 0 }

        // Exponential backoff: initialDelay * multiplier^(attempt-1)
        let baseDelay = initialDelay * pow(multiplier, Double(attempt - 1))

        // Ограничение максимальной задержкой
        let cappedDelay = min(baseDelay, maxDelay)

        // Добавляем джиттер
        let jitterRange = cappedDelay * jitter
        let jitterValue = TimeInterval.random(in: -jitterRange...jitterRange)

        return max(0, cappedDelay + jitterValue)
    }

    /// Можно ли делать ещё попытки
    public func canRetry(attempt: Int) -> Bool {
        attempt < maxAttempts
    }
}

/// Исполнитель повторных попыток
public actor RetryExecutor {

    private let policy: RetryPolicy

    public init(policy: RetryPolicy = .standard) {
        self.policy = policy
    }

    /// Выполнить операцию с повторными попытками
    public func execute<T: Sendable>(
        operation: @Sendable () async throws -> T,
        shouldRetry: @Sendable (Error) -> Bool = { _ in true }
    ) async throws -> T {
        var attempt = 0

        while true {
            do {
                return try await operation()
            } catch {
                attempt += 1

                // Проверяем, можно ли повторить
                if !policy.canRetry(attempt: attempt) || !shouldRetry(error) {
                    throw error
                }

                // Ждём перед следующей попыткой
                let delay = policy.delay(for: attempt)
                try await Task.sleep(for: .seconds(delay))
            }
        }
    }
}
